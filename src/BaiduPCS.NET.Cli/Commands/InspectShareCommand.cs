using System.Text.Json;
using BaiduPCS.Netdisk;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class InspectShareCommand : RemoteCommand<InspectShareCommand.Settings>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<link>")]
        [UiDescription("Baidu share URL or copied share text.")]
        public string Link { get; init; } = "";
        [CommandOption("--code <CODE>")]
        [UiDescription("Supply the share extraction code.")]
        public string? Code { get; init; }
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
