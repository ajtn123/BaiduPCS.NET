using System.ComponentModel;
using System.Globalization;
using BaiduPCS.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;
using Profile = BaiduPCS.Configuration.Profile;

namespace BaiduPCS.Cli;

internal class CommonSettings : CommandSettings
{
    [CommandOption("--verbose")]
    [Description("Enable diagnostic output.")]
    public bool Verbose { get; init; }
}

internal abstract class ProfileCommand<TSettings> : AsyncCommand<TSettings> where TSettings : CommonSettings
{
    protected ProfileStore Store { get; } = new(ProfileStore.DefaultDirectory());
}

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

internal sealed class ListAccountsCommand : ProfileCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        Profile profile = await Store.ReadAsync(cancellationToken);
        var table = new Table().AddColumns("Active", "UID", "Name", "Sex", "Age");
        foreach (Account account in profile.Accounts)
        {
            table.AddRow(account.Id == profile.ActiveAccountId ? "*" : "", account.Id.ToString(CultureInfo.InvariantCulture),
                Markup.Escape(account.Name), Markup.Escape(account.Sex), account.Age.ToString(CultureInfo.InvariantCulture));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

internal sealed class CurrentAccountCommand : ProfileCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        Account account = (await Store.ReadAsync(cancellationToken)).RequireAccount();
        Console.WriteLine($"{account.Id}\t{account.Name}\t{account.Sex}\t{account.Age.ToString(CultureInfo.InvariantCulture)}");
        return 0;
    }
}

internal sealed class PrintWorkingDirectoryCommand : ProfileCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        Console.WriteLine((await Store.ReadAsync(cancellationToken)).RequireAccount().WorkingDirectory);
        return 0;
    }
}

internal sealed class SwitchAccountCommand : ProfileCommand<SwitchAccountCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "[uid-or-name]")]
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

        Account[] matches = profile.Accounts
            .Where(account => string.Equals(account.Name, selector, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException("No saved account matches the selector."),
            _ => throw new ArgumentException("Several accounts share that name. Specify a UID."),
        };
    }
}

internal sealed class LogoutCommand : ProfileCommand<LogoutCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandOption("--yes")]
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

internal sealed class SetAccessTokenCommand : ProfileCommand<SetAccessTokenCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandOption("--stdin")]
        [Description("Read the token from standard input.")]
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

internal sealed class EnvironmentCommand : ProfileCommand<CommonSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Configuration directory: {Store.DirectoryPath}");
        Console.WriteLine($"{ProfileStore.DirectoryVariable}={Environment.GetEnvironmentVariable(ProfileStore.DirectoryVariable)}");
        Console.WriteLine($"{ProfileStore.VerboseVariable}={Environment.GetEnvironmentVariable(ProfileStore.VerboseVariable)}");
        Console.WriteLine($"{LegacyProfile.DirectoryVariable}={Environment.GetEnvironmentVariable(LegacyProfile.DirectoryVariable)}");
        return Task.FromResult(0);
    }
}
