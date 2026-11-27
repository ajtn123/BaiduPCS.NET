using BaiduPCS.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class LogoutCommand : ProfileCommand<LogoutCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandOption("--yes")]
        [UiDescription("Remove the saved account without prompting.")]
        public bool Yes { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Account active = (await Store.ReadAsync(cancellationToken)).RequireAccount();
        if (!settings.Yes)
        {
            if (Console.IsInputRedirected || Console.IsOutputRedirected)
                throw new ArgumentException("Use --yes to confirm removal when running without a terminal.");
            if (!AnsiConsole.Confirm(Markup.Escape($"Remove the saved account for {active.Name}?"), defaultValue: false))
                return 0;
        }

        await Store.UpdateAsync(profile =>
        {
            if (profile.ActiveAccountId != active.Id)
                throw new InvalidOperationException("The active account changed. Confirm removal again.");

            profile.RemoveActiveAccount();
        }, cancellationToken);
        return 0;
    }
}
