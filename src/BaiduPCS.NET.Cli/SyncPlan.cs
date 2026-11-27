using System.Security.Cryptography;
using BaiduPCS.Netdisk;
using BaiduPCS.Transfers;

namespace BaiduPCS.Cli;

internal enum SyncMode { Upload, Download, TransferMissing, RemoveMissing }
internal enum SyncDirection { Upload, Download, Both }
[Flags]
internal enum SyncComparison { Name = 1, Time = 2, Length = 4, Checksum = 8 }
internal enum SyncAction { Upload, Download, CreateLocalDirectory, CreateRemoteDirectory, RemoveLocal, RemoveRemote }

internal sealed record SyncOptions(SyncMode Mode, SyncDirection Direction, SyncComparison Compare, bool Recursive, string[] Patterns);
internal sealed record SyncFile(string Path, bool IsDirectory, long Length, DateTime Modified);
internal sealed record SyncChange(SyncAction Action, string Relative, SyncFile? Local, RemoteEntry? Remote, string Reason);

internal sealed record SyncPlan(string LocalRoot, string RemoteRoot, IReadOnlyList<SyncChange> Changes, int Skipped)
{
    private const string PartialDirectory = ".baidupcs-partial";

    internal static async Task<SyncPlan> CreateAsync(NetdiskClient client, string localRoot, string remoteRoot, SyncOptions options,
        CancellationToken token)
    {
        StringComparer comparer = options.Mode is SyncMode.RemoveMissing or SyncMode.TransferMissing
            || options.Compare.HasFlag(SyncComparison.Name) ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var filters = options.Patterns.Select(pattern => new SyncGlob(pattern)).ToArray();
        var local = new Dictionary<string, SyncFile>(comparer);
        var remote = new Dictionary<string, RemoteEntry>(comparer);
        var changes = new List<SyncChange>();
        var directories = new Dictionary<(SyncAction, string), SyncChange>();

        localRoot = ResolveLocalRoot(new DirectoryInfo(localRoot), 32);
        RequireSafeParents(localRoot);
        if (ReadLocal(localRoot) is not { IsDirectory: true })
            throw new ArgumentException("The local sync root must be an existing directory.");
        if (remoteRoot != "/" && await client.FindAsync(remoteRoot, token) is not { IsDirectory: true })
            throw new ArgumentException("The remote sync root must be an existing directory.");

        var localFolders = new Queue<string>();
        localFolders.Enqueue(localRoot);
        while (localFolders.TryDequeue(out string? folder))
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(folder))
            {
                token.ThrowIfCancellationRequested();
                if (Path.GetFileName(path).Equals(PartialDirectory, StringComparison.OrdinalIgnoreCase))
                    continue;
                SyncFile file = ReadLocal(path) ?? throw new IOException($"Local path changed while scanning: {path}");
                string relative = Path.GetRelativePath(localRoot, path).Replace(Path.DirectorySeparatorChar, '/');
                if (!local.TryAdd(relative, file))
                    throw new InvalidDataException($"Ambiguous local names at '{relative}'. Include name in --compare.");
                if (options.Recursive && file.IsDirectory)
                    localFolders.Enqueue(path);
            }
        }

        var remoteFolders = new Queue<string>();
        remoteFolders.Enqueue(remoteRoot);
        while (remoteFolders.TryDequeue(out string? folder))
        {
            await foreach (RemoteEntry entry in client.ListCompleteAsync(folder, token))
            {
                if (entry.Name.Equals(PartialDirectory, StringComparison.OrdinalIgnoreCase))
                    continue;
                string relative = entry.Path[(remoteRoot == "/" ? 1 : remoteRoot.Length + 1)..];
                if (!remote.TryAdd(relative, entry))
                    throw new InvalidDataException($"Ambiguous remote names at '{relative}'. Include name in --compare.");
                if (options.Recursive && entry.IsDirectory)
                    remoteFolders.Enqueue(entry.Path);
            }
        }

        HashSet<string>? protectedLocalNames = options.Mode == SyncMode.RemoveMissing && options.Direction == SyncDirection.Download
            ? remote.Keys.ToHashSet(PathComparer) : null;
        int skipped = 0;
        foreach (string relative in local.Keys.Union(remote.Keys, comparer).Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            local.TryGetValue(relative, out SyncFile? localFile);
            remote.TryGetValue(relative, out RemoteEntry? remoteFile);
            bool directory = localFile?.IsDirectory == true || remoteFile?.IsDirectory == true;
            bool both = localFile is not null && remoteFile is not null;

            bool matches = filters.Length == 0 || filters.Any(filter => filter.Matches(relative));
            if (directory && matches && both && localFile!.IsDirectory != remoteFile!.IsDirectory)
                throw new InvalidDataException($"A file and directory share the sync path '{relative}'.");
            if (directory ? !options.Recursive || filters.Length != 0 : !matches)
                continue;

            if (options.Mode == SyncMode.RemoveMissing)
            {
                if (localFile is null && options.Direction == SyncDirection.Upload)
                {
                    CheckLocalName(relative);
                    changes.Add(new(SyncAction.RemoveRemote, relative, null, remoteFile, "Missing locally"));
                }
                else if (remoteFile is null && options.Direction == SyncDirection.Download)
                {
                    CheckLocalName(relative);
                    changes.Add(new(SyncAction.RemoveLocal, relative, localFile, null, "Missing on Netdisk"));
                }
                else if (!directory)
                    skipped++;
                continue;
            }

            string? reason = both
                ? directory || options.Mode == SyncMode.TransferMissing ? null
                    : await DifferenceAsync(client, localFile!, remoteFile!, options.Compare, token)
                : localFile is null ? "Missing locally" : "Missing on Netdisk";

            bool upload = localFile is not null && options.Direction != SyncDirection.Download && (options.Mode == SyncMode.Upload || !both);
            bool download = remoteFile is not null && options.Direction != SyncDirection.Upload && (options.Mode == SyncMode.Download || !both);
            if (reason is null || !upload && !download)
            {
                if (!directory)
                    skipped++;
                continue;
            }

            if (directory)
            {
                AddDirectory(relative, upload);
                continue;
            }

            if (upload && (localFile!.Length == 0 || localFile.Length > NetdiskClient.MaximumUploadSize))
                throw new ArgumentException($"Sync upload requires a nonempty file no larger than 128 GiB: {relative}");
            if (download)
                CheckLocalName(relative, localFile);
            for (string parent = Parent(relative); parent.Length != 0; parent = Parent(parent))
                AddDirectory(parent, upload);

            changes.Add(new(upload ? SyncAction.Upload : SyncAction.Download, relative, localFile, remoteFile, reason));
        }

        IEnumerable<SyncChange> ordered = options.Mode == SyncMode.RemoveMissing
            ? changes.OrderBy(change => change.Local?.IsDirectory == true || change.Remote?.IsDirectory == true)
                .ThenByDescending(change => change.Relative.Count(character => character == '/'))
            : directories.Values.OrderBy(change => change.Relative.Count(character => character == '/')).Concat(changes);
        return new(localRoot, remoteRoot, [.. ordered], skipped);

        void AddDirectory(string relative, bool upload)
        {
            local.TryGetValue(relative, out SyncFile? localFolder);
            remote.TryGetValue(relative, out RemoteEntry? remoteFolder);
            if (localFolder is { IsDirectory: false } || remoteFolder is { IsDirectory: false })
                throw new InvalidDataException($"A file blocks the sync directory '{relative}'.");
            if (upload && remoteFolder is null)
                directories.TryAdd((SyncAction.CreateRemoteDirectory, relative), new(SyncAction.CreateRemoteDirectory,
                    relative, localFolder, null, "Missing on Netdisk"));
            else if (!upload && localFolder is null)
            {
                CheckLocalName(relative);
                directories.TryAdd((SyncAction.CreateLocalDirectory, relative), new(SyncAction.CreateLocalDirectory,
                    relative, null, remoteFolder, "Missing locally"));
            }
        }

        void CheckLocalName(string relative, SyncFile? expected = null)
        {
            string path = LocalPath(localRoot, relative);
            if (ReadLocal(path) is not null && (expected ?? local.GetValueOrDefault(relative)) is null)
                throw new InvalidDataException($"Sync path '{relative}' conflicts with an existing local name.");
            if (protectedLocalNames?.Contains(relative) == true)
                throw new InvalidDataException($"Sync would remove '{relative}' even though a remote name differs only by case.");
        }
    }

    internal static SyncFile? ReadLocal(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        if (attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException($"Sync cannot follow a symbolic link or junction: {path}");
        if (attributes.HasFlag(FileAttributes.Directory))
            return new(path, true, 0, default);
        var info = new FileInfo(path);
        return new(path, false, info.Length, info.LastWriteTimeUtc);
    }

    private static string ResolveLocalRoot(DirectoryInfo directory, int linkBudget)
    {
        if (directory.Parent is not { } parent)
            return directory.FullName;

        string path = Path.Combine(ResolveLocalRoot(parent, linkBudget), directory.Name);
        var resolved = new DirectoryInfo(path);
        if (!resolved.Exists)
            throw new ArgumentException("The local sync root must be an existing directory.");
        if (!resolved.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return path;
        if (linkBudget == 0 || resolved.ResolveLinkTarget(returnFinalTarget: true) is not DirectoryInfo target)
            throw new IOException($"The selected sync root has an unresolved directory link: {path}");
        return ResolveLocalRoot(target, linkBudget - 1);
    }

    internal static void RequireSafeParents(string path)
    {
        for (string? parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
        {
            if (ReadLocal(parent) is { IsDirectory: false })
                throw new IOException($"A file blocks the sync directory: {parent}");
        }
    }

    internal static string LocalPath(string root, string relative)
    {
        foreach (string segment in relative.Split('/'))
        {
            if (DownloadDestination.SafeName(segment) != segment)
                throw new InvalidDataException($"The remote sync name '{relative}' cannot be represented safely on this file system.");
        }
        return DownloadDestination.Resolve(root, relative);
    }

    internal static string RemoteFilePath(string root, string relative) => root == "/" ? "/" + relative : root + "/" + relative;

    private static string Parent(string relative) => relative.Contains('/') ? relative[..relative.LastIndexOf('/')] : "";
    private static StringComparer PathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static async Task<string?> DifferenceAsync(NetdiskClient client, SyncFile local, RemoteEntry remote,
        SyncComparison compare, CancellationToken token)
    {
        if (compare.HasFlag(SyncComparison.Time)
            && new DateTimeOffset(local.Modified).ToUnixTimeSeconds() != (remote.LocalModified ?? remote.Modified).ToUnixTimeSeconds())
            return "Modification time differs";
        if (compare.HasFlag(SyncComparison.Length) && local.Length != remote.Size)
            return "Length differs";
        if (compare.HasFlag(SyncComparison.Checksum))
        {
            await using var stream = File.OpenRead(local.Path);
            string md5 = Convert.ToHexStringLower(await MD5.HashDataAsync(stream, token));
            if (!md5.Equals(await client.GetFileMd5Async(remote, token), StringComparison.OrdinalIgnoreCase))
                return "Checksum differs";
        }
        return null;
    }

    private sealed class SyncGlob
    {
        private readonly GlobSegment?[] _segments;
        private readonly bool _filenameOnly;

        internal SyncGlob(string pattern)
        {
            string[] segments = pattern.Split('/');
            if (segments.Any(segment => segment is "" or "." or ".."))
                throw new ArgumentException("Sync globs require a filename or a relative path with '/' separators.");
            _filenameOnly = segments.Length == 1 && segments[0] != "**";
            _segments = [.. segments.Select(segment => segment == "**" ? null : new GlobSegment(segment))];
        }

        internal bool Matches(string relative)
        {
            string[] names = relative.Split('/');
            if (_filenameOnly)
                return _segments[0]!.Matches(names[^1]);

            var results = new bool?[_segments.Length + 1, names.Length + 1];
            return Match(0, 0);

            bool Match(int pattern, int name) => results[pattern, name] ??= pattern == _segments.Length ? name == names.Length
                : _segments[pattern] is { } matcher
                    ? name < names.Length && matcher.Matches(names[name]) && Match(pattern + 1, name + 1)
                    : Match(pattern + 1, name) || name < names.Length && Match(pattern, name + 1);
        }
    }
}
