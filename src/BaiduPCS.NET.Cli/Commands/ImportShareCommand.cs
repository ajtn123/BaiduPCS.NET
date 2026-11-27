using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class ImportShareCommand : RemoteCommand<ImportShareCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<link>")]
        [UiDescription("Baidu share URL or copied share text.")]
        public string Link { get; init; } = "";
        [CommandOption("--code <CODE>")]
        [UiDescription("Supply the share extraction code.")]
        public string? Code { get; init; }
        [CommandOption("--file-id <ID>")]
        [UiDescription("Import only the specified shared file IDs. Repeat to select multiple files.")]
        public long[] FileIds { get; init; } = [];
        [CommandOption("--destination <PATH>")]
        [UiDescription("Netdisk folder to copy the files into. Defaults to the current folder.")]
        public string Destination { get; init; } = ".";
        [CommandOption("--collect")]
        [UiDescription("Collect the share in Baidu Netdisk.")]
        public bool Collect { get; init; }
        [CommandOption("--download")]
        [UiDescription("Download the files after importing them into Netdisk.")]
        public bool Download { get; init; }
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
