using BaiduPCS.Netdisk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal abstract class RelocateFilesCommand(bool copy) : RemoteCommand<RemotePaths>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RemotePaths settings, CancellationToken cancellationToken)
    {
        if (settings.Paths.Length < 2)
            throw new ArgumentException("Specify at least one source and a destination.");

        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            string[] sources = await ExpandAsync(client, account, settings.Paths[..^1], cancellationToken);
            if (sources.Contains("/", StringComparer.Ordinal))
                throw new ArgumentException("The remote root cannot be moved or copied.");

            string destination = settings.Paths[^1];
            IReadOnlyList<string> matches = await client.MatchAsync(account.WorkingDirectory, destination, cancellationToken);
            if (matches.Count > 1)
                throw new ArgumentException("The destination pattern matches more than one path.");

            string target = matches.Count == 1 ? matches[0] : RemotePath.Resolve(account.WorkingDirectory, destination);
            RemoteEntry? existing = target == "/" ? null : await client.FindAsync(target, cancellationToken);
            bool isDirectory = target == "/" || existing is { IsDirectory: true };
            if (!isDirectory && (existing is not null || sources.Length != 1))
                throw new ArgumentException("Use an existing directory for multiple sources or an unused path for a single source.");

            var operations = sources.Select(source => (Source: source,
                Target: isDirectory ? RemotePath.Resolve(target, RemotePath.Name(source)) : target)).ToArray();
            if (operations.Select(item => item.Target).Distinct(StringComparer.Ordinal).Count() != operations.Length
                || operations.Any(item => item.Target == item.Source || item.Target.StartsWith(item.Source + '/', StringComparison.Ordinal)))
                throw new ArgumentException("The sources and destination resolve to conflicting paths.");

            return await ExecuteEachAsync(settings, operations, item => $"{item.Source} -> {item.Target}",
                item => copy ? client.CopyAsync(item.Source, item.Target, cancellationToken)
                    : client.MoveAsync(item.Source, item.Target, cancellationToken), cancellationToken);
        }
    }
}
