using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BaiduPCS.Transfers;

namespace BaiduPCS.Netdisk;

public sealed partial class NetdiskClient
{
    private const int ProbeSize = 32 * 1024;
    private const int MaxRanges = 10_000;
    private TransferRate? _downloadRate;

    public async Task<bool> DownloadAsync(RemoteEntry entry, string destination, DownloadOptions options,
        Action<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (entry.IsDirectory || entry.Size < 0 || options is { Parallel: < 1 } or { Retries: < 0 } or { LinkIndex: < 0 })
            throw new ArgumentException("Download requires a file and valid worker, retry, and link settings.");
        EnsureBelowRoot(entry.Path);

        destination = Path.GetFullPath(destination);
        if (!options.TestOnly && File.Exists(destination) && !options.Overwrite)
            return false;

        if (options.Mode != DownloadMode.Direct)
            return await DownloadFromAsync(entry, destination, options, DownloadEndpoint(entry.Path, options.Mode == DownloadMode.Stream),
                progress, cancellationToken);

        IReadOnlyList<Uri> addresses = await LocateDownloadAsync(entry.Path, cancellationToken);
        int preferred = PreferredLink(addresses, options.LinkIndex);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await DownloadFromAsync(entry, destination, options, addresses[preferred], progress, cancellationToken);
            }
            catch (Exception error) when (attempt < options.Retries && !cancellationToken.IsCancellationRequested
                && (error is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    or HttpStatusCode.NotFound or HttpStatusCode.Gone } or BaiduApiException { Status: HttpStatusCode.Forbidden }
                    || error is InvalidDataException && addresses.Count > 1))
            {
                if (preferred + 1 < addresses.Count)
                    preferred++;
                else
                {
                    addresses = await LocateDownloadAsync(entry.Path, cancellationToken);
                    preferred = PreferredLink(addresses, options.LinkIndex);
                }
            }
        }
    }

    private async Task<bool> DownloadFromAsync(RemoteEntry entry, string destination, DownloadOptions options, Uri uri,
        Action<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        string parent = Path.GetDirectoryName(destination) ?? throw new ArgumentException("A destination file is required.");
        var rate = LazyInitializer.EnsureInitialized(ref _downloadRate, () => new TransferRate(_settings.DownloadRate));
        bool ranged = await ProbeAsync(entry.Size, uri, options, rate, cancellationToken);

        if (options.TestOnly)
        {
            if (entry.Size != 0)
            {
                if (ranged)
                    await DownloadRangesAsync(entry, uri, options, null, null, rate, progress, cancellationToken);
                else
                    await WithRetriesAsync(async token =>
                    {
                        using HttpResponseMessage response = await SendDownloadAsync(uri, options.Mode, null, null, token);
                        await CopyDownloadAsync(response, null, 0, entry.Size, rate, null, progress, token);
                        return true;
                    }, options.Retries, cancellationToken);
            }

            return true;
        }

        Directory.CreateDirectory(parent);
        string stateDirectory = Path.Combine(parent, ".baidupcs-partial");
        RejectLink(stateDirectory);
        Directory.CreateDirectory(stateDirectory);
        string key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFileName(destination))));
        string part = Path.Combine(stateDirectory, key + ".part");
        string state = Path.Combine(stateDirectory, key + ".json");
        string lockPath = Path.Combine(stateDirectory, key + ".lock");
        RejectLink(part);
        RejectLink(state);
        RejectLink(lockPath);
        await using var transferLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(destination) && !options.Overwrite)
            return false;

        await using (var file = new FileStream(part, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read,
            1, FileOptions.Asynchronous | FileOptions.RandomAccess))
        {
            if (ranged)
                await DownloadRangesAsync(entry, uri, options, file, state, rate, progress, cancellationToken);
            else if (entry.Size == 0)
                file.SetLength(0);
            else
            {
                File.Delete(state);
                await WithRetriesAsync(async token =>
                {
                    file.SetLength(0);
                    using HttpResponseMessage response = await SendDownloadAsync(uri, options.Mode, null, null, token);
                    await CopyDownloadAsync(response, file, 0, entry.Size, rate, null, progress, token);
                    return true;
                }, options.Retries, cancellationToken);
            }

            if (options.Verify && entry.Md5.Length == 32 && entry.Blocks.Count <= 1)
            {
                file.Position = 0;
                string actual = Convert.ToHexStringLower(await MD5.HashDataAsync(file, cancellationToken));
                if (!actual.Equals(entry.Md5, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(state);
                    if (IsPlaceholder(entry.Size, actual))
                        throw new InvalidOperationException("Baidu returned a restricted-file placeholder instead of the file.");
                    throw new InvalidDataException("Downloaded data does not match the Baidu checksum.");
                }
            }

            file.Flush(flushToDisk: true);
        }

        if (options.PreserveModifiedTime)
            File.SetLastWriteTimeUtc(part, entry.Modified.UtcDateTime);
        if (options.Executable && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(part, File.GetUnixFileMode(part) | UnixFileMode.UserExecute);
        cancellationToken.ThrowIfCancellationRequested();
        RejectLink(destination);
        File.Move(part, destination, options.Overwrite);
        File.Delete(state);
        return true;
    }

    private async Task<bool> ProbeAsync(long size, Uri uri, DownloadOptions options, TransferRate rate, CancellationToken token) =>
        await WithRetriesAsync(async attemptToken =>
        {
            using HttpResponseMessage probe = await SendDownloadAsync(uri, options.Mode, 0, ProbeSize - 1, attemptToken);
            if (probe.StatusCode == HttpStatusCode.PartialContent)
            {
                ValidateRange(probe, 0, Math.Min(size, ProbeSize) - 1, size);
                return true;
            }

            if (size != 0 || probe.StatusCode != HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                probe.EnsureSuccessStatusCode();
                if (size == 0)
                    await CopyDownloadAsync(probe, null, 0, 0, rate, null, null, attemptToken);
                else if (probe.Content.Headers.ContentLength is long length && length != size)
                    throw new InvalidDataException("Baidu advertised an unexpected file size.");
            }

            return false;
        }, options.Retries, token);

    private async Task<Uri> ResolveDownloadUriAsync(string path, DownloadOptions options, CancellationToken token)
    {
        if (options.Mode != DownloadMode.Direct)
            return DownloadEndpoint(path, options.Mode == DownloadMode.Stream);

        IReadOnlyList<Uri> addresses = await LocateDownloadAsync(path, token);
        return addresses[PreferredLink(addresses, options.LinkIndex)];
    }

    private static int PreferredLink(IReadOnlyList<Uri> addresses, int linkIndex)
    {
        int preferred = Math.Min(linkIndex, addresses.Count - 1);
        if (addresses[preferred].Host.StartsWith("nb.cache", StringComparison.OrdinalIgnoreCase) && preferred + 1 < addresses.Count)
            preferred++;

        return preferred;
    }

    private static bool IsPlaceholder(long size, string hash) => (size, hash) is
        (1749504, "48bb9b0361dc9c672f3dc7b3ffcfde97") or (120, "6c1b84914588d09a6e5ec43605557457");

    private async Task DownloadRangesAsync(RemoteEntry entry, Uri uri, DownloadOptions options, FileStream? file,
        string? statePath, TransferRate rate, Action<DownloadProgress>? progress, CancellationToken token)
    {
        const long mib = 1024 * 1024;
        long blockSize = entry.Size switch
        {
            < 5 * mib => 256 * 1024,
            < 10 * mib => 512 * 1024,
            < 15 * mib => mib,
            _ => 4 * mib,
        };
        long rangeSize = Math.Max(blockSize, (entry.Size - 1) / MaxRanges + 1);
        int rangeCount = checked((int)((entry.Size - 1) / rangeSize + 1));
        var identity = new DownloadIdentity(1, _account.Id, entry.Id, entry.Path, entry.Size, entry.Modified, entry.Md5, rangeSize);
        var completed = new bool[rangeCount];
        if (file is not null && file.Length <= entry.Size && await ReadCheckpointAsync<DownloadCheckpoint>(statePath!, token) is { } previous
            && previous.Identity == identity && previous.Completed is { Length: var savedCount } && savedCount == rangeCount
            && Array.FindLastIndex(previous.Completed, value => value) < (file.Length == entry.Size ? rangeCount : file.Length / rangeSize))
            completed = previous.Completed;

        if (file is not null)
        {
            if (!completed.Any(value => value))
            {
                File.Delete(statePath!);
                if (!options.Preallocate)
                    file.SetLength(0);
            }
            if (options.Preallocate)
                file.SetLength(entry.Size);
        }

        long transferred = 0;
        for (int index = 0; index < rangeCount; index++)
        {
            if (completed[index])
                transferred += Math.Min(rangeSize, entry.Size - index * rangeSize);
        }
        if (transferred != 0)
            progress?.Invoke(new DownloadProgress(transferred, entry.Size, null) { Resumed = true });

        using var stateLock = new SemaphoreSlim(1);
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = file is null || options.Preallocate ? options.Parallel : 1,
            CancellationToken = token,
        };
        await Parallel.ForEachAsync(Enumerable.Range(0, rangeCount).Where(index => !completed[index]), parallel, async (index, innerToken) =>
        {
            long start = index * rangeSize;
            long size = Math.Min(rangeSize, entry.Size - start);
            long end = start + size - 1;
            long partLoaded = 0;
            await WithRetriesAsync(async workerToken =>
            {
                Interlocked.Add(ref transferred, -partLoaded);
                partLoaded = 0;
                using HttpResponseMessage response = await SendDownloadAsync(uri, options.Mode, start, end, workerToken);
                ValidateRange(response, start, end, entry.Size);
                await CopyDownloadAsync(response, file, start, size, rate, index, progress is null ? null : current =>
                {
                    long whole = Interlocked.Add(ref transferred, current.CompletedBytes - partLoaded);
                    partLoaded = current.CompletedBytes;
                    progress(new DownloadProgress(whole, entry.Size, index));
                }, workerToken);
                return true;
            }, options.Retries, innerToken);

            await stateLock.WaitAsync(CancellationToken.None);
            try
            {
                completed[index] = true;
                if (file is not null)
                {
                    RandomAccess.FlushToDisk(file.SafeFileHandle);
                    await WriteCheckpointAsync(statePath!, new DownloadCheckpoint(identity, completed));
                }

            }
            finally
            {
                stateLock.Release();
            }
        });
    }

    private async Task<HttpResponseMessage> SendDownloadAsync(Uri uri, DownloadMode mode, long? from, long? to, CancellationToken token)
    {
        HttpResponseMessage response = await _transport.SendAsync(HttpMethod.Get, uri, mode == DownloadMode.Direct, token, configure: request =>
        {
            string agent = mode == DownloadMode.Direct ? _settings.PanUserAgent : _settings.PcsUserAgent;
            if (agent.Length == 0)
                request.Headers.Remove("User-Agent");
            if (from.HasValue)
                request.Headers.Range = new RangeHeaderValue(from, to);
        }, rawDownload: from.HasValue);
        if (response.StatusCode != HttpStatusCode.Forbidden)
            return response;

        using (response)
        {
            int code = await ReadDownloadErrorCodeAsync(response.Content, token);
            if (code != 0)
                throw new BaiduApiException("download", code, response.StatusCode);
            throw new HttpRequestException("Download request was forbidden.", null, response.StatusCode);
        }
    }

    private static async Task<int> ReadDownloadErrorCodeAsync(HttpContent content, CancellationToken token)
    {
        const int maximum = 4096;
        if (content.Headers.ContentLength > maximum)
            return 0;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            // Baidu also sends JSON error bodies labeled text/plain.
            var buffer = new byte[maximum + 1];
            await using Stream body = await content.ReadAsStreamAsync(timeout.Token);
            int count = await body.ReadAtLeastAsync(buffer, buffer.Length, false, timeout.Token);
            if (count > maximum)
                return 0;

            using JsonDocument json = JsonDocument.Parse(buffer.AsMemory(0, count));
            long code = json.RootElement.Number("error_code");
            if (code == 0)
                code = json.RootElement.Number("errno");
            return code is >= int.MinValue and <= int.MaxValue ? (int)code : 0;
        }
        catch (Exception error) when (error is IOException or HttpRequestException or JsonException
            || error is OperationCanceledException && !token.IsCancellationRequested)
        {
            return 0;
        }
    }

    private async Task CopyDownloadAsync(HttpResponseMessage response, FileStream? destination, long start, long expected,
        TransferRate rate, int? range, Action<DownloadProgress>? progress, CancellationToken token)
    {
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length != expected)
            throw new InvalidDataException("Baidu returned an unexpected download size.");

        await using Stream body = await response.Content.ReadAsStreamAsync(token);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Clamp(_settings.CacheSize, 1024, 1024 * 1024));
        long read = 0;
        try
        {
            int count;
            while ((count = await body.ReadAsync(buffer, token)) != 0)
            {
                if (count > expected - read)
                    throw new InvalidDataException("Baidu returned more bytes than requested.");

                await rate.WaitAsync(count, token);
                if (destination is not null)
                    await RandomAccess.WriteAsync(destination.SafeFileHandle, buffer.AsMemory(0, count), start + read, token);
                read += count;
                progress?.Invoke(new DownloadProgress(read, expected, range));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (read != expected)
            throw new EndOfStreamException("Baidu ended the download before sending all bytes.");
    }

    private static void ValidateRange(HttpResponseMessage response, long start, long end, long size)
    {
        response.EnsureSuccessStatusCode();
        ContentRangeHeaderValue? range = response.Content.Headers.ContentRange;
        if (response.StatusCode != HttpStatusCode.PartialContent || range is null || range.Unit != "bytes"
            || range.From != start || range.To != end || range.Length != size)
            throw new InvalidDataException("Baidu returned an unexpected download range.");
    }

    private static async Task<T> WithRetriesAsync<T>(Func<CancellationToken, Task<T>> work, int retries, CancellationToken token)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await work(token);
            }
            catch (Exception error) when (attempt < retries && !token.IsCancellationRequested && (error is
                HttpRequestException { StatusCode: null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                    or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout }
                or EndOfStreamException or InvalidDataException or OperationCanceledException
                || error is BaiduApiException { Code: 0, Status: HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                    or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout }))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Min(attempt + 1, 8)), token);
            }
        }
    }

    private static async Task<T?> ReadCheckpointAsync<T>(string path, CancellationToken token) where T : class
    {
        try
        {
            await using FileStream checkpoint = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(checkpoint, cancellationToken: token);
        }
        catch (Exception error) when (error is FileNotFoundException or JsonException)
        {
            return null;
        }
    }

    private static async Task WriteCheckpointAsync<T>(string path, T checkpoint)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream stream = File.Open(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, checkpoint);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static void RejectLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A download path points to a symbolic link.");
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            // It is safe to create an absent path.
        }
    }

    private sealed record DownloadIdentity(int Version, ulong AccountId, long Id, string Path, long Size,
        DateTimeOffset Modified, string Md5, long RangeSize);

    private sealed record DownloadCheckpoint(DownloadIdentity Identity, bool[] Completed);
}
