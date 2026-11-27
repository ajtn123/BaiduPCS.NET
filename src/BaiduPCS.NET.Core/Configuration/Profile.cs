using System.Text.Json.Serialization;

namespace BaiduPCS.Configuration;

public sealed class Profile
{
    public int Version { get; init; } = 1;
    public ClientSettings Settings { get; set; } = new();
    public ulong? ActiveAccountId { get; set; }
    public List<Account> Accounts { get; init; } = [];

    [JsonIgnore]
    public Account? ActiveAccount => Accounts.Find(account => account.Id == ActiveAccountId);

    public Account RequireAccount() => ActiveAccount
        ?? throw new InvalidOperationException("Sign in or import an account first.");

    public void Activate(Account account)
    {
        int index = Accounts.FindIndex(saved => saved.Id == account.Id);
        if (index < 0)
            Accounts.Add(account);
        else
            Accounts[index] = account;

        ActiveAccountId = account.Id;
    }

    public void RemoveActiveAccount()
    {
        Accounts.Remove(RequireAccount());
        ActiveAccountId = Accounts.FirstOrDefault()?.Id;
    }
}

public sealed class Account
{
    public required ulong Id { get; init; }
    public required string Name { get; init; }
    public string Sex { get; init; } = "";
    public double Age { get; init; }
    public string WorkingDirectory { get; set; } = "/";
    public Credentials Credentials { get; init; } = new();
}

public sealed class Credentials
{
    public string Bduss { get; init; } = "";
    public string Ptoken { get; init; } = "";
    public string Stoken { get; init; } = "";
    public string Baiduid { get; init; } = "";
    public string Sboxtkn { get; init; } = "";
    public string Cookies { get; init; } = "";
    public string AccessToken { get; set; } = "";
}

public enum UploadConflict
{
    Skip,
    Overwrite,
    OverwriteIfSizeDiffers,
}

public enum NetworkFamily { Auto, PreferIpv6, Ipv4 }

public sealed record ClientSettings
{
    public const string DefaultUserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_13_2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/63.0.3239.132 Safari/537.36";
    public const string DefaultPanUserAgent = "netdisk;P2SP;3.0.0.8;netdisk;11.12.3;ANG-AN00;android-android;10.0;JSbridge4.4.0;jointBridge;1.1.0;";

    public int AppId { get; init; } = 266719;
    public int CacheSize { get; init; } = 65536;
    public int DownloadParallel { get; init; } = 1;
    public int UploadParallel { get; init; } = 4;
    public int DownloadConcurrency { get; init; } = 1;
    public int UploadConcurrency { get; init; } = 4;
    public long DownloadRate { get; init; }
    public long UploadRate { get; init; }
    public string SaveDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public string? DownloadPrefix { get; init; }
    public bool UseHttps { get; init; } = true;
    public bool CheckUploadNames { get; init; }
    public string ManualAccountName { get; init; } = "";
    public bool VerifyDownloads { get; init; }
    public bool SkipRapidCheck { get; init; }
    public UploadConflict UploadConflict { get; init; }
    public string UserAgent { get; init; } = DefaultUserAgent;
    public string PcsUserAgent { get; init; } = "";
    public string PanUserAgent { get; init; } = DefaultPanUserAgent;
    public string PcsHost { get; init; } = "pcs.baidu.com";
    public bool FixedPcsHost { get; init; }
    public string Proxy { get; init; } = "";
    public string ProxyHosts { get; init; } = "";
    public string LocalAddresses { get; init; } = "";
    public NetworkFamily NetworkFamily { get; init; }

    public override string ToString() => nameof(ClientSettings);
}
