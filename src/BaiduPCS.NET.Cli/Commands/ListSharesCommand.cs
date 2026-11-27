using System.Globalization;
using BaiduPCS.Netdisk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class ListSharesCommand : RemoteCommand<PageSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PageSettings settings, CancellationToken cancellationToken)
    {
        var (_, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            var table = new Table().AddColumns("Share ID", "Link", "Code", "Path", "Expires", "Views");
            foreach (ShareRecord share in await client.ListSharesAsync(settings.Page, cancellationToken))
            {
                table.AddRow(share.Id.ToString(CultureInfo.InvariantCulture), Markup.Escape(share.Link), Markup.Escape(share.Code ?? ""),
                    Markup.Escape(share.Path), share.Expired ? "Expired" : share.Expires?.ToString("u", CultureInfo.InvariantCulture) ?? "Never",
                    share.Views.ToString(CultureInfo.InvariantCulture));
            }

            AnsiConsole.Write(table);
        }

        return 0;
    }
}
