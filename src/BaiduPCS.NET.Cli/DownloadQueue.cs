using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BaiduPCS.Netdisk;
using BaiduPCS.Transfers;

namespace BaiduPCS.Cli;

internal sealed record QueuedDownload(RemoteEntry Entry, string Root, string Target, DownloadOptions Options);

internal sealed class DownloadQueue(string profileDirectory, ulong accountId)
{
    private readonly string _directory = Path.Combine(profileDirectory, "downloads", accountId.ToString(CultureInfo.InvariantCulture));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal async Task RecordAsync(QueuedDownload download, CancellationToken token)
    {
        Directory.CreateDirectory(_directory);
        string path = PathFor(download.Target);
        QueuedDownload? existing = await ReadAsync(path, token);
        ValidateExisting(existing);
        if (existing is not null && existing.Entry.Id == download.Entry.Id && existing.Entry.Size == download.Entry.Size
            && existing.Entry.Modified == download.Entry.Modified && existing.Options == download.Options)
            return;

        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(file, download, Json, token);
                await file.FlushAsync(token);
            }

            token.ThrowIfCancellationRequested();
            try { File.Move(temporary, path, overwrite: existing is not null); }
            catch (IOException) when (existing is null && File.Exists(path))
            {
                ValidateExisting(await ReadAsync(path, token));
            }
        }
        finally
        {
            File.Delete(temporary);
        }

        void ValidateExisting(QueuedDownload? saved)
        {
            if (saved is not null && (!saved.Target.Equals(download.Target, PathComparison) || saved.Entry.Path != download.Entry.Path))
                throw new IOException("A different download is queued for the same destination.");
        }
    }

    internal void Complete(QueuedDownload download) => File.Delete(PathFor(download.Target));

    internal async IAsyncEnumerable<QueuedDownload> ReadAllAsync([EnumeratorCancellation] CancellationToken token)
    {
        if (!Directory.Exists(_directory))
            yield break;
        foreach (string path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            token.ThrowIfCancellationRequested();
            if (await ReadAsync(path, token) is { } download)
                yield return download;
        }
    }

    private async Task<QueuedDownload?> ReadAsync(string path, CancellationToken token)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A queued download record is a symbolic link.");
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                4096, FileOptions.Asynchronous);
            QueuedDownload? job = await JsonSerializer.DeserializeAsync<QueuedDownload>(file, Json, token);
            if (job is not { Entry.IsDirectory: false, Options.TestOnly: false } || !RemotePath.IsAbsolute(job.Entry.Path)
                || string.IsNullOrEmpty(job.Root) || string.IsNullOrEmpty(job.Target)
                || !Path.IsPathFullyQualified(job.Root) || !Path.IsPathFullyQualified(job.Target)
                || !PathFor(job.Target).Equals(path, PathComparison)
                || !Path.GetFullPath(job.Target).Equals(job.Target, PathComparison))
                throw new InvalidDataException("A queued download has invalid data.");

            string relative = Path.GetRelativePath(job.Root, job.Target);
            if (relative is "." or ".." || Path.IsPathRooted(relative)
                || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("A queued download target is outside its destination.");
            return job;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("A queued download has invalid data.", error);
        }
    }

    private string PathFor(string destination)
    {
        string key = PathComparison == StringComparison.OrdinalIgnoreCase ? destination.ToUpperInvariant() : destination;
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_directory, hash + ".json");
    }
}
