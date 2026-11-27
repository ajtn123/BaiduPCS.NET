using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class ExitCommand : Command<CommonSettings>
{
    public override int Execute(CommandContext context, CommonSettings settings, CancellationToken cancellationToken) => 0;
}
