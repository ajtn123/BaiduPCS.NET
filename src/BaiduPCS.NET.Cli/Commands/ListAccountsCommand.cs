using System.Globalization;
using BaiduPCS.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;
using Profile = BaiduPCS.Configuration.Profile;

namespace BaiduPCS.Cli.Commands;

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
