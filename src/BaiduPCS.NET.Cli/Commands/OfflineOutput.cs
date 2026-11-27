using System.Globalization;
using BaiduPCS.Netdisk;
using Spectre.Console;

namespace BaiduPCS.Cli.Commands;

internal static class OfflineOutput
{
    internal static void Write(IEnumerable<OfflineTask> tasks, bool raw)
    {
        var table = new Table().AddColumns("Task ID", "Name", "Destination", "Source", "Progress", "Created", "Status");
        var numbers = new DisplayNumbers(raw);
        foreach (OfflineTask task in tasks)
        {
            table.AddRow(task.Id.ToString(CultureInfo.InvariantCulture), Markup.Escape(task.Name), Markup.Escape(task.Destination),
                Markup.Escape(task.Source), $"{numbers.Bytes(task.CompletedBytes, unit: false)}/{numbers.Bytes(task.Size, unit: false)}",
                task.Created.ToString("u", CultureInfo.InvariantCulture),
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
