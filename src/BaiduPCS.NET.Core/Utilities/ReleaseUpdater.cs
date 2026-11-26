using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using BaiduPCS.Netdisk;

namespace BaiduPCS.Utilities;

public sealed record StableRelease(string Tag, Version Version, IReadOnlySet<string> Assets);

public sealed class ReleaseUpdater(HttpClient http)
{
    public const string Repository = "ajtn123/BaiduPCS.NET";
    public const string ChecksumFile = "SHA256SUMS";
    private const long MaximumArchiveSize = 512L * 1024 * 1024;
    private static readonly Uri LatestUrl = new($"https://api.github.com/repos/{Repository}/releases/latest");

    public async Task<StableRelease?> LatestAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        request.Headers.UserAgent.ParseAdd("BaiduPCS.NET");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        await using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument release = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        JsonElement root = release.RootElement;
        string tag = root.Text("tag_name");
        if (root.Field("draft").ValueKind == JsonValueKind.True || root.Field("prerelease").ValueKind == JsonValueKind.True)
            return null;
        if (!tag.StartsWith('v') || !TryParseVersion(tag[1..], out Version? version))
            throw new InvalidDataException("The GitHub release does not have a valid stable version tag.");

        var assets = root.Items("assets").Where(asset => asset.Text("state") == "uploaded" || asset.Text("state").Length == 0)
            .Select(asset => asset.Text("name")).ToHashSet(StringComparer.Ordinal);
        return new StableRelease(tag, version!, assets);
    }

    public static bool IsNewer(StableRelease release, string current)
    {
        string[] parts = current.Split('-', 2);
        if (!TryParseVersion(parts[0], out Version? version))
            throw new InvalidDataException("The running tool has an invalid version.");

        int order = release.Version.CompareTo(version);
        return order > 0 || order == 0 && parts.Length > 1;
    }

    public async Task<string> StageStandaloneAsync(StableRelease release, string runtime, string executable,
        CancellationToken cancellationToken = default)
    {
        string target = Path.GetFullPath(executable);
        string fileName = OperatingSystem.IsWindows() ? "baidupcs.exe" : "baidupcs";
        string archiveName = $"BaiduPCS.NET-{release.Tag}-{runtime}.zip";
        if (!SupportedRuntime(runtime) || !release.Assets.Contains(archiveName) || !release.Assets.Contains(ChecksumFile))
            throw new InvalidOperationException($"The release has no verified standalone archive for {runtime}.");

        string manifest = await ReadManifestAsync(AssetUri(release, ChecksumFile), cancellationToken);
        byte[] expected = ReadArchiveHash(manifest, archiveName);
        string stage = Path.Combine(Path.GetDirectoryName(target)!, ".baidupcs-update-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(stage);
        else
            Directory.CreateDirectory(stage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string zipPath = Path.Combine(stage, "release.zip");
        string binary = Path.Combine(stage, fileName);
        bool ready = false;
        try
        {
            await DownloadArchiveAsync(AssetUri(release, archiveName), zipPath, expected, cancellationToken);
            await using (FileStream archive = File.OpenRead(zipPath))
            using (var zip = new ZipArchive(archive, ZipArchiveMode.Read))
            {
                ZipArchiveEntry[] candidates = zip.Entries.Where(entry => entry.FullName == fileName).ToArray();
                if (candidates.Length != 1 || candidates[0].Length is <= 0 or > MaximumArchiveSize)
                    throw new InvalidDataException("The release archive does not contain the expected executable.");

                await using Stream source = candidates[0].Open();
                await using var staged = new FileStream(binary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    128 * 1024, FileOptions.Asynchronous);
                await CopyBoundedAsync(source, staged, MaximumArchiveSize, cancellationToken);
                staged.Flush(flushToDisk: true);
            }

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(binary, File.GetUnixFileMode(target));
            ready = true;
            return binary;
        }
        finally
        {
            File.Delete(zipPath);
            if (!ready)
            {
                File.Delete(binary);
                Directory.Delete(stage);
            }
        }
    }

    public static string ReplaceStandalone(string executable, string stagedExecutable)
    {
        string target = Path.GetFullPath(executable);
        string staged = Path.GetFullPath(stagedExecutable);
        string backup = target + ".previous";
        NetdiskClient.RejectLink(target);
        NetdiskClient.RejectLink(backup);
        NetdiskClient.RejectLink(staged);
        File.Replace(staged, target, backup, ignoreMetadataErrors: false);
        return backup;
    }

    internal static byte[] ReadArchiveHash(string manifest, string archiveName)
    {
        string? hash = null;
        foreach (string line in manifest.Split('\n'))
        {
            string trimmed = line.Trim();
            int separator = trimmed.IndexOfAny([' ', '\t']);
            if (separator < 0 || !trimmed[(separator + 1)..].TrimStart().TrimStart('*').Equals(archiveName, StringComparison.Ordinal))
                continue;
            if (hash is not null || separator != 64 || !trimmed[..separator].All(char.IsAsciiHexDigit))
                throw new InvalidDataException("The release checksum manifest contains an invalid or duplicate archive entry.");

            hash = trimmed[..separator];
        }

        return hash is null ? throw new InvalidDataException("The release checksum manifest does not list this archive.")
            : Convert.FromHexString(hash);
    }

    private async Task<string> ReadManifestAsync(Uri uri, CancellationToken token)
    {
        using HttpResponseMessage response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using Stream source = await response.Content.ReadAsStreamAsync(token);
        using var content = new MemoryStream();
        await CopyBoundedAsync(source, content, 128 * 1024, token);
        return System.Text.Encoding.UTF8.GetString(content.ToArray());
    }

    private async Task DownloadArchiveAsync(Uri uri, string path, byte[] expected, CancellationToken token)
    {
        using HttpResponseMessage response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using Stream content = await response.Content.ReadAsStreamAsync(token);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 128 * 1024, FileOptions.Asynchronous);
        await CopyBoundedAsync(content, file, MaximumArchiveSize, token);
        file.Position = 0;
        byte[] actual = await SHA256.HashDataAsync(file, token);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            throw new InvalidDataException("The downloaded archive does not match the published SHA-256 checksum.");
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, long maximum, CancellationToken token)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token)) != 0)
        {
            if ((total += read) > maximum)
                throw new InvalidDataException("The release asset is larger than expected.");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
    }

    private static Uri AssetUri(StableRelease release, string name) => new($"https://github.com/{Repository}/releases/download/"
        + Uri.EscapeDataString(release.Tag) + "/" + Uri.EscapeDataString(name));

    private static bool TryParseVersion(string value, out Version? version) => Version.TryParse(value, out version)
        && version.Build >= 0 && version.Revision < 0 && version.ToString(3) == value;

    private static bool SupportedRuntime(string runtime) => runtime is "win-x64" or "win-arm64" or "osx-x64" or "osx-arm64"
        or "linux-x64" or "linux-arm64" or "linux-musl-x64" or "linux-musl-arm64";
}
