using System.Globalization;
using System.Text.Json;
using BaiduPCS.Netdisk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli;

internal sealed class CreateShareCommand : RemoteCommand<CreateShareCommand.Settings>
{
    public sealed class Settings : RemotePaths
    {
        [CommandOption("--code <CODE>")] public string? Code { get; init; }
        [CommandOption("--days <DAYS>")] public int Days { get; init; }
        [CommandOption("--combined")] public bool Combined { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            string[] paths = await ExpandAsync(client, account, settings.Paths, cancellationToken);
            CreatedShare share = await client.CreateShareAsync(paths, settings.Code, settings.Days, cancellationToken);
            Console.WriteLine($"Share ID: {share.Id}");
            if (settings.Combined)
            {
                var link = new UriBuilder(share.Link);
                link.Query = (link.Query.Length == 0 ? "" : link.Query[1..] + "&") + "pwd=" + Uri.EscapeDataString(share.Code);
                Console.WriteLine(link.Uri.AbsoluteUri);
            }
            else
                Console.WriteLine($"{share.Link.AbsoluteUri}\nExtraction code: {share.Code}");
        }

        return 0;
    }
}

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

internal sealed class ImportShareCommand : RemoteCommand<ImportShareCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<link>")] public string Link { get; init; } = "";
        [CommandOption("--code <CODE>")] public string? Code { get; init; }
        [CommandOption("--file-id <ID>")] public long[] FileIds { get; init; } = [];
        [CommandOption("--destination <PATH>")] public string Destination { get; init; } = ".";
        [CommandOption("--collect")] public bool Collect { get; init; }
        [CommandOption("--download")] public bool Download { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        IReadOnlyList<string> saved;
        using (client)
        {
            string destination = await client.MatchOneAsync(account.WorkingDirectory, settings.Destination, cancellationToken);
            saved = await client.ImportShareAsync(settings.Link, settings.Code, destination, settings.FileIds,
                settings.Collect, cancellationToken);
        }

        if (saved.Count == 0)
        {
            Console.WriteLine("Baidu reported a successful share import without returning saved paths.");
            return settings.Download ? 1 : 0;
        }

        foreach (string path in saved)
            Console.WriteLine($"Imported {path}");
        if (!settings.Download)
            return 0;

        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        string[] options = settings.RawNumbers ? ["--raw-numbers"] : [];
        return await Program.RunAsync(["download", .. options, .. saved], cancellationToken);
    }
}

internal sealed class InspectShareCommand : RemoteCommand<InspectShareCommand.Settings>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<link>")] public string Link { get; init; } = "";
        [CommandOption("--code <CODE>")] public string? Code { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (_, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            ShareInventory inventory = await client.InspectShareAsync(settings.Link, settings.Code, cancellationToken);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                inventory.Files,
                FileCount = inventory.Files.Count(file => !file.IsDirectory),
                DirectoryCount = inventory.Files.Count(file => file.IsDirectory),
                TotalBytes = inventory.Files.Where(file => !file.IsDirectory).Sum(file => file.Size),
                inventory.Requests,
                inventory.Truncated,
                inventory.UnlistedDirectories,
                inventory.Complete,
            }, Json));
            return inventory.Complete ? 0 : 1;
        }
    }
}
