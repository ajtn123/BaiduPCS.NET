using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class SearchFilesCommand : RemoteCommand<SearchFilesCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<keyword>")]
        public string Keyword { get; init; } = "";

        [CommandOption("--path <PATH>")]
        public string Path { get; init; } = ".";

        [CommandOption("--recursive")]
        public bool Recursive { get; init; }

        [CommandOption("--totals")]
        public bool Totals { get; init; }

        [CommandOption("--json")]
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
