// Adapts the BaiduPCS-Go checksum export to .NET. See THIRD-PARTY-NOTICES.md.
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using BaiduPCS.Transfers;

namespace BaiduPCS.Netdisk;

public sealed partial class NetdiskClient
{
    public Task<string> GetFileMd5Async(RemoteEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry.IsDirectory || entry.Size < 0)
            throw new ArgumentException("A file is required for checksum comparison.");
        if (entry.Size == 0)
            return Task.FromResult(Convert.ToHexStringLower(MD5.HashData([])));
        if (IsMd5(entry.Md5) && (entry.Blocks.Count == 0
            || entry.Blocks.Count == 1 && entry.Blocks[0].Equals(entry.Md5, StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(entry.Md5);

        return WithRetriesAsync(async token =>
        {
            Uri direct = await ResolveDownloadUriAsync(entry.Path, new DownloadOptions(), token);
            string? md5 = await ReadRemoteMd5Async(direct, entry.Size, token);
            if (md5 is null && entry.Size < 4L * 1024 * 1024 * 1024)
                md5 = await ReadRemoteMd5Async(DownloadEndpoint(entry.Path, streaming: false), entry.Size, token);
            return md5 ?? throw new InvalidDataException($"Baidu did not return a full file checksum for {entry.Path}.");
        }, 3, cancellationToken);
    }

    public Task<FileChecksums> GetExportChecksumsAsync(RemoteEntry entry, int retries = 3, CancellationToken cancellationToken = default)
    {
        if (entry.IsDirectory || entry.Size < 0 || entry.Size > MaximumUploadSize)
            throw new ArgumentException("Export checksums require a file no larger than 128 GiB.");
        ArgumentOutOfRangeException.ThrowIfNegative(retries);

        if (entry.Size == 0)
        {
            string empty = Convert.ToHexStringLower(MD5.HashData([]));
            return Task.FromResult(new FileChecksums(0, empty, empty, 0));
        }

        if (entry.Size <= FileChecksums.SliceLength && entry.Blocks.Count == 1 && IsMd5(entry.Md5)
            && string.Equals(entry.Blocks[0], entry.Md5, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new FileChecksums(entry.Size, entry.Md5, entry.Md5, 0));

        return WithRetriesAsync(token => ExportChecksumsAsync(entry, token), retries, cancellationToken);
    }

    private async Task<FileChecksums> ExportChecksumsAsync(RemoteEntry entry, CancellationToken token)
    {
        Uri direct = await ResolveDownloadUriAsync(entry.Path, new DownloadOptions(), token);
        FileChecksums? checksums = await ReadRemoteChecksumsAsync(direct, entry.Size, token);
        if (checksums is null && entry.Size < 4L * 1024 * 1024 * 1024)
            checksums = await ReadRemoteChecksumsAsync(DownloadEndpoint(entry.Path, streaming: false), entry.Size, token);

        return checksums ?? throw new InvalidDataException("Baidu did not return the full file MD5 needed for export.");
    }

    private async Task<FileChecksums?> ReadRemoteChecksumsAsync(Uri uri, long size, CancellationToken token)
    {
        bool sliced = size > FileChecksums.SliceLength;
        using HttpResponseMessage response = await OpenRemoteChecksumResponseAsync(uri, size, token);
        string? md5 = ReadMd5(response);
        if (md5 is null)
            return null;

        if (!uint.TryParse(ReadHeader(response, "x-bs-meta-crc32"), NumberStyles.None, CultureInfo.InvariantCulture, out uint crc32))
            throw new InvalidDataException("Baidu did not return the file CRC32 needed for export.");

        if (!sliced)
            return new FileChecksums(size, md5, md5, crc32);

        byte[] slice = new byte[FileChecksums.SliceLength];
        await using Stream content = await response.Content.ReadAsStreamAsync(token);
        await content.ReadExactlyAsync(slice, token);
        return new FileChecksums(size, md5, Convert.ToHexStringLower(MD5.HashData(slice)), crc32);
    }

    private async Task<string?> ReadRemoteMd5Async(Uri uri, long size, CancellationToken token)
    {
        using HttpResponseMessage response = await OpenRemoteChecksumResponseAsync(uri, size, token);
        return ReadMd5(response);
    }

    private async Task<HttpResponseMessage> OpenRemoteChecksumResponseAsync(Uri uri, long size, CancellationToken token)
    {
        bool sliced = size > FileChecksums.SliceLength;
        HttpResponseMessage response = await SendDownloadAsync(uri, DownloadMode.Direct, sliced ? 0 : null,
            sliced ? FileChecksums.SliceLength - 1 : null, token);
        try
        {
            response.EnsureSuccessStatusCode();
            if (sliced)
                ValidateRange(response, 0, FileChecksums.SliceLength - 1, size);
            else if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength != size)
                throw new InvalidDataException("Baidu returned the wrong size for the checksum request.");
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static string? ReadMd5(HttpResponseMessage response)
    {
        string header = ReadHeader(response, "Content-MD5");
        return IsMd5(header) ? header.ToLowerInvariant()
            : response.Content.Headers.ContentMD5 is { Length: 16 } bytes ? Convert.ToHexStringLower(bytes) : null;
    }

    private static string ReadHeader(HttpResponseMessage response, string name)
    {
        if (response.Content.Headers.NonValidated.TryGetValues(name, out var values)
            || response.Headers.NonValidated.TryGetValues(name, out values))
            return values.FirstOrDefault()?.Trim() ?? "";

        return "";
    }

    private static bool IsMd5(string value) => value.Length == 32 && value.All(char.IsAsciiHexDigit);
}
