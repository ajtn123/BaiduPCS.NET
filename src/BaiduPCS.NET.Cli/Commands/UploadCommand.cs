using System.Collections.Concurrent;
using System.Diagnostics;
using BaiduPCS.Netdisk;
using BaiduPCS.Transfers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class UploadCommand : RemoteCommand<UploadCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<path>")] public string[] Paths { get; init; } = [];
        [CommandOption("--parallel <WORKERS>")] public int? Parallel { get; init; }
        [CommandOption("--file-concurrency <FILES>")] public int? FileConcurrency { get; init; }
        [CommandOption("--retries <COUNT>")] public int Retries { get; init; } = 3;
        [CommandOption("--skip-rapid-check")] public bool SkipRapidCheck { get; init; }
        [CommandOption("--rapid-check")] public bool RapidCheck { get; init; }
        [CommandOption("--remove-source")] public bool RemoveSource { get; init; }
        [CommandOption("--stdin")] public bool StandardInput { get; init; }
        [CommandOption("--conflict <POLICY>")] public string? Conflict { get; init; }
        [CommandOption("--as <NAME>")] public string? RemoteName { get; init; }

        public override ValidationResult Validate() => StandardInput && (Paths.Length != 1 || RemoveSource || RemoteName is not null)
            ? ValidationResult.Error("Use upload - with one remote file path and without --as or --remove-source.")
            : !StandardInput && Paths.Length < 2
            ? ValidationResult.Error("Specify at least one local source, followed by the remote directory.")
            : RemoteName is not null && (Paths.Length != 2 || RemoteName is "" or "." or ".." || RemoteName.Contains('/'))
                ? ValidationResult.Error("--as requires one local file and a name within the remote directory.")
            : SkipRapidCheck && RapidCheck ? ValidationResult.Error("Choose either --rapid-check or --skip-rapid-check.")
            : Parallel is < 1 || FileConcurrency is < 1 || Retries < 0
                ? ValidationResult.Error("Concurrency must be positive and retries cannot be negative.") : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (profile, client) = await OpenProfileAsync(settings, cancellationToken);
        using (client)
        {
            string workingDirectory = profile.RequireAccount().WorkingDirectory;
            var options = new UploadOptions
            {
                Parallel = settings.Parallel ?? profile.Settings.UploadParallel,
                Retries = settings.Retries,
                SkipRapidCheck = settings.SkipRapidCheck || !settings.RapidCheck && profile.Settings.SkipRapidCheck,
                RemoveSource = settings.RemoveSource,
                Conflict = settings.Conflict is null ? profile.Settings.UploadConflict : SetConfigCommand.ParseConflict(settings.Conflict),
            };
            if (settings.StandardInput)
                return await UploadInputAsync(client, workingDirectory, settings.Paths[^1], options, cancellationToken);

            string remote = await client.MatchOneAsync(workingDirectory, settings.Paths[^1], cancellationToken);
            string checkpoints = Path.Combine(Store.DirectoryPath, "uploads");
            var failures = new ConcurrentQueue<string>();
            UploadItem[] files = EnumerateFiles(settings.Paths[..^1], remote, settings.RemoteName,
                profile.Settings.CheckUploadNames, settings.RemoveSource, failures, cancellationToken);
            if (settings.RemoveSource && files.Select(item => item.File.FullName).Distinct(LocalPathComparer).Count() != files.Length)
                throw new ArgumentException("--remove-source requires each local file to have just one remote target.");
            if (files.Length == 0)
            {
                foreach (string failure in failures)
                    Console.Error.WriteLine(failure);
                throw new InvalidOperationException("No nonempty local files were found to upload.");
            }

            var timer = Stopwatch.StartNew();
            var display = new OperationDisplay(settings, "File", settings.Verbose);
            var numbers = new DisplayNumbers(settings.RawNumbers);
            var queue = files.Select(item => (Item: item, Row: display.Enqueue(item.Target, item.Length))).ToArray();
            long totalBytes = 0;
            int failed = 0;
            var parallel = new ParallelOptions
            {
                MaxDegreeOfParallelism = settings.FileConcurrency ?? profile.Settings.UploadConcurrency,
                CancellationToken = cancellationToken,
            };
            await display.RunAsync(() => Parallel.ForEachAsync(queue, parallel, async (work, token) =>
            {
                UploadItem item = work.Item;
                OperationDisplay.Item row = display.Begin(work.Row);
                try
                {
                    UploadResult result = await client.UploadAsync(item.File.FullName, item.Target, checkpoints, options,
                        display.TrackProgress ? progress => display.Report(row, progress.CompletedBytes,
                            progress.TotalBytes, progress.Part, resumed: progress.Part < 0) : null, token);
                    if (result != UploadResult.Skipped)
                        Interlocked.Add(ref totalBytes, item.Length);

                    string label = result switch
                    {
                        UploadResult.RapidUpload => "Rapid upload",
                        UploadResult.Skipped => "Skipped",
                        _ => "Uploaded",
                    };
                    string plain = $"{label} {item.Target}";
                    if (settings.RemoveSource)
                    {
                        label += ", removed local source";
                        plain += $" (removed {item.File.FullName})";
                    }
                    display.End(row, label, plain: plain);
                }
                catch (Exception error) when (error is IOException or InvalidDataException or HttpRequestException or BaiduApiException
                    or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                    || error is OperationCanceledException && !token.IsCancellationRequested)
                {
                    string message = error is BaiduApiException or InvalidDataException or ArgumentException or InvalidOperationException
                        ? error.Message : error.GetType().Name;
                    Interlocked.Increment(ref failed);
                    display.End(row, message, failed: true, plain: $"{item.Target}: {message}");
                }
            }));

            Console.WriteLine($"Processed {numbers.Bytes(totalBytes)} in {numbers.Duration(timer.Elapsed.TotalSeconds)}.");
            foreach (string failure in failures)
                Console.Error.WriteLine(failure);

            return failures.IsEmpty && failed == 0 ? 0 : 1;
        }
    }

    private static async Task<int> UploadInputAsync(NetdiskClient client, string workingDirectory, string target, UploadOptions options,
        CancellationToken token)
    {
        if (!Console.IsInputRedirected)
            throw new ArgumentException("Pipe binary data to upload - and supply the full remote file path.");
        if (target.EndsWith('/') || RemotePath.Name(target) is "" or "." or "..")
            throw new ArgumentException("A piped upload requires a remote file name.");
        string remote = RemotePath.Resolve(workingDirectory, target);
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("baidupcs-upload-stdin-");
        string source = Path.Combine(temporary.FullName, "input");
        try
        {
            await using (Stream input = Console.OpenStandardInput())
            await using (var output = new FileStream(source, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
            {
                var buffer = new byte[128 * 1024];
                int read;
                while ((read = await input.ReadAsync(buffer, token)) != 0)
                {
                    if (output.Length > NetdiskClient.MaximumUploadSize - read)
                        throw new ArgumentException("Piped input exceeds the 128 GiB upload limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                }
            }

            UploadResult result = await client.UploadAsync(source, remote, temporary.FullName, options, cancellationToken: token);
            string status = result switch
            {
                UploadResult.RapidUpload => "Rapid upload",
                UploadResult.Skipped => "Skipped",
                _ => "Uploaded",
            };
            Console.WriteLine($"{status} {remote}");
            return 0;
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    private static UploadItem[] EnumerateFiles(string[] paths, string destination, string? remoteName, bool checkNames, bool removeSource,
        ConcurrentQueue<string> failures,
        CancellationToken token)
    {
        var items = new Dictionary<string, UploadItem>(StringComparer.Ordinal);
        var ancestors = new HashSet<string>(LocalPathComparer);
        string[] sources = paths.SelectMany(path => ExpandLocalPaths(path, token)).ToArray();
        if (remoteName is not null && sources.Length != 1)
            throw new ArgumentException("--as requires exactly one matching local file.");
        foreach (string path in sources)
        {
            string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            FileSystemInfo entry = Directory.Exists(fullPath) ? new DirectoryInfo(fullPath) : new FileInfo(fullPath);
            if (remoteName is not null && entry is DirectoryInfo)
                throw new ArgumentException("--as requires a local file.");
            Visit(entry, RemotePath.Resolve(destination, remoteName ?? entry.Name));
        }

        return [.. items.Values];

        void Visit(FileSystemInfo entry, string target)
        {
            token.ThrowIfCancellationRequested();
            if (checkNames && RemotePath.Name(target).IndexOfAny(['<', '>', '|', ':', '"', '*', '?', '\\']) >= 0)
            {
                failures.Enqueue($"Invalid upload file name: {entry.FullName}");
                return;
            }

            try
            {
                FileSystemInfo? linked = entry.ResolveLinkTarget(returnFinalTarget: true);
                if (linked is not null && removeSource)
                {
                    failures.Enqueue($"Cannot remove a source reached through a symbolic link: {entry.FullName}");
                    return;
                }
                if (linked is not null && !linked.Exists)
                    return;

                FileSystemInfo source = linked ?? entry;
                if (source is DirectoryInfo directory)
                {
                    if (!ancestors.Add(directory.FullName))
                        return;

                    try
                    {
                        foreach (FileSystemInfo child in directory.EnumerateFileSystemInfos())
                            Visit(child, RemotePath.Resolve(target, child.Name));
                    }
                    finally
                    {
                        ancestors.Remove(directory.FullName);
                    }
                    return;
                }

                if (source is FileInfo file && file.Length > 0)
                {
                    if (items.TryGetValue(target, out UploadItem? existing) && existing.File.FullName != file.FullName)
                        throw new ArgumentException($"Multiple local files map to remote path {target}.");

                    items.TryAdd(target, new UploadItem(file, target, file.Length));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                failures.Enqueue($"{entry.FullName}: {error.GetType().Name}");
            }
        }
    }

    internal static IEnumerable<string> ExpandLocalPaths(string path, CancellationToken token)
    {
        if (Path.Exists(path))
            return [path];

        string root = Path.GetPathRoot(path) ?? "";
        string[] segments = path[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (!segments.Any(GlobSegment.IsPattern))
            return [path];

        var paths = new List<string> { Path.GetFullPath(root.Length == 0 ? "." : root) };
        for (int index = 0; index < segments.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            string segment = segments[index];
            if (!GlobSegment.IsPattern(segment))
            {
                paths = paths.Select(parent => Path.Combine(parent, segment)).Where(Path.Exists).ToList();
                continue;
            }

            var matcher = new GlobSegment(segment, ignoreCase: OperatingSystem.IsWindows());
            var matches = new List<string>();
            foreach (string parent in paths.Where(Directory.Exists))
            {
                foreach (FileSystemInfo entry in new DirectoryInfo(parent).EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    if (matcher.Matches(entry.Name) && (index == segments.Length - 1 || entry is DirectoryInfo))
                        matches.Add(entry.FullName);
                }
            }
            paths = matches;
        }

        if (paths.Count == 0)
            throw new ArgumentException($"The local pattern '{path}' matched no paths.");
        return paths;
    }

    private static StringComparer LocalPathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record UploadItem(FileInfo File, string Target, long Length);
}
