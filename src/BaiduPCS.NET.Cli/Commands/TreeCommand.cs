using BaiduPCS.Netdisk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class TreeCommand : RemoteCommand<TreeCommand.Settings>
{
    public sealed class Settings : OptionalRemotePath
    {
        [CommandOption("--depth <LEVELS>")]
        public int Depth { get; init; } = -1;

        [CommandOption("--file-ids")]
        public bool FileIds { get; init; }

        public override ValidationResult Validate() => Depth >= -1
            ? ValidationResult.Success() : ValidationResult.Error("Depth must be -1 or greater.");
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            string path = await client.MatchOneAsync(account.WorkingDirectory, settings.Path, cancellationToken);
            var tree = new Tree(Markup.Escape(path));
            await AddChildrenAsync(client, tree, path, 0, settings, cancellationToken);
            AnsiConsole.Write(tree);
        }

        return 0;
    }

    private static async Task AddChildrenAsync(NetdiskClient client, IHasTreeNodes parent, string path, int level,
        Settings settings, CancellationToken token)
    {
        await foreach (RemoteEntry entry in client.ListAsync(path, cancellationToken: token))
        {
            if (RemotePath.Parent(entry.Path) != path || entry.Path == path)
                continue;

            string label = entry.Name + (entry.IsDirectory ? "/" : "") + (settings.FileIds ? $" ({entry.Id})" : "");
            TreeNode child = parent.AddNode(Markup.Escape(label));
            if (entry.IsDirectory && (settings.Depth < 0 || level < settings.Depth))
                await AddChildrenAsync(client, child, entry.Path, level + 1, settings, token);
        }
    }
}
