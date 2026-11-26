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
    private const long MinimumRangeSize = 8 * 1024 * 1024;
    private const int MaxRanges = 10_000;
    private TransferRate? _downloadRate;

    public async Task<bool> DownloadAsync(RemoteEntry entry, string destination, DownloadOptions options,
        Action<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (entry.IsDirectory || entry.Size < 0 || options is { Parallel: < 1 } or { Retries: < 0 } or { LinkIndex: < 0 })
            throw new ArgumentException("Download requires a file and valid worker, retry, and link settings.");

        destination = Path.GetFullPath(destination);
        string parent = Path.GetDirectoryName(destination) ?? throw new ArgumentException("A destination file is required.");
        if (!options.TestOnly && File.Exists(destination) && !options.Overwrite)
            return false;

        var rate = LazyInitializer.EnsureInitialized(ref _downloadRate, () => new TransferRate(_settings.DownloadRate));
        Uri uri = await ResolveDownloadUriAsync(entry.Path, options, cancellationToken);
        using HttpResponseMessage probe = await WithRetriesAsync(
            token => SendDownloadAsync(uri, options.Mode, 0, 0, token), options.Retries, cancellationToken);
        bool ranged = probe.StatusCode == HttpStatusCode.PartialContent;
        if (ranged)
            ValidateRange(probe, 0, 0, entry.Size);
        else if (!(entry.Size == 0 && probe.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable))
        {
            probe.EnsureSuccessStatusCode();
            if (entry.Size == 0)
                await CopyDownloadAsync(probe, null, 0, 0, rate, null, null, cancellationToken);
        }

        probe.Dispose();

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

    private async Task<Uri> ResolveDownloadUriAsync(string path, DownloadOptions options, CancellationToken token)
    {
        if (options.Mode != DownloadMode.Direct)
            return DownloadEndpoint(path, options.Mode == DownloadMode.Stream);

        IReadOnlyList<Uri> addresses = await LocateDownloadAsync(path, token);
        int preferred = Math.Min(options.LinkIndex, addresses.Count - 1);
        if (addresses[preferred].Host.StartsWith("nb.cache", StringComparison.OrdinalIgnoreCase) && preferred + 1 < addresses.Count)
            preferred++;

        return addresses[preferred];
    }

    private async Task DownloadRangesAsync(RemoteEntry entry, Uri uri, DownloadOptions options, FileStream? file,
        string? statePath, TransferRate rate, Action<DownloadProgress>? progress, CancellationToken token)
    {
        long rangeSize = Math.Max(MinimumRangeSize, (entry.Size - 1) / MaxRanges + 1);
        int rangeCount = checked((int)((entry.Size - 1) / rangeSize + 1));
        var identity = new DownloadIdentity(1, _account.Id, entry.Id, entry.Path, entry.Size, entry.Modified, entry.Md5, rangeSize);
        var completed = new bool[rangeCount];
        if (file is not null && file.Length == entry.Size && await ReadCheckpointAsync(statePath!, token) is { } previous
            && previous.Identity == identity && previous.Completed is { Length: var savedCount } && savedCount == rangeCount)
            completed = previous.Completed;

        if (file is not null)
        {
            if (!completed.Any(value => value))
                File.Delete(statePath!);
            file.SetLength(entry.Size);
        }

        long confirmed = 0;
        for (int index = 0; index < rangeCount; index++)
        {
            if (completed[index])
                confirmed += Math.Min(rangeSize, entry.Size - index * rangeSize);
        }

        using var stateLock = new SemaphoreSlim(1);
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = options.Parallel, CancellationToken = token };
        await Parallel.ForEachAsync(Enumerable.Range(0, rangeCount).Where(index => !completed[index]), parallel, async (index, innerToken) =>
        {
            long start = index * rangeSize;
            long size = Math.Min(rangeSize, entry.Size - start);
            long end = start + size - 1;
            await WithRetriesAsync(async workerToken =>
            {
                using HttpResponseMessage response = await SendDownloadAsync(uri, options.Mode, start, end, workerToken);
                ValidateRange(response, start, end, entry.Size);
                await CopyDownloadAsync(response, file, start, size, rate, index, null, workerToken);
                return true;
            }, options.Retries, innerToken);

            await stateLock.WaitAsync(CancellationToken.None);
            try
            {
                completed[index] = true;
                confirmed += size;
                if (file is not null)
                {
                    RandomAccess.FlushToDisk(file.SafeFileHandle);
                    await WriteCheckpointAsync(statePath!, new DownloadCheckpoint(identity, completed));
                }

                progress?.Invoke(new DownloadProgress(confirmed, entry.Size, index));
            }
            finally
            {
                stateLock.Release();
            }
        });
    }

    private Task<HttpResponseMessage> SendDownloadAsync(Uri uri, DownloadMode mode, long? from, long? to, CancellationToken token) =>
        _transport.SendAsync(HttpMethod.Get, uri, mode == DownloadMode.Direct, token, configure: request =>
        {
            request.Headers.AcceptEncoding.Clear();
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
            if (from.HasValue)
                request.Headers.Range = new RangeHeaderValue(from, to);
        });

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
            catch (Exception error) when (attempt < retries && !token.IsCancellationRequested && error is
                HttpRequestException or EndOfStreamException or InvalidDataException or OperationCanceledException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Min(attempt + 1, 8)), token);
            }
        }
    }

    private static async Task<DownloadCheckpoint?> ReadCheckpointAsync(string path, CancellationToken token)
    {
        try
        {
            await using FileStream checkpoint = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<DownloadCheckpoint>(checkpoint, cancellationToken: token);
        }
        catch (Exception error) when (error is FileNotFoundException or JsonException)
        {
            return null;
        }
    }

    private static async Task WriteCheckpointAsync(string path, DownloadCheckpoint checkpoint)
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
