using System.Globalization;
using BaiduPCS.Configuration;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class CurrentAccountCommand : ProfileCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        Account account = (await Store.ReadAsync(cancellationToken)).RequireAccount();
        Console.WriteLine($"{account.Id}\t{account.Name}\t{account.Sex}\t{account.Age.ToString(CultureInfo.InvariantCulture)}");
        return 0;
    }
}
