using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using BaiduPCS.Configuration;
using BaiduPCS.Netdisk;
using Spectre.Console;
using Spectre.Console.Cli;
using Profile = BaiduPCS.Configuration.Profile;

namespace BaiduPCS.Cli;

internal abstract class RemoteCommand<TSettings> : ProfileCommand<TSettings> where TSettings : CommonSettings
{
    protected async Task<(Account Account, NetdiskClient Client)> OpenAsync(TSettings settings, CancellationToken token)
    {
        var (profile, client) = await OpenProfileAsync(settings, token);
        return (profile.RequireAccount(), client);
    }

    protected async Task<(Profile Profile, NetdiskClient Client)> OpenProfileAsync(TSettings settings, CancellationToken token)
    {
        Profile profile = await Store.ReadAsync(token);
        Account account = profile.RequireAccount();
        if (settings.Verbose || Environment.GetEnvironmentVariable(ProfileStore.VerboseVariable) == "1")
            Console.Error.WriteLine($"Baidu API operation: {GetType().Name}");

        return (profile, new NetdiskClient(profile.Settings, account));
    }

    protected static async Task<string[]> ExpandAsync(NetdiskClient client, Account account, IEnumerable<string> patterns,
        CancellationToken token)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (string pattern in patterns)
        {
            IReadOnlyList<string> matches = await client.MatchAsync(account.WorkingDirectory, pattern, token);
            if (matches.Count == 0)
                throw new InvalidOperationException($"The remote pattern '{pattern}' matched no paths.");

            paths.UnionWith(matches);
        }

        return [.. paths];
    }

    protected static async Task<int> ExecuteEachAsync<T>(CommonSettings settings, IEnumerable<T> items, Func<T, string> describe,
        Func<T, Task> action, CancellationToken token)
    {
        var display = new OperationDisplay(settings, "Item");
        int exitCode = 0;
        var queue = items.Select(item =>
        {
            string name = describe(item);
            return (Item: item, Name: name, Row: display.Enqueue(name));
        }).ToArray();
        await display.RunAsync(async () =>
        {
            foreach (var (item, name, queued) in queue)
            {
                token.ThrowIfCancellationRequested();
                OperationDisplay.Item row = display.Begin(queued);
                try
                {
                    await action(item);
                    display.End(row, "Done", plain: name);
                }
                catch (Exception error) when (error is BaiduApiException or HttpRequestException or InvalidDataException
                    || error is OperationCanceledException && !token.IsCancellationRequested)
                {
                    string failure = error is HttpRequestException ? "Baidu request failed." : error.Message;
                    display.End(row, failure, failed: true, plain: $"{name}: {failure}");
                    exitCode = 1;
                }
            }
        });

        return exitCode;
    }
}

internal sealed class QuotaCommand : RemoteCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        var (_, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            Quota quota = await client.GetQuotaAsync(cancellationToken);
            var numbers = new DisplayNumbers(settings.RawNumbers);
            Console.WriteLine($"Total: {numbers.Bytes(quota.Total)}\nUsed: {numbers.Bytes(quota.Used)}\nFree: {numbers.Bytes(quota.Free)}");
        }

        return 0;
    }
}

internal class OptionalRemotePath : CommonSettings
{
    [CommandArgument(0, "[path]")]
    public string Path { get; init; } = ".";
}

internal sealed class ListFilesCommand : RemoteCommand<ListFilesCommand.Settings>
{
    public sealed class Settings : OptionalRemotePath
    {
        [CommandOption("--totals")]
        [Description("Include file IDs, timestamps, and checksums.")]
        public bool Totals { get; init; }

        [CommandOption("--sort <FIELD>")]
        public FileSort Sort { get; init; }

        [CommandOption("--descending")]
        public bool Descending { get; init; }

        [CommandOption("--json")]
        public bool Json { get; init; }

        [CommandOption("--folder-sizes")]
        [Description("Rank immediate subdirectories by their recursive size.")]
        public bool FolderSizes { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            string path = await client.MatchOneAsync(account.WorkingDirectory, settings.Path, cancellationToken);
            IReadOnlyCollection<RemoteEntry> entries = settings.FolderSizes
                ? await client.GetSubdirectorySizesAsync(path, cancellationToken)
                : await client.ListAsync(path, settings.Sort, settings.Descending, cancellationToken).ToArrayAsync(cancellationToken);
            FileOutput.Write(entries, settings.Totals, json: settings.Json, raw: settings.RawNumbers, folderSizes: settings.FolderSizes);
        }

        return 0;
    }
}

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

internal class RemotePaths : CommonSettings
{
    [CommandArgument(0, "<path>")]
    public string[] Paths { get; init; } = [];
}

internal sealed class MetadataCommand : RemoteCommand<MetadataCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "[path]")]
        public string[] Paths { get; init; } = [];

        [CommandOption("--json")]
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
            string[] missing = paths.Except(entries.Select(entry => entry.Path), StringComparer.Ordinal).ToArray();
            foreach (string path in missing)
                Console.Error.WriteLine($"No metadata returned for {path}.");

            return missing.Length == 0 ? 0 : 1;
        }
    }
}

internal sealed class MatchCommand : RemoteCommand<MatchCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<pattern>")]
        public string Pattern { get; init; } = "";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            foreach (string match in await client.MatchAsync(account.WorkingDirectory, settings.Pattern, cancellationToken))
                Console.WriteLine(match);
        }

        return 0;
    }
}

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

internal sealed class CreateDirectoryCommand : RemoteCommand<RemotePaths>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RemotePaths settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            string[] paths = settings.Paths.Select(path => RemotePath.Resolve(account.WorkingDirectory, path)).Distinct().ToArray();
            return await ExecuteEachAsync(settings, paths, path => path, path => client.CreateDirectoryAsync(path, cancellationToken), cancellationToken);
        }
    }
}

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

internal sealed class CopyFilesCommand() : RelocateFilesCommand(copy: true);
internal sealed class MoveFilesCommand() : RelocateFilesCommand(copy: false);
internal sealed class RenameFileCommand : RemoteCommand<RenameFileCommand.Settings>
{
    public sealed class Settings : RemotePaths
    {
        [CommandOption("--pattern <REGEX>")] public string? Pattern { get; init; }
        [CommandOption("--replacement <TEXT>")] public string? Replacement { get; init; }
        [CommandOption("--dry-run")] public bool DryRun { get; init; }

        public override ValidationResult Validate() => (Pattern, Replacement, Paths.Length) switch
        {
            (null, null, 2) => ValidationResult.Success(),
            (not null, not null, > 0) => ValidationResult.Success(),
            _ => ValidationResult.Error("Supply a source and a new name, or --pattern and --replacement with one or more source paths."),
        };
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            Regex? expression = settings.Pattern is { } pattern
                ? new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) : null;
            string[] sources = await ExpandAsync(client, account, expression is null ? settings.Paths[..1] : settings.Paths, cancellationToken);
            if (expression is null && sources.Length != 1 || sources.Contains("/", StringComparer.Ordinal))
                throw new ArgumentException("Rename requires explicit files or directories below the remote root.");

            var selected = sources.ToHashSet(StringComparer.Ordinal);
            var operations = new List<(string Source, string Target)>();
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (string source in sources)
            {
                string name = expression is null ? settings.Paths[^1] : expression.Replace(RemotePath.Name(source), settings.Replacement!);
                if (name is "" or "." or ".." || name.Contains('/') || name.Contains('\0'))
                    throw new ArgumentException("Every replacement must be a name within the same directory.");

                string target = RemotePath.Resolve(RemotePath.Parent(source), name);
                if (target == source)
                {
                    if (expression is null)
                        throw new ArgumentException("The new name must differ from the source name.");
                    continue;
                }
                if (!targets.Add(target) || selected.Contains(target))
                    throw new ArgumentException("The rename targets overlap. Use separate invocations for swaps or chains.");
                operations.Add((source, target));
            }

            var moving = operations.Select(item => item.Source).ToHashSet(StringComparer.Ordinal);
            foreach (var (source, target) in operations)
            {
                for (string parent = RemotePath.Parent(source); parent != "/"; parent = RemotePath.Parent(parent))
                {
                    if (moving.Contains(parent))
                        throw new ArgumentException("Rename parent directories and their children separately.");
                }
                if (settings.DryRun || expression is not null)
                {
                    if (await client.FindAsync(source, cancellationToken) is null)
                        throw new ArgumentException($"The rename source does not exist: {source}.");
                    if (await client.FindAsync(target, cancellationToken) is not null)
                        throw new ArgumentException($"The rename target already exists: {target}.");
                }
            }

            if (settings.DryRun)
            {
                foreach (var (source, target) in operations)
                    Console.WriteLine($"{source} -> {target}");
                return 0;
            }

            return await ExecuteEachAsync(settings, operations, item => $"{item.Source} -> {item.Target}",
                item => client.MoveAsync(item.Source, item.Target, cancellationToken), cancellationToken);
        }
    }
}

internal static class FileOutput
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void Write(IReadOnlyCollection<RemoteEntry> entries, bool details = false, bool fullPaths = false,
        bool json = false, bool raw = false, bool folderSizes = false)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(entries, Json));
            return;
        }

        var numbers = new DisplayNumbers(raw);
        var table = new Table().AddColumns("Name", raw ? "Bytes" : "Size", "Modified");
        if (details)
            table.AddColumns("File ID", "App ID", "Created", "MD5", "Blocks");

        foreach (RemoteEntry entry in entries)
        {
            string name = (fullPaths ? entry.Path : entry.Name) + (entry.IsDirectory && entry.Path != "/" ? "/" : "");
            string[] row = [Markup.Escape(name), entry.IsDirectory && !folderSizes ? "-" : numbers.Bytes(entry.Size, unit: false),
                entry.Modified.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)];
            if (details)
                row = [.. row, entry.Id.ToString(CultureInfo.InvariantCulture), entry.AppId.ToString(CultureInfo.InvariantCulture),
                    entry.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    Markup.Escape(entry.Md5), entry.Blocks.Count.ToString(CultureInfo.InvariantCulture)];

            table.AddRow(row);
        }

        AnsiConsole.Write(table);
        Console.WriteLine($"Files: {entries.Count(entry => !entry.IsDirectory)}, directories: {entries.Count(entry => entry.IsDirectory)}, "
            + numbers.Bytes(entries.Where(entry => folderSizes || !entry.IsDirectory).Sum(entry => entry.Size)));
    }
}
