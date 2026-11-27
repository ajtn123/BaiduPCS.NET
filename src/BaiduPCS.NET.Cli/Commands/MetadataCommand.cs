using BaiduPCS.Netdisk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class MetadataCommand : RemoteCommand<MetadataCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "[path]")]
        [UiDescription("Netdisk paths or patterns. Defaults to the current folder.")]
        public string[] Paths { get; init; } = [];

        [CommandOption("--json")]
        [UiDescription("Write results as JSON.")]
        public bool Json { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            string[] paths = await ExpandAsync(client, account, settings.Paths.Length == 0 ? ["."] : settings.Paths, cancellationToken);
            IReadOnlyList<RemoteEntry> entries = await client.GetMetadataAsync(paths, cancellationToken);
            FileOutput.Write(entries, details: true, fullPaths: true, json: settings.Json, raw: settings.RawNumbers);
            string[] missing = [.. paths.Except(entries.Select(entry => entry.Path), StringComparer.Ordinal)];
            foreach (string path in missing)
                Console.Error.WriteLine($"No metadata returned for {path}.");

            return missing.Length == 0 ? 0 : 1;
        }
    }
}
