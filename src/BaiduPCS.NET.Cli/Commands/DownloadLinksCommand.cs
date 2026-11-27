using BaiduPCS.Netdisk;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class DownloadLinksCommand : RemoteCommand<DownloadLinksCommand.Settings>
{
    public sealed class Settings : RemotePaths
    {
        [CommandOption("--pan")]
        [UiDescription("Use the Pan web endpoint. Links can require a signed-in session.")]
        public bool Pan { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            string[] paths = await ExpandAsync(client, account, settings.Paths, cancellationToken);
            if (settings.Pan)
            {
                IReadOnlyList<RemoteEntry> entries = await client.GetMetadataAsync(paths, cancellationToken);
                var links = (await client.LocatePanDownloadAsync(entries.Select(entry => entry.Id), cancellationToken))
                    .ToLookup(link => link.FileId, link => link.Url);
                var byPath = entries.ToLookup(entry => entry.Path, entry => entry.Id, StringComparer.Ordinal);
                int result = 0;
                foreach (string path in paths)
                {
                    Uri[] addresses = [.. byPath[path].SelectMany(id => links[id])];
                    if (addresses.Length == 0)
                    {
                        Console.Error.WriteLine($"No Pan download link for {path}.");
                        result = 1;
                    }

                    foreach (Uri url in addresses)
                        Console.WriteLine($"{path}\t{url.AbsoluteUri}");
                }

                return result;
            }

            int exitCode = 0;
            var display = new OperationDisplay(settings, "File");
            var queue = paths.Select(path => (Path: path, Row: display.Enqueue(path))).ToArray();
            await display.RunAsync(async () =>
            {
                foreach (var (path, queued) in queue)
                {
                    var row = display.Begin(queued);
                    try
                    {
                        IReadOnlyList<Uri> links = await client.LocateDownloadAsync(path, cancellationToken);
                        string result = string.Join(Environment.NewLine, links.Select(link => link.AbsoluteUri));
                        string plain = string.Join(Environment.NewLine, links.Select(link => $"{path}\t{link.AbsoluteUri}"));
                        display.End(row, result, plain: plain);
                    }
                    catch (Exception error) when (error is BaiduApiException or InvalidDataException or HttpRequestException)
                    {
                        string failure = error is HttpRequestException ? "Baidu request failed." : error.Message;
                        display.End(row, failure, failed: true, plain: $"{path}: {failure}");
                        exitCode = 1;
                    }
                }
            });

            return exitCode;
        }
    }
}
