using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class ResetConfigCommand : ProfileCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        await Store.UpdateAsync(profile => profile.Settings = new(), cancellationToken);
        return 0;
    }
}
