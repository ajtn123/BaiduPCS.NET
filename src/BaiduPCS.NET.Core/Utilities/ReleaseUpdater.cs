using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using BaiduPCS.Netdisk;

namespace BaiduPCS.Utilities;

public sealed record StableRelease(string Tag, Version Version, IReadOnlyDictionary<string, string> AssetDigests);

public sealed class ReleaseUpdater(HttpClient http)
{
    public const string Repository = "ajtn123/BaiduPCS.NET";
    private const long MaximumExecutableSize = 512L * 1024 * 1024;
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

        var digests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonElement asset in root.Items("assets"))
        {
            if (asset.Text("state") == "uploaded" && !digests.TryAdd(asset.Text("name"), asset.Text("digest")))
                throw new InvalidDataException("The GitHub release lists an asset more than once.");
        }
        return new StableRelease(tag, version!, digests);
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
        CancellationToken cancellationToken = default, bool frameworkDependent = false)
    {
        string target = Path.GetFullPath(executable);
        string fileName = OperatingSystem.IsWindows() ? "baidupcs.exe" : "baidupcs";
        string extension = runtime.StartsWith("win-", StringComparison.Ordinal) ? ".exe" : "";
        string flavor = frameworkDependent ? "-framework-dependent" : "";
        string assetName = $"BaiduPCS.NET-{release.Tag}-{runtime}{flavor}{extension}";
        if (!SupportedRuntime(runtime) || !release.AssetDigests.TryGetValue(assetName, out string? digest))
            throw new InvalidOperationException($"The release has no {(frameworkDependent ? "framework-dependent" : "self-contained")} executable for {runtime}.");

        byte[] expected = ReadAssetDigest(digest);
        string stage = Path.Combine(Path.GetDirectoryName(target)!, ".baidupcs-update-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(stage);
        else
            Directory.CreateDirectory(stage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string binary = Path.Combine(stage, fileName);
        bool ready = false;
        try
        {
            await DownloadExecutableAsync(AssetUri(release, assetName), binary, expected, cancellationToken);

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(binary, File.GetUnixFileMode(target));
            ready = true;
            return binary;
        }
        finally
        {
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

    private static byte[] ReadAssetDigest(string digest)
    {
        const string prefix = "sha256:";
        if (digest.Length != prefix.Length + 64 || !digest.StartsWith(prefix, StringComparison.Ordinal)
            || !digest[prefix.Length..].All(char.IsAsciiHexDigit))
            throw new InvalidDataException("The GitHub release asset does not have a valid SHA-256 digest.");

        return Convert.FromHexString(digest.AsSpan(prefix.Length));
    }

    private async Task DownloadExecutableAsync(Uri uri, string path, byte[] expected, CancellationToken token)
    {
        using HttpResponseMessage response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using Stream content = await response.Content.ReadAsStreamAsync(token);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 128 * 1024, FileOptions.Asynchronous);
        await CopyBoundedAsync(content, file, MaximumExecutableSize, token);
        if (file.Length == 0)
            throw new InvalidDataException("The downloaded executable is empty.");
        file.Position = 0;
        byte[] actual = await SHA256.HashDataAsync(file, token);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            throw new InvalidDataException("The downloaded executable does not match the published SHA-256 checksum.");
        file.Flush(flushToDisk: true);
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
