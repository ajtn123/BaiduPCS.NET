using System.Globalization;
using System.Text.Json;
using BaiduPCS.Netdisk;
using Spectre.Console;

namespace BaiduPCS.Cli.Commands;

internal static class FileOutput
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void Write(IReadOnlyCollection<RemoteEntry> entries, bool details = false, bool fullPaths = false,
        bool json = false, bool raw = false, bool folderSizes = false)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(entries, Json));
            return;
        }

        var numbers = new DisplayNumbers(raw);
        var table = new Table().AddColumns("Name", raw ? "Bytes" : "Size", "Modified");
        if (details)
            table.AddColumns("File ID", "App ID", "Created", "MD5", "Blocks");

        foreach (RemoteEntry entry in entries)
        {
            string name = (fullPaths ? entry.Path : entry.Name) + (entry.IsDirectory && entry.Path != "/" ? "/" : "");
            string[] row = [Markup.Escape(name), entry.IsDirectory && !folderSizes ? "-" : numbers.Bytes(entry.Size, unit: false),
                entry.Modified.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)];
            if (details)
                row = [.. row, entry.Id.ToString(CultureInfo.InvariantCulture), entry.AppId.ToString(CultureInfo.InvariantCulture),
                    entry.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    Markup.Escape(entry.Md5), entry.Blocks.Count.ToString(CultureInfo.InvariantCulture)];

            table.AddRow(row);
        }

        AnsiConsole.Write(table);
        Console.WriteLine($"Files: {entries.Count(entry => !entry.IsDirectory)}, directories: {entries.Count(entry => entry.IsDirectory)}, "
            + numbers.Bytes(entries.Where(entry => folderSizes || !entry.IsDirectory).Sum(entry => entry.Size)));
    }
}
