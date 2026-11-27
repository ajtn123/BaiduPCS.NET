using BaiduPCS.Configuration;
using Spectre.Console.Cli;
using Profile = BaiduPCS.Configuration.Profile;

namespace BaiduPCS.Cli.Commands;

internal sealed class ImportAccountCommand : ProfileCommand<ImportAccountCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "[config-file]")]
        public string? ConfigFile { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Profile legacy = await LegacyProfile.ReadAsync(settings.ConfigFile ?? LegacyProfile.FindFile(), cancellationToken);
        await Store.UpdateAsync(profile => LegacyProfile.Merge(profile, legacy), cancellationToken);
        Console.WriteLine($"Imported {legacy.Accounts.Count} BaiduPCS-Go account(s).");
        return 0;
    }
}
