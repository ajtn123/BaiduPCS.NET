using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class ClearCommand : Command<CommonSettings>
{
    public override int Execute(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        if (!Console.IsOutputRedirected)
            Console.Clear();
        return 0;
    }
}
