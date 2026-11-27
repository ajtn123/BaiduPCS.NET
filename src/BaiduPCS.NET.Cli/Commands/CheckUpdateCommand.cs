using BaiduPCS.Utilities;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class CheckUpdateCommand : AsyncCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        StableRelease? release = await new ReleaseUpdater(http).LatestAsync(cancellationToken);
        if (release is null)
            Console.WriteLine("No stable BaiduPCS.NET release is available.");
        else if (ReleaseUpdater.IsNewer(release, Program.Version))
            Console.WriteLine($"BaiduPCS.NET {release.Tag} is available. Run 'baidupcs update apply' to update.");
        else
            Console.WriteLine($"BaiduPCS.NET {Program.Version} is up to date.");
        return 0;
    }
}
