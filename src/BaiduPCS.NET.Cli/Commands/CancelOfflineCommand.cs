using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class CancelOfflineCommand : RemoteCommand<IdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IdSettings settings, CancellationToken cancellationToken)
    {
        var (_, client) = await OpenAsync(settings, cancellationToken);
        using (client)
            return await ExecuteEachAsync(settings, settings.Ids.Distinct(), id => $"Task {id}",
                id => client.CancelOfflineTaskAsync(id, cancellationToken), cancellationToken);
    }
}
