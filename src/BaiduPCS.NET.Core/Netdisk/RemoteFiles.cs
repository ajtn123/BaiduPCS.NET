using System.Runtime.CompilerServices;
using System.Text.Json;
using BaiduPCS.Configuration;

namespace BaiduPCS.Netdisk;

public enum FileSort { Name, Time, Size }

public sealed record RemoteEntry(long Id, long AppId, string Path, string Name, bool IsDirectory, long Size,
    DateTimeOffset Created, DateTimeOffset Modified, string Md5, IReadOnlyList<string> Blocks)
{
    public DateTimeOffset? LocalModified { get; init; }
}

public sealed record Quota(long Total, long Used)
{
    public long Free => Math.Max(0, Total - Used);
}

public sealed partial class NetdiskClient : IDisposable
{
    private const int PageSize = 1000;
    private const int PageLimit = 2000;
    private readonly BaiduTransport _transport;
    private readonly Account _account;
    private readonly ClientSettings _settings;

    public NetdiskClient(ClientSettings settings, Account account) : this(settings, account, null) { }

    internal NetdiskClient(ClientSettings settings, Account account, HttpMessageHandler? handler)
    {
        _settings = settings;
        _account = account;
        _transport = new BaiduTransport(settings, account.Credentials, handler);
    }

    public async Task<Quota> GetQuotaAsync(CancellationToken cancellationToken = default)
    {
        JsonElement root = await _transport.JsonAsync(HttpMethod.Get, _transport.Pcs("quota", "info"), "quota", false, cancellationToken);
        long total = root.Number("quota", -1);
        long used = root.Number("used", -1);
        if (total < 0 || used < 0)
            throw new InvalidDataException("Baidu returned incomplete quota data.");

        return new Quota(total, used);
    }

    public IAsyncEnumerable<RemoteEntry> ListAsync(string path, FileSort order = FileSort.Name, bool descending = false,
        CancellationToken cancellationToken = default) => ListDirectoryAsync(path, order, descending, false, cancellationToken);

    public IAsyncEnumerable<RemoteEntry> ListCompleteAsync(string path, CancellationToken cancellationToken = default) =>
        ListDirectoryAsync(path, FileSort.Name, false, true, cancellationToken);

    private async IAsyncEnumerable<RemoteEntry> ListDirectoryAsync(string path, FileSort order, bool descending, bool requireComplete,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string sort = order.ToString().ToLowerInvariant();
        var emitted = new HashSet<long>();
        var paths = requireComplete ? new HashSet<string>(StringComparer.Ordinal) : null;
        for (int page = 1; page <= PageLimit; page++)
        {
            Uri uri = _transport.Pan("list", ("dir", path), ("order", sort),
                ("desc", descending ? "1" : "0"), ("clienttype", "0"),
                ("num", BaiduTransport.Format(PageSize)), ("page", BaiduTransport.Format(page)));
            RemoteEntry[] entries;
            try
            {
                entries = ReadEntries(await _transport.JsonAsync(HttpMethod.Get, uri, "directory listing", true, cancellationToken), requireComplete);
            }
            catch (BaiduApiException error) when (page == 1 && error.Code == -6)
            {
                Uri pcs = _transport.Pcs("file", "list", ("path", path), ("by", sort), ("order", descending ? "desc" : "asc"));
                entries = ReadEntries(await _transport.JsonAsync(HttpMethod.Get, pcs, "PCS directory listing", false, cancellationToken), requireComplete);
                if (entries.Length >= PageSize)
                    throw new InvalidDataException("PCS returned its first 1,000 directory entries. Sign in with Pan session cookies to list the entire directory.");
            }

            int newEntries = 0;
            foreach (RemoteEntry entry in entries)
            {
                if (paths is not null && (entry.Id <= 0 || entry.Size < 0 || entry.Path == path
                    || RemotePath.Parent(entry.Path) != path || RemotePath.Resolve("/", entry.Path) != entry.Path
                    || entry.Name != RemotePath.Name(entry.Path) || !paths.Add(entry.Path) || emitted.Contains(entry.Id)))
                    throw new InvalidDataException("Baidu returned an incomplete or inconsistent directory listing.");
                if (!emitted.Add(entry.Id))
                    continue;

                newEntries++;
                yield return entry;
            }

            if (entries.Length < PageSize || newEntries == 0)
                yield break;
        }

        throw new InvalidDataException("Baidu directory listing exceeded the pagination limit.");
    }

    public async Task<IReadOnlyList<RemoteEntry>> GetMetadataAsync(IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        var result = new List<RemoteEntry>();
        foreach (string[] batch in paths.Chunk(100))
        {
            using MultipartFormDataContent form = BaiduTransport.JsonForm(new { list = batch.Select(path => new { path }) });
            JsonElement root = await _transport.JsonAsync(HttpMethod.Post, _transport.Pcs("file", "meta"), "metadata", false,
                cancellationToken, form);
            result.AddRange(ReadEntries(root));
        }

        return result;
    }

    public async IAsyncEnumerable<RemoteEntry> ListDescendantsAsync(string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!RemotePath.IsAbsolute(path) || RemotePath.Resolve("/", path) != path)
            throw new ArgumentException("A normalized absolute directory path is required.", nameof(path));

        var directories = new Queue<string>();
        directories.Enqueue(path);
        while (directories.TryDequeue(out string? parent))
        {
            await foreach (RemoteEntry entry in ListAsync(parent, cancellationToken: cancellationToken))
            {
                if (entry.Path == parent || RemotePath.Parent(entry.Path) != parent || RemotePath.Resolve("/", entry.Path) != entry.Path
                    || !entry.Name.Equals(RemotePath.Name(entry.Path), StringComparison.Ordinal))
                    throw new InvalidDataException("Baidu returned a file outside the requested directory.");

                yield return entry;
                if (entry.IsDirectory)
                    directories.Enqueue(entry.Path);
            }
        }
    }

    public async Task<IReadOnlyList<RemoteEntry>> GetSubdirectorySizesAsync(string path, CancellationToken cancellationToken = default)
    {
        var sizes = new Dictionary<string, RemoteEntry>(StringComparer.Ordinal);
        await foreach (RemoteEntry entry in ListDescendantsAsync(path, cancellationToken))
        {
            string relative = entry.Path[(path == "/" ? 1 : path.Length + 1)..];
            int separator = relative.IndexOf('/');
            if (separator < 0 && entry.IsDirectory)
                sizes.TryAdd(entry.Path, entry with { Size = 0 });
            else if (separator >= 0 && !entry.IsDirectory)
            {
                if (entry.Size < 0)
                    throw new InvalidDataException("Baidu returned a negative file size.");
                string root = RemotePath.Resolve(path, relative[..separator]);
                RemoteEntry directory = sizes[root];
                sizes[root] = directory with { Size = checked(directory.Size + entry.Size) };
            }
        }

        return sizes.Values.OrderByDescending(entry => entry.Size)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
    }

    public async Task<RemoteEntry?> FindAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<RemoteEntry> entries = await GetMetadataAsync([path], cancellationToken);
            return entries.FirstOrDefault(entry => entry.Path == path);
        }
        catch (BaiduApiException error) when (error.IsMissing)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<RemoteEntry>> SearchAsync(string directory, string keyword, bool recursive,
        CancellationToken cancellationToken = default)
    {
        Uri uri = _transport.Pcs("file", "search", ("path", directory), ("wd", keyword), ("re", recursive ? "1" : "0"));
        JsonElement root = await _transport.JsonAsync(HttpMethod.Get, uri, "search", false, cancellationToken);
        return ReadEntries(root);
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
        await _transport.JsonAsync(HttpMethod.Post, _transport.Pcs("file", "mkdir", ("path", path)),
            "directory creation", false, cancellationToken);

    public Task RemoveAsync(string path, CancellationToken cancellationToken = default)
    {
        EnsureBelowRoot(path);
        return ApplyFileOperationAsync("delete", new { list = new[] { new { path } } }, cancellationToken);
    }

    public Task CopyAsync(string from, string to, CancellationToken cancellationToken = default) =>
        RelocateAsync("copy", from, to, cancellationToken);

    public Task MoveAsync(string from, string to, CancellationToken cancellationToken = default) =>
        RelocateAsync("move", from, to, cancellationToken);

    private Task RelocateAsync(string operation, string from, string to, CancellationToken cancellationToken)
    {
        EnsureBelowRoot(from);
        EnsureBelowRoot(to);
        if (to == from || to.StartsWith(from + '/', StringComparison.Ordinal))
            throw new ArgumentException("The destination must be outside the source path.");

        return ApplyFileOperationAsync(operation, new { list = new[] { new { from, to } } }, cancellationToken);
    }

    private async Task ApplyFileOperationAsync(string operation, object payload, CancellationToken cancellationToken)
    {
        using MultipartFormDataContent form = BaiduTransport.JsonForm(payload);
        JsonElement root = await _transport.JsonAsync(HttpMethod.Post, _transport.Pcs("file", operation), operation, false,
            cancellationToken, form);
        foreach (JsonElement item in root.Items("info").Concat(root.Items("list")))
        {
            long code = item.Number("errno", item.Number("error_code"));
            if (code != 0)
                throw new BaiduApiException(operation, checked((int)code), System.Net.HttpStatusCode.OK);
        }
    }

    private static void EnsureBelowRoot(string path)
    {
        if (!RemotePath.IsAbsolute(path) || path == "/" || RemotePath.Resolve("/", path) != path)
            throw new ArgumentException("The operation requires a normalized path below the remote root.");
    }

    private static RemoteEntry[] ReadEntries(JsonElement root, bool requireComplete = false)
    {
        JsonElement field = root.Field("list");
        if (field.ValueKind == JsonValueKind.Null && !requireComplete)
            return [];
        if (field.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Baidu returned a response without a file list.");

        if (requireComplete && field.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.Object
            || item.Number("isdir", -1) is not (0 or 1) || item.Number("isdir") == 0 && item.Number("size", -1) < 0))
            throw new InvalidDataException("Baidu returned an incomplete directory listing.");
        return field.EnumerateArray().Where(item => item.ValueKind != JsonValueKind.Null).Select(ReadEntry).ToArray();
    }

    private static RemoteEntry ReadEntry(JsonElement item)
    {
        string path = item.Text("path");
        if (!RemotePath.IsAbsolute(path))
            throw new InvalidDataException("Baidu returned a file without an absolute path.");

        string name = item.Text("server_filename");
        long localModified = item.Number("local_mtime", -1);
        return new RemoteEntry(item.Number("fs_id"), item.Number("app_id"), path,
            name.Length > 0 ? name : RemotePath.Name(path), item.Number("isdir") == 1, item.Number("size"),
            DateTimeOffset.FromUnixTimeSeconds(item.Number("server_ctime", item.Number("ctime"))),
            DateTimeOffset.FromUnixTimeSeconds(item.Number("server_mtime", item.Number("mtime"))),
            DecodeMd5(item.Text("md5")), item.Items("block_list").Select(value => value.GetString() ?? "").ToArray())
        {
            LocalModified = localModified >= 0 ? DateTimeOffset.FromUnixTimeSeconds(localModified) : null,
        };
    }

    private static string DecodeMd5(string value)
    {
        if (value.Length != 32 || char.IsAsciiHexDigit(value[9]))
            return value;

        const string hex = "0123456789abcdef";
        int marker = value[9] - 'g';
        if (marker is < 0 or > 15)
            return value;

        Span<char> decoded = stackalloc char[32];
        for (int index = 0; index < decoded.Length; index++)
        {
            int nibble = index == 9 ? marker : value[index] == '-' ? 0 : hex.IndexOf(char.ToLowerInvariant(value[index]));
            if (nibble < 0)
                return value;

            decoded[index] = hex[nibble ^ (index & 15)];
        }

        return string.Concat(decoded[8..16], decoded[..8], decoded[24..], decoded[16..24]);
    }

    public void Dispose() => _transport.Dispose();
}
