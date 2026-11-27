using System.Globalization;
using BaiduPCS.Netdisk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class ListRecycleCommand : RemoteCommand<PageSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PageSettings settings, CancellationToken cancellationToken)
    {
        var (_, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            var numbers = new DisplayNumbers(settings.RawNumbers);
            var table = new Table()
                .AddColumn(new TableColumn("File ID").NoWrap())
                .AddColumns("Path", settings.RawNumbers ? "Bytes" : "Size", "Created", "Modified", "MD5", "Remaining time");
            foreach (RecycleEntry recycled in await client.ListRecycleBinAsync(settings.Page, cancellationToken))
            {
                RemoteEntry entry = recycled.Entry;
                table.AddRow(entry.Id.ToString(CultureInfo.InvariantCulture), Markup.Escape(entry.Path + (entry.IsDirectory ? "/" : "")),
                    entry.IsDirectory ? "-" : numbers.Bytes(entry.Size, unit: false),
                    entry.Created.ToString("u", CultureInfo.InvariantCulture), entry.Modified.ToString("u", CultureInfo.InvariantCulture),
                    Markup.Escape(entry.Md5), recycled.RemainingTime.ToString(CultureInfo.InvariantCulture));
            }

            AnsiConsole.Write(table);
        }

        return 0;
    }
}
