using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class PrintWorkingDirectoryCommand : ProfileCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        Console.WriteLine((await Store.ReadAsync(cancellationToken)).RequireAccount().WorkingDirectory);
        return 0;
    }
}
