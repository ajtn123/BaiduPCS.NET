using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class RestoreRecycleCommand : RemoteCommand<IdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IdSettings settings, CancellationToken cancellationToken)
    {
        var (_, client) = await OpenAsync(settings, cancellationToken);
        using (client)
            return await ExecuteEachAsync(settings, settings.Ids.Distinct(), id => $"File {id}", async id =>
            {
                if (!await client.RestoreRecycleEntryAsync(id, cancellationToken))
                    throw new InvalidDataException("Baidu did not confirm this restore.");
            }, cancellationToken);
    }
}
