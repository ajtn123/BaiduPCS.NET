using System.Globalization;
using System.Text.RegularExpressions;
using BaiduPCS.Configuration;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed partial class SetConfigCommand : ProfileCommand<SetConfigCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<key=value>")]
        public string[] Assignments { get; init; } = [];
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        await Store.UpdateAsync(profile =>
        {
            ClientSettings updated = profile.Settings;
            foreach (string assignment in settings.Assignments)
            {
                int separator = assignment.IndexOf('=');
                if (separator <= 0)
                    throw new ArgumentException("Settings must use key=value syntax. Use 'config show' for valid keys.");

                updated = Apply(updated, assignment[..separator], assignment[(separator + 1)..]);
            }

            profile.Settings = updated;
        }, cancellationToken);
        return 0;
    }

    private static ClientSettings Apply(ClientSettings settings, string key, string value) => key switch
    {
        "app-id" => settings with { AppId = PositiveInt(value) },
        "cache-size" => settings with { CacheSize = checked((int)Math.Max(1024, ParseBytes(value, isRate: false))) },
        "download-parallel" => settings with { DownloadParallel = PositiveInt(value) },
        "upload-parallel" => settings with { UploadParallel = PositiveInt(value) },
        "download-concurrency" => settings with { DownloadConcurrency = PositiveInt(value) },
        "upload-concurrency" => settings with { UploadConcurrency = PositiveInt(value) },
        "download-rate" => settings with { DownloadRate = ParseBytes(value, isRate: true) },
        "upload-rate" => settings with { UploadRate = ParseBytes(value, isRate: true) },
        "save-directory" => settings with { SaveDirectory = Path.GetFullPath(value) },
        "download-prefix" => settings with { DownloadPrefix = value == "@account" ? null : value },
        "use-https" => settings with { UseHttps = bool.Parse(value) },
        "check-upload-names" => settings with { CheckUploadNames = bool.Parse(value) },
        "manual-account-name" => settings with { ManualAccountName = value },
        "verify-downloads" => settings with { VerifyDownloads = bool.Parse(value) },
        "skip-rapid-check" => settings with { SkipRapidCheck = bool.Parse(value) },
        "upload-conflict" => settings with { UploadConflict = ParseConflict(value) },
        "user-agent" => settings with { UserAgent = value },
        "pcs-user-agent" => settings with { PcsUserAgent = value },
        "pan-user-agent" => settings with { PanUserAgent = value },
        "pcs-host" => settings with { PcsHost = value },
        "fixed-pcs-host" => settings with { FixedPcsHost = bool.Parse(value) },
        "proxy" => settings with { Proxy = value },
        "proxy-hosts" => settings with { ProxyHosts = value },
        "local-addresses" => settings with { LocalAddresses = value },
        "network-family" => settings with { NetworkFamily = value switch
        {
            "auto" => NetworkFamily.Auto,
            "prefer-ipv6" => NetworkFamily.PreferIpv6,
            "ipv4" => NetworkFamily.Ipv4,
            _ => throw new ArgumentException("Use auto, prefer-ipv6, or ipv4 as the network family."),
        } },
        _ => throw new ArgumentException("Unknown setting. Use 'config show' for valid keys."),
    };

    internal static UploadConflict ParseConflict(string value) => value switch
    {
        "skip" => UploadConflict.Skip,
        "overwrite" => UploadConflict.Overwrite,
        "overwrite-if-size-differs" => UploadConflict.OverwriteIfSizeDiffers,
        _ => throw new ArgumentException("Upload conflict must be skip, overwrite, or overwrite-if-size-differs."),
    };

    private static int PositiveInt(string value)
    {
        int number = int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
        return number > 0 ? number : throw new ArgumentException("Enter an integer greater than zero.");
    }

    private static long ParseBytes(string value, bool isRate)
    {
        string text = value.Trim().ToLowerInvariant();
        if (isRate && text.EndsWith("/s", StringComparison.Ordinal))
            text = text[..^2].TrimEnd();

        Match match = ByteSize().Match(text);
        if (!match.Success)
            throw new ArgumentException("Enter a byte count or a size such as 64KB or 2MiB.");

        int exponent = match.Groups["unit"].Value switch
        {
            "k" => 1,
            "m" => 2,
            "g" => 3,
            "t" => 4,
            _ => 0,
        };
        decimal count = decimal.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture);
        return checked((long)(count * (1L << (10 * exponent))));
    }

    [GeneratedRegex(@"\A(?<count>[0-9]+(?:\.[0-9]+)?)\s*(?:(?<unit>[kmgt])(?:i?b)?|b)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex ByteSize();
}
