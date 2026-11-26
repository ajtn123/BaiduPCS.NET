using System.ComponentModel;
using BaiduPCS.Netdisk;
using BaiduPCS.Transfers;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli;

internal sealed class DownloadLinksCommand : RemoteCommand<DownloadLinksCommand.Settings>
{
    public sealed class Settings : RemotePaths
    {
        [CommandOption("--pan")]
        [Description("Use the Pan web endpoint. Links can require a signed-in session.")]
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
                    Uri[] addresses = byPath[path].SelectMany(id => links[id]).ToArray();
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
            foreach (string path in paths)
            {
                try
                {
                    foreach (Uri link in await client.LocateDownloadAsync(path, cancellationToken))
                        Console.WriteLine($"{path}\t{link.AbsoluteUri}");
                }
                catch (Exception error) when (error is BaiduApiException or InvalidDataException or HttpRequestException)
                {
                    Console.Error.WriteLine($"{path}: {(error is HttpRequestException ? "Baidu request failed." : error.Message)}");
                    exitCode = 1;
                }
            }

            return exitCode;
        }
    }
}

internal sealed class ChecksumCommand : AsyncCommand<ChecksumCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<file>")]
        public string[] Files { get; init; } = [];
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        int exitCode = 0;
        foreach (string path in settings.Files)
        {
            try
            {
                await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                FileChecksums checksum = await FileChecksums.ComputeAsync(file, cancellationToken);
                string name = Path.GetFileName(path).Replace(' ', '_').Replace('#', '_');
                Console.WriteLine($"{path}\nLength: {checksum.Length}\nMD5: {checksum.Md5}\n"
                    + $"First 256 KiB MD5: {checksum.SliceMd5}\nCRC32: {checksum.Crc32}\n{checksum.FormatLink(name)}");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Cannot read {path} ({error.GetType().Name}).");
                exitCode = 1;
            }
        }

        return exitCode;
    }
}
