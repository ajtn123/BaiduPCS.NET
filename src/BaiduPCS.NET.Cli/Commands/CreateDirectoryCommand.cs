using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class CreateDirectoryCommand : RemoteCommand<RemotePaths>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RemotePaths settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            string[] paths = [.. settings.Paths.Select(path => RemotePath.Resolve(account.WorkingDirectory, path)).Distinct()];
            return await ExecuteEachAsync(settings, paths, path => path, path => client.CreateDirectoryAsync(path, cancellationToken), cancellationToken);
        }
    }
}
