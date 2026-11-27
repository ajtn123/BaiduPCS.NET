using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class CancelShareCommand : RemoteCommand<IdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IdSettings settings, CancellationToken cancellationToken)
    {
        var (_, client) = await OpenAsync(settings, cancellationToken);
        using (client)
            return await ExecuteEachAsync(settings, settings.Ids.Distinct(), id => $"Canceled share {id}",
                id => client.CancelShareAsync(id, cancellationToken), cancellationToken);
    }
}
