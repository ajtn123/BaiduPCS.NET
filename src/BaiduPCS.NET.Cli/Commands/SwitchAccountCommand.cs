using System.Globalization;
using BaiduPCS.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;
using Profile = BaiduPCS.Configuration.Profile;

namespace BaiduPCS.Cli.Commands;

internal sealed class SwitchAccountCommand : ProfileCommand<SwitchAccountCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "[uid-or-name]")]
        [UiDescription("Account UID or name. Omit to choose interactively.")]
        public string? Selector { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Profile snapshot = await Store.ReadAsync(cancellationToken);
        Account selected = Select(snapshot, settings.Selector);
        await Store.UpdateAsync(profile =>
        {
            if (!profile.Accounts.Any(account => account.Id == selected.Id))
                throw new InvalidOperationException("The selected account was removed. Select an account again.");

            profile.ActiveAccountId = selected.Id;
        }, cancellationToken);
        Console.WriteLine($"Active account: {selected.Name}");
        return 0;
    }

    private static Account Select(Profile profile, string? selector)
    {
        if (profile.Accounts.Count == 0)
            throw new InvalidOperationException("There are no saved accounts.");

        if (selector is null)
        {
            if (Console.IsInputRedirected || Console.IsOutputRedirected)
                throw new ArgumentException("Specify an account UID or name when running without a terminal.");

            var prompt = new SelectionPrompt<Account>().Title(Ui.Text("Select an account."))
                .UseConverter(account => Markup.Escape($"{account.Name} ({account.Id})"))
                .AddChoices(profile.Accounts);
            return AnsiConsole.Prompt(prompt);
        }

        if (ulong.TryParse(selector, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id)
            && profile.Accounts.Find(account => account.Id == id) is { } byId)
            return byId;

        Account[] matches = [.. profile.Accounts.Where(account => string.Equals(account.Name, selector, StringComparison.OrdinalIgnoreCase))];
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException("No saved account matches the selector."),
            _ => throw new ArgumentException("Several accounts share that name. Specify a UID."),
        };
    }
}
