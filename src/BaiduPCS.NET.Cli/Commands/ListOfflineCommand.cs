using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class ListOfflineCommand : RemoteCommand<PageSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PageSettings settings, CancellationToken cancellationToken)
    {
        var (_, client) = await OpenAsync(settings, cancellationToken);
        using (client)
            OfflineOutput.Write(await client.ListOfflineTasksAsync(settings.Page, cancellationToken), settings.RawNumbers);
        return 0;
    }
}
