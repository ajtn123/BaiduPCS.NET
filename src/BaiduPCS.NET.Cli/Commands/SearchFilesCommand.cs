using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class SearchFilesCommand : RemoteCommand<SearchFilesCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<keyword>")]
        [UiDescription("Text to search for in file and folder names.")]
        public string Keyword { get; init; } = "";

        [CommandOption("--path <PATH>")]
        [UiDescription("Netdisk folder to search. Defaults to the current folder.")]
        public string Path { get; init; } = ".";

        [CommandOption("--recursive")]
        [UiDescription("Include files in subfolders.")]
        public bool Recursive { get; init; }

        [CommandOption("--totals")]
        [UiDescription("Include file IDs, timestamps, and checksums.")]
        public bool Totals { get; init; }

        [CommandOption("--json")]
        [UiDescription("Write results as JSON.")]
        public bool Json { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            string path = await client.MatchOneAsync(account.WorkingDirectory, settings.Path, cancellationToken);
            FileOutput.Write(await client.SearchAsync(path, settings.Keyword, settings.Recursive, cancellationToken),
                settings.Totals, fullPaths: true, json: settings.Json, raw: settings.RawNumbers);
        }

        return 0;
    }
}
