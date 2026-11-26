using System.Globalization;
using BaiduPCS.Netdisk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli;

internal sealed class PageSettings : CommonSettings
{
    [CommandOption("--page <PAGE>")]
    public int Page { get; init; } = 1;

    public override ValidationResult Validate() => Page >= 1
        ? ValidationResult.Success() : ValidationResult.Error("Page numbers start at 1.");
}

internal class IdSettings : CommonSettings
{
    [CommandArgument(0, "<id>")]
    public long[] Ids { get; init; } = [];

    public override ValidationResult Validate() => Ids.Length > 0 && Ids.All(id => id > 0)
        ? ValidationResult.Success() : ValidationResult.Error("Specify one or more positive IDs.");
}

internal sealed class DeleteIdSettings : CommonSettings
{
    [CommandArgument(0, "[id]")]
    public long[] Ids { get; init; } = [];
    [CommandOption("--all")] public bool All { get; init; }
    [CommandOption("--yes")] public bool Yes { get; init; }

    public override ValidationResult Validate() => All ? Ids.Length == 0
        ? ValidationResult.Success() : ValidationResult.Error("Choose IDs or --all.")
        : Ids.Length > 0 && Ids.All(id => id > 0)
            ? ValidationResult.Success() : ValidationResult.Error("Specify positive IDs or --all.");
}

internal sealed class AddOfflineCommand : RemoteCommand<AddOfflineCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<url>")]
        public string[] Sources { get; init; } = [];
        [CommandOption("--path <DIRECTORY>")]
        public string Path { get; init; } = ".";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            string destination = await client.MatchOneAsync(account.WorkingDirectory, settings.Path, cancellationToken);
            var tasks = new List<(int Index, long Id)>();
            int result = await ExecuteEachAsync(settings.Sources.Select((source, index) => (Source: source, Index: index + 1)),
                item => $"Source {item.Index}", async item => tasks.Add((item.Index,
                    await client.AddOfflineTaskAsync(item.Source, destination, cancellationToken))), cancellationToken);
            foreach (var task in tasks)
                Console.WriteLine($"Source {task.Index} → task {task.Id}");

            return result;
        }
    }
}

internal sealed class ListOfflineCommand : RemoteCommand<PageSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PageSettings settings, CancellationToken cancellationToken)
    {
        var (_, client) = await OpenAsync(settings, cancellationToken);
        using (client)
            OfflineOutput.Write(await client.ListOfflineTasksAsync(settings.Page, cancellationToken));
        return 0;
    }
}

internal sealed class QueryOfflineCommand : RemoteCommand<IdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IdSettings settings, CancellationToken cancellationToken)
    {
        var (_, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            IReadOnlyList<OfflineTask> tasks = await client.QueryOfflineTasksAsync(settings.Ids, cancellationToken);
            OfflineOutput.Write(tasks);
            foreach (OfflineTask task in tasks.Where(task => task.Result == 0))
            {
                foreach (OfflineFile file in task.Files)
                    Console.WriteLine($"{task.Id}\t{file.Size}\t{file.Name}");
            }

            return tasks.All(task => task.Result == 0) ? 0 : 1;
        }
    }
}

internal sealed class CancelOfflineCommand : RemoteCommand<IdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IdSettings settings, CancellationToken cancellationToken)
    {
        var (_, client) = await OpenAsync(settings, cancellationToken);
        using (client)
            return await ExecuteEachAsync(settings.Ids.Distinct(), id => $"Task {id}",
                id => client.CancelOfflineTaskAsync(id, cancellationToken), cancellationToken);
    }
}

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
                return await ExecuteEachAsync(settings.Ids.Distinct(), id => $"Deleted {id}",
                    id => DeleteAsync(client, id, cancellationToken), cancellationToken);

            Console.WriteLine($"Deleted {await ClearAsync(client, cancellationToken)} entries.");
            return 0;
        }
    }
}

internal sealed class DeleteOfflineCommand : DeleteByIdCommand
{
    protected override string ClearPrompt => "Clear all offline task records?";
    protected override Task<long> ClearAsync(NetdiskClient client, CancellationToken token) => client.ClearOfflineTasksAsync(token);
    protected override Task DeleteAsync(NetdiskClient client, long id, CancellationToken token) => client.DeleteOfflineTaskAsync(id, token);
}

internal sealed class DeleteRecycleCommand : DeleteByIdCommand
{
    protected override string ClearPrompt => "Permanently delete everything in the recycle bin?";
    protected override Task<long> ClearAsync(NetdiskClient client, CancellationToken token) => client.ClearRecycleBinAsync(token);
    protected override Task DeleteAsync(NetdiskClient client, long id, CancellationToken token) => client.DeleteRecycleEntryAsync(id, token);
}

internal sealed class ListRecycleCommand : RemoteCommand<PageSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PageSettings settings, CancellationToken cancellationToken)
    {
        var (_, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            var table = new Table().AddColumns("File ID", "Path", "Bytes", "Created", "Modified", "MD5", "Remaining time");
            foreach (RecycleEntry recycled in await client.ListRecycleBinAsync(settings.Page, cancellationToken))
            {
                RemoteEntry entry = recycled.Entry;
                table.AddRow(entry.Id.ToString(CultureInfo.InvariantCulture), Markup.Escape(entry.Path + (entry.IsDirectory ? "/" : "")),
                    entry.IsDirectory ? "-" : entry.Size.ToString(CultureInfo.InvariantCulture),
                    entry.Created.ToString("u", CultureInfo.InvariantCulture), entry.Modified.ToString("u", CultureInfo.InvariantCulture),
                    Markup.Escape(entry.Md5), recycled.RemainingTime.ToString(CultureInfo.InvariantCulture));
            }

            AnsiConsole.Write(table);
        }

        return 0;
    }
}

internal sealed class RestoreRecycleCommand : RemoteCommand<IdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, IdSettings settings, CancellationToken cancellationToken)
    {
        var (_, client) = await OpenAsync(settings, cancellationToken);
        using (client)
            return await ExecuteEachAsync(settings.Ids.Distinct(), id => $"File {id}", async id =>
            {
                if (!await client.RestoreRecycleEntryAsync(id, cancellationToken))
                    throw new InvalidDataException("Baidu did not confirm this restore.");
            }, cancellationToken);
    }
}

internal static class OfflineOutput
{
    internal static void Write(IEnumerable<OfflineTask> tasks)
    {
        var table = new Table().AddColumns("Task ID", "Name", "Destination", "Source", "Progress", "Created", "Status");
        foreach (OfflineTask task in tasks)
        {
            table.AddRow(task.Id.ToString(CultureInfo.InvariantCulture), Markup.Escape(task.Name), Markup.Escape(task.Destination),
                Markup.Escape(task.Source), $"{task.CompletedBytes}/{task.Size}", task.Created.ToString("u", CultureInfo.InvariantCulture),
                task.Result != 0 ? "Task not found" : Status(task.Status));
        }
        AnsiConsole.Write(table);
    }

    private static string Status(long status) => status switch
    {
        0 => "Completed",
        1 => "In progress",
        2 => "Server error",
        3 => "Source not found",
        4 => "Timed out",
        5 => "Download failed",
        6 => "Insufficient storage",
        7 => "Canceled",
        _ => $"Unknown ({status})",
    };
}
