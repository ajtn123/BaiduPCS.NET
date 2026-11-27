using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class RemoveFilesCommand : RemoteCommand<RemotePaths>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RemotePaths settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            string[] paths = await ExpandAsync(client, account, settings.Paths, cancellationToken);
            if (paths.Contains("/", StringComparer.Ordinal))
                throw new ArgumentException("The remote root cannot be removed.");

            return await ExecuteEachAsync(settings, paths, path => path, path => client.RemoveAsync(path, cancellationToken), cancellationToken);
        }
    }
}
