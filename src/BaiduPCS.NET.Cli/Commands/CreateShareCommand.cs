using BaiduPCS.Netdisk;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

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
