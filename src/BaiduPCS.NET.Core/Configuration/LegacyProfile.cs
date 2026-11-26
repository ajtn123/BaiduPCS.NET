using System.Text.Json;

namespace BaiduPCS.Configuration;

public static class LegacyProfile
{
    private const string ConfigName = "pcs_config.json";
    public const string DirectoryVariable = "BAIDUPCS_GO_CONFIG_DIR";

    public static string FindFile()
    {
        string? configured = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string directory = Path.IsPathFullyQualified(configured)
                ? configured
                : Path.Combine(FindExecutableDirectory() ?? throw new InvalidOperationException(
                    "A relative BaiduPCS-Go config directory requires baidupcs-go on PATH or an explicit config file."), configured);
            return Path.Combine(directory, ConfigName);
        }

        if (FindExecutableDirectory() is { } executableDirectory)
        {
            string portablePath = Path.Combine(executableDirectory, ConfigName);
            if (File.Exists(portablePath))
                return portablePath;
        }

        string parent = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(parent, "BaiduPCS-Go", ConfigName);
    }

    public static async Task<Profile> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                4096, FileOptions.Asynchronous);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            JsonElement root = document.RootElement;
            var profile = new Profile { Settings = ReadSettings(root) };
            if (root.TryGetProperty("baidu_user_list", out JsonElement accounts) && accounts.ValueKind != JsonValueKind.Null)
            {
                foreach (JsonElement value in accounts.EnumerateArray())
                    profile.Activate(ReadAccount(value));
            }

            ulong activeId = Read(root, "baidu_active_uid", 0UL);
            if (activeId != 0 && profile.Accounts.All(account => account.Id != activeId))
                throw new InvalidDataException("The BaiduPCS-Go config refers to a missing active account.");

            profile.ActiveAccountId = activeId == 0 ? null : activeId;
            return profile;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException("The BaiduPCS-Go config contains invalid settings or account data.");
        }
    }

    public static void Merge(Profile destination, Profile legacy)
    {
        ulong? previousActiveId = destination.ActiveAccountId;
        foreach (Account account in legacy.Accounts)
            destination.Activate(account);

        destination.ActiveAccountId = legacy.ActiveAccountId ?? previousActiveId;
        destination.Settings = legacy.Settings;
    }

    private static Account ReadAccount(JsonElement value)
    {
        ulong id = Read(value, "uid", 0UL);
        string name = Read(value, "name", "");
        string bduss = Read(value, "bduss", "");
        string cookies = Read(value, "cookies", "");
        if (id == 0 || string.IsNullOrWhiteSpace(name) || (bduss.Length == 0 && cookies.Length == 0))
            throw new InvalidDataException("A BaiduPCS-Go account is missing its identity or session credentials.");

        return new Account
        {
            Id = id,
            Name = name,
            Sex = Read(value, "sex", ""),
            Age = Read(value, "age", 0d),
            WorkingDirectory = RemotePath.Resolve("/", Read(value, "workdir", "/")),
            Credentials = new Credentials
            {
                Bduss = bduss,
                Cookies = cookies,
                Ptoken = Read(value, "ptoken", ""),
                Stoken = Read(value, "stoken", ""),
                Baiduid = Read(value, "baiduid", ""),
                Sboxtkn = Read(value, "sboxtkn", ""),
                AccessToken = Read(value, "accesstoken", ""),
            },
        };
    }

    private static ClientSettings ReadSettings(JsonElement root)
    {
        var defaults = new ClientSettings();
        return defaults with
        {
            AppId = Math.Max(1, Read(root, "appid", defaults.AppId)),
            CacheSize = Math.Max(1024, Read(root, "cache_size", defaults.CacheSize)),
            DownloadParallel = Math.Max(1, Read(root, "max_parallel", defaults.DownloadParallel)),
            UploadParallel = Math.Max(1, Read(root, "max_upload_parallel", defaults.UploadParallel)),
            DownloadConcurrency = Math.Max(1, Read(root, "max_download_load", defaults.DownloadConcurrency)),
            UploadConcurrency = Math.Max(1, Read(root, "max_upload_load", defaults.UploadConcurrency)),
            DownloadRate = Math.Max(0, Read(root, "max_download_rate", defaults.DownloadRate)),
            UploadRate = Math.Max(0, Read(root, "max_upload_rate", defaults.UploadRate)),
            SaveDirectory = Read(root, "savedir", defaults.SaveDirectory),
            DownloadPrefix = Read(root, "save_prefix", defaults.DownloadPrefix),
            UseHttps = Read(root, "enable_https", defaults.UseHttps),
            CheckUploadNames = !Read(root, "ignore_illegal", !defaults.CheckUploadNames),
            ManualAccountName = Read(root, "force_login_username", defaults.ManualAccountName),
            VerifyDownloads = !Read(root, "no_check", !defaults.VerifyDownloads),
            UploadConflict = Read(root, "u_policy", "skip") switch
            {
                "skip" or "" => UploadConflict.Skip,
                "overwrite" => UploadConflict.Overwrite,
                "rsync" => UploadConflict.OverwriteIfSizeDiffers,
                _ => throw new InvalidDataException("The BaiduPCS-Go upload conflict policy is invalid."),
            },
            UserAgent = Read(root, "user_agent", defaults.UserAgent),
            PcsUserAgent = Read(root, "pcs_ua", defaults.PcsUserAgent),
            PanUserAgent = Read(root, "pan_ua", defaults.PanUserAgent),
            PcsHost = Read(root, "pcs_addr", defaults.PcsHost),
            FixedPcsHost = Read(root, "fix_pcs_addr", defaults.FixedPcsHost),
            Proxy = Read(root, "proxy", defaults.Proxy),
            ProxyHosts = Read(root, "proxy_hostnames", defaults.ProxyHosts),
            LocalAddresses = Read(root, "local_addrs", defaults.LocalAddresses),
            NetworkFamily = Read(root, "prefer_ipv6", false) ? NetworkFamily.PreferIpv6 : NetworkFamily.Auto,
        };
    }

    private static T Read<T>(JsonElement value, string key, T fallback) =>
        value.TryGetProperty(key, out JsonElement child) && child.ValueKind != JsonValueKind.Null
            ? child.Deserialize<T>()!
            : fallback;

    private static string? FindExecutableDirectory()
    {
        string executable = OperatingSystem.IsWindows() ? "BaiduPCS-Go.exe" : "BaiduPCS-Go";
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (directory.Length > 0 && File.Exists(Path.Combine(directory, executable)))
                return Path.GetFullPath(directory);
        }

        return null;
    }
}
