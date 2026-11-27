using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class RenameFileCommand : RemoteCommand<RenameFileCommand.Settings>
{
    public sealed class Settings : RemotePaths
    {
        [CommandOption("--pattern <REGEX>")]
        [UiDescription("Match file or folder names with a regular expression.")]
        public string? Pattern { get; init; }
        [CommandOption("--replacement <TEXT>")]
        [UiDescription("Replacement text for the regular expression match.")]
        public string? Replacement { get; init; }
        [CommandOption("--dry-run")]
        [UiDescription("Preview renamed paths without changing them.")]
        public bool DryRun { get; init; }

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
