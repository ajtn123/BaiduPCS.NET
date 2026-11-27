using BaiduPCS.Netdisk;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class QueryOfflineCommand : RemoteCommand<IdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IdSettings settings, CancellationToken cancellationToken)
    {
        var (_, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            IReadOnlyList<OfflineTask> tasks = await client.QueryOfflineTasksAsync(settings.Ids, cancellationToken);
            OfflineOutput.Write(tasks, settings.RawNumbers);
            var numbers = new DisplayNumbers(settings.RawNumbers);
            foreach (OfflineTask task in tasks.Where(task => task.Result == 0))
            {
                foreach (OfflineFile file in task.Files)
                    Console.WriteLine($"{task.Id}\t{numbers.Bytes(file.Size, unit: false)}\t{file.Name}");
            }

            return tasks.All(task => task.Result == 0) ? 0 : 1;
        }
    }
}
