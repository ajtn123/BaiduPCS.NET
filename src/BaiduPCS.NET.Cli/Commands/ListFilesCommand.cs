using System.ComponentModel;
using BaiduPCS.Netdisk;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class ListFilesCommand : RemoteCommand<ListFilesCommand.Settings>
{
    public sealed class Settings : OptionalRemotePath
    {
        [CommandOption("--totals")]
        [Description("Include file IDs, timestamps, and checksums.")]
        public bool Totals { get; init; }

        [CommandOption("--sort <FIELD>")]
        public FileSort Sort { get; init; }

        [CommandOption("--descending")]
        public bool Descending { get; init; }

        [CommandOption("--json")]
        public bool Json { get; init; }

        [CommandOption("--folder-sizes")]
        [Description("Rank immediate subdirectories by their recursive size.")]
        public bool FolderSizes { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            string path = await client.MatchOneAsync(account.WorkingDirectory, settings.Path, cancellationToken);
            IReadOnlyCollection<RemoteEntry> entries = settings.FolderSizes
                ? await client.GetSubdirectorySizesAsync(path, cancellationToken)
                : await client.ListAsync(path, settings.Sort, settings.Descending, cancellationToken).ToArrayAsync(cancellationToken);
            FileOutput.Write(entries, settings.Totals, json: settings.Json, raw: settings.RawNumbers, folderSizes: settings.FolderSizes);
        }

        return 0;
    }
}
