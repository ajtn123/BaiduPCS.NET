using BaiduPCS.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class SetAccessTokenCommand : ProfileCommand<SetAccessTokenCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandOption("--stdin")]
        [UiDescription("Read the token from standard input.")]
        public bool StandardInput { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Account active = (await Store.ReadAsync(cancellationToken)).RequireAccount();
        string? token;
        if (settings.StandardInput)
            token = await Console.In.ReadLineAsync(cancellationToken);
        else if (!Console.IsInputRedirected && !Console.IsOutputRedirected)
            token = AnsiConsole.Prompt(new TextPrompt<string>(Ui.Text("Access token:")).Secret());
        else
            throw new ArgumentException("Use --stdin to supply the access token without a terminal.");

        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("An access token is required.");

        await Store.UpdateAsync(profile =>
        {
            if (profile.ActiveAccountId != active.Id)
                throw new InvalidOperationException("The active account changed. Set the access token again.");

            profile.RequireAccount().Credentials.AccessToken = token.Trim();
        }, cancellationToken);
        return 0;
    }
}
