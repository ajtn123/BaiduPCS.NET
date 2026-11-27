using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class AddOfflineCommand : RemoteCommand<AddOfflineCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<url>")]
        [UiDescription("One or more source URLs for Baidu to download.")]
        public string[] Sources { get; init; } = [];
        [CommandOption("--path <DIRECTORY>")]
        [UiDescription("Netdisk folder for the downloaded files. Defaults to the current folder.")]
        public string Path { get; init; } = ".";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            string destination = await client.MatchOneAsync(account.WorkingDirectory, settings.Path, cancellationToken);
            var tasks = new List<(int Index, long Id)>();
            int result = await ExecuteEachAsync(settings, settings.Sources.Select((source, index) => (Source: source, Index: index + 1)),
                item => $"Source {item.Index}", async item => tasks.Add((item.Index,
                    await client.AddOfflineTaskAsync(item.Source, destination, cancellationToken))), cancellationToken);
            foreach (var task in tasks)
                Console.WriteLine($"Source {task.Index} → task {task.Id}");

            return result;
        }
    }
}
