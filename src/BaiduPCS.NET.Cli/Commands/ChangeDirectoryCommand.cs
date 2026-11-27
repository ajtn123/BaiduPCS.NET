using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class ChangeDirectoryCommand : RemoteCommand<ChangeDirectoryCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<path>")]
        public string Path { get; init; } = "";

        [CommandOption("--list")]
        public bool List { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            string path = await client.MatchOneAsync(account.WorkingDirectory, settings.Path, cancellationToken);
            if (path != "/" && await client.FindAsync(path, cancellationToken) is not { IsDirectory: true })
                throw new InvalidOperationException("The remote directory was not found.");

            await Store.UpdateAsync(profile =>
            {
                if (profile.ActiveAccountId != account.Id)
                    throw new InvalidOperationException("The active account changed. Run cd again.");

                profile.RequireAccount().WorkingDirectory = path;
            }, cancellationToken);
            if (settings.List)
                FileOutput.Write(await client.ListAsync(path, cancellationToken: cancellationToken).ToArrayAsync(cancellationToken),
                    raw: settings.RawNumbers);
        }

        return 0;
    }
}
