using System.Buffers;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BaiduPCS.Configuration;
using BaiduPCS.Transfers;
using Microsoft.Win32.SafeHandles;

namespace BaiduPCS.Netdisk;

public sealed partial class NetdiskClient
{
    public const long MaximumUploadSize = 128L * 1024 * 1024 * 1024;
    private const long GiB = 1024L * 1024 * 1024;
    private const int MiB = 1024 * 1024;
    private TransferRate? _uploadRate;

    public async Task<UploadResult> UploadAsync(string source, string target, string checkpointDirectory, UploadOptions options,
        Action<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureBelowRoot(target);
        if (options is { Parallel: < 1 } or { Retries: < 0 })
            throw new ArgumentException("Upload requires a positive worker count and nonnegative retries.");

        source = Path.GetFullPath(source);
        await using var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous);
        long length = file.Length;
        if (length == 0 || length > MaximumUploadSize)
            throw new ArgumentException("Upload requires a nonempty file no larger than 128 GiB.");

        DateTime modified = File.GetLastWriteTimeUtc(source);
        RemoteEntry? existing = await FindAsync(target, cancellationToken);
        if (existing is { IsDirectory: true })
            throw new InvalidOperationException("The upload target is a directory.");
        if (existing is not null && (options.Conflict == UploadConflict.Skip
            || options.Conflict == UploadConflict.OverwriteIfSizeDiffers && existing.Size == length))
            return UploadResult.Skipped;

        FileChecksums sums = await FileChecksums.ComputeAsync(file, cancellationToken);
        if (!options.SkipRapidCheck && existing is not null && string.Equals(existing.Md5, sums.Md5, StringComparison.OrdinalIgnoreCase)
            && existing.Size == length)
            return UploadResult.Skipped;

        int blockSize = length >= 32 * GiB ? 64 * MiB : length >= 8 * GiB ? 16 * MiB : 4 * MiB;
        string[] blocks = await HashBlocksAsync(file.SafeFileHandle, length, blockSize, cancellationToken);
        var identity = new UploadIdentity(1, _account.Id, source, target, length, modified, sums.Md5, blockSize, options.Conflict);
        Directory.CreateDirectory(checkpointDirectory);
        string key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{_account.Id}:{target}")));
        string statePath = Path.Combine(checkpointDirectory, key + ".json");
        string lockPath = Path.Combine(checkpointDirectory, key + ".lock");
        await using var transferLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        UploadCheckpoint? checkpoint = await ReadCheckpointAsync<UploadCheckpoint>(statePath, cancellationToken);
        if (checkpoint?.Identity != identity || string.IsNullOrEmpty(checkpoint.UploadId)
            || checkpoint.Completed is null || checkpoint.Completed.Length != blocks.Length)
            checkpoint = null;

        var rate = LazyInitializer.EnsureInitialized(ref _uploadRate, () => new TransferRate(_settings.UploadRate));
        string rtype = options.Conflict == UploadConflict.Skip ? "2" : "3";
        for (int attempt = 0; ; attempt++)
        {
            if (checkpoint is null)
            {
                JsonElement precreate = await PrecreateUploadAsync(file.SafeFileHandle, identity, sums, blocks, rtype,
                    options.SkipRapidCheck, cancellationToken);
                if (precreate.Number("return_type") == 2)
                {
                    File.Delete(statePath);
                    return UploadResult.RapidUpload;
                }

                string uploadId = precreate.Text("uploadid");
                if (uploadId.Length == 0)
                    throw new InvalidDataException("Baidu precreate did not return an upload session.");

                checkpoint = new(identity, uploadId, new bool[blocks.Length]);
                await WriteCheckpointAsync(statePath, checkpoint);
            }

            try
            {
                await UploadPartsAsync(file.SafeFileHandle, checkpoint, statePath, blocks, options, rate, progress, cancellationToken);
                if (file.Length != length || File.GetLastWriteTimeUtc(source) != modified)
                    throw new IOException("The local source changed during upload.");

                await SendFormAsync(_transport.Pan("create"), "upload finalization", new()
                {
                    ["uploadid"] = checkpoint.UploadId,
                    ["path"] = target,
                    ["size"] = BaiduTransport.Format(length),
                    ["isdir"] = "0",
                    ["rtype"] = rtype,
                    ["block_list"] = JsonSerializer.Serialize(blocks),
                    ["target_path"] = RemotePath.Parent(target),
                }, cancellationToken);
                File.Delete(statePath);
                return UploadResult.Uploaded;
            }
            catch (BaiduApiException error) when (attempt == 0 && error.Code is 31061 or 31363)
            {
                File.Delete(statePath);
                checkpoint = null;
            }
        }
    }

    private async Task<JsonElement> PrecreateUploadAsync(SafeFileHandle file, UploadIdentity identity, FileChecksums sums,
        string[] blocks, string rtype, bool skipRapidCheck, CancellationToken token)
    {
        var form = new Dictionary<string, string>
        {
            ["path"] = identity.Target,
            ["size"] = BaiduTransport.Format(identity.Length),
            ["isdir"] = "0",
            ["block_list"] = JsonSerializer.Serialize(blocks),
            ["autoinit"] = "1",
            ["content-md5"] = sums.Md5,
            ["slice-md5"] = sums.SliceMd5,
            ["contentCrc32"] = sums.Crc32.ToString(CultureInfo.InvariantCulture),
            ["rtype"] = rtype,
        };
        if (!skipRapidCheck)
        {
            try
            {
                Uri infoUrl = BaiduTransport.Build("https", BaiduTransport.PanHost, "/api/user/getinfo", ("need_selfinfo", "1"));
                JsonElement info = await _transport.JsonAsync(HttpMethod.Get, infoUrl, "account UK", false, token);
                long uk = info.Items("records").FirstOrDefault().Number("uk");
                if (uk != 0)
                {
                    string timestamp = BaiduTransport.Format(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    byte[] digest = MD5.HashData(Encoding.UTF8.GetBytes(BaiduTransport.Format(uk) + sums.Md5 + timestamp));
                    uint seed = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(digest);
                    int sampleLength = (int)Math.Min(4096, identity.Length);
                    long offset = seed % (identity.Length - sampleLength + 1);
                    byte[] sample = new byte[sampleLength];
                    await ReadExactlyAtAsync(file, sample, offset, token);
                    var rapid = new Dictionary<string, string>(form)
                    {
                        ["target_path"] = RemotePath.Parent(identity.Target).TrimEnd('/') + "/",
                        ["data_offset"] = BaiduTransport.Format(offset),
                        ["data_length"] = BaiduTransport.Format(sampleLength),
                        ["data_time"] = timestamp,
                        ["data_content"] = Convert.ToBase64String(sample).TrimEnd('='),
                        ["local_mtime"] = timestamp,
                        ["local_ctime"] = timestamp,
                        ["checkexist"] = "0",
                        ["mode"] = "1",
                    };
                    return await SendFormAsync(_transport.Pan("precreate"), "rapid upload precreate", rapid, token);
                }
            }
            catch (BaiduApiException error) when (error.Code != 31112)
            {
                // The optional rapid check may be unavailable for an otherwise valid upload session.
            }
        }

        return await SendFormAsync(_transport.Pan("precreate"), "upload precreate", form, token);
    }

    private async Task UploadPartsAsync(SafeFileHandle file, UploadCheckpoint checkpoint, string statePath, string[] blocks,
        UploadOptions options, TransferRate rate, Action<UploadProgress>? progress, CancellationToken token)
    {
        UploadIdentity identity = checkpoint.Identity;
        string? host = await DiscoverUploadHostAsync(token);
        long confirmed = Enumerable.Range(0, blocks.Length).Where(index => checkpoint.Completed[index])
            .Sum(index => Math.Min(identity.BlockSize, identity.Length - (long)index * identity.BlockSize));
        using var stateLock = new SemaphoreSlim(1);
        await Parallel.ForEachAsync(Enumerable.Range(0, blocks.Length).Where(index => !checkpoint.Completed[index]),
            new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = options.Parallel }, async (index, partToken) =>
            {
                long offset = (long)index * identity.BlockSize;
                long count = Math.Min(identity.BlockSize, identity.Length - offset);
                await WithRetriesAsync(async workerToken =>
                {
                    (string, string)[] query = [("type", "tmpfile"), ("path", identity.Target), ("partseq", BaiduTransport.Format(index)),
                        ("partoffset", BaiduTransport.Format(offset)), ("uploadid", checkpoint.UploadId), ("vip", "1")];
                    Uri endpoint = _transport.Pcs("superfile2", "upload", query);
                    if (host is not null)
                        endpoint = new UriBuilder(endpoint) { Host = host }.Uri;

                    using var form = new MultipartFormDataContent();
                    form.Add(new UploadPartContent(file, offset, count, rate), "uploadedfile", "part");
                    JsonElement response = await _transport.JsonAsync(HttpMethod.Post, endpoint, "upload part", false, workerToken, form);
                    if (!blocks[index].Equals(response.Text("md5"), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Baidu returned an unexpected checksum for the uploaded part.");

                    return true;
                }, options.Retries, partToken);

                await stateLock.WaitAsync(CancellationToken.None);
                try
                {
                    checkpoint.Completed[index] = true;
                    confirmed += count;
                    await WriteCheckpointAsync(statePath, checkpoint);
                    progress?.Invoke(new(confirmed, identity.Length, index));
                }
                finally
                {
                    stateLock.Release();
                }
            });
    }

    private async Task<string?> DiscoverUploadHostAsync(CancellationToken token)
    {
        if (_settings.FixedPcsHost)
            return null;

        try
        {
            Uri endpoint = _transport.Pcs("file", "locateupload", ("upload_version", "2.0"), ("app_id", BaiduTransport.PanAppId));
            JsonElement root = await _transport.JsonAsync(HttpMethod.Get, endpoint, "upload server discovery", false, token);
            IEnumerable<string> candidates = root.Items("servers").Select(value => value.Text("server"))
                .Concat(root.Items("server").Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!));
            string[] hosts = candidates.Select(value => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ? uri.Host : value)
                .Where(host => host.EndsWith(".baidu.com", StringComparison.OrdinalIgnoreCase) && Uri.CheckHostName(host) == UriHostNameType.Dns)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return hosts.Length == 0 ? null : hosts[Random.Shared.Next(hosts.Length)];
        }
        catch (Exception error) when (error is BaiduApiException or HttpRequestException or InvalidDataException)
        {
            return null;
        }
    }

    private async Task<JsonElement> SendFormAsync(Uri uri, string operation, Dictionary<string, string> form, CancellationToken token)
    {
        using var content = new FormUrlEncodedContent(form);
        return await _transport.JsonAsync(HttpMethod.Post, uri, operation, true, token, content);
    }

    private static async Task<string[]> HashBlocksAsync(SafeFileHandle file, long length, int blockSize, CancellationToken token)
    {
        var blocks = new List<string>();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(256 * 1024);
        try
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            for (long offset = 0; offset < length;)
            {
                long blockEnd = Math.Min(length, offset + blockSize);
                while (offset < blockEnd)
                {
                    int count = (int)Math.Min(buffer.Length, blockEnd - offset);
                    await ReadExactlyAtAsync(file, buffer.AsMemory(0, count), offset, token);
                    hash.AppendData(buffer, 0, count);
                    offset += count;
                }

                blocks.Add(Convert.ToHexStringLower(hash.GetHashAndReset()));
            }

            return [.. blocks];
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task ReadExactlyAtAsync(SafeFileHandle file, Memory<byte> buffer, long offset, CancellationToken token)
    {
        while (buffer.Length > 0)
        {
            int count = await RandomAccess.ReadAsync(file, buffer, offset, token);
            if (count == 0)
                throw new EndOfStreamException("The local source ended before the expected offset.");

            buffer = buffer[count..];
            offset += count;
        }
    }

    private sealed record UploadIdentity(int Version, ulong AccountId, string Source, string Target, long Length, DateTime Modified,
        string Md5, int BlockSize, UploadConflict Conflict);
    private sealed record UploadCheckpoint(UploadIdentity Identity, string UploadId, bool[] Completed);

    private sealed class UploadPartContent(SafeFileHandle file, long offset, long length, TransferRate rate) : HttpContent
    {
        protected override bool TryComputeLength(out long size)
        {
            size = length;
            return true;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
            try
            {
                for (long sent = 0; sent < length;)
                {
                    int count = (int)Math.Min(buffer.Length, length - sent);
                    await ReadExactlyAtAsync(file, buffer.AsMemory(0, count), offset + sent, token);
                    await rate.WaitAsync(count, token);
                    await stream.WriteAsync(buffer.AsMemory(0, count), token);
                    sent += count;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
