using BaiduPCS.Netdisk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal abstract class DeleteByIdCommand : RemoteCommand<DeleteIdSettings>
{
    protected abstract string ClearPrompt { get; }
    protected abstract Task<long> ClearAsync(NetdiskClient client, CancellationToken token);
    protected abstract Task DeleteAsync(NetdiskClient client, long id, CancellationToken token);

    public override async Task<int> ExecuteAsync(CommandContext context, DeleteIdSettings settings, CancellationToken cancellationToken)
    {
        if (settings.All && !settings.Yes)
        {
            if (Console.IsInputRedirected || Console.IsOutputRedirected)
                throw new ArgumentException("Use --yes to confirm clearing when running without a terminal.");
            if (!AnsiConsole.Confirm(Ui.Text(ClearPrompt), defaultValue: false))
                return 0;
        }

        var (_, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            if (!settings.All)
                return await ExecuteEachAsync(settings, settings.Ids.Distinct(), id => $"Deleted {id}",
                    id => DeleteAsync(client, id, cancellationToken), cancellationToken);

            Console.WriteLine($"Deleted {await ClearAsync(client, cancellationToken)} entries.");
            return 0;
        }
    }
}
