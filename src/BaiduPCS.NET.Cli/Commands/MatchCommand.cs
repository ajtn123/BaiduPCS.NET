using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class MatchCommand : RemoteCommand<MatchCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<pattern>")]
        [UiDescription("Netdisk wildcard pattern to expand.")]
        public string Pattern { get; init; } = "";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            foreach (string match in await client.MatchAsync(account.WorkingDirectory, settings.Pattern, cancellationToken))
                Console.WriteLine(match);
        }

        return 0;
    }
}
