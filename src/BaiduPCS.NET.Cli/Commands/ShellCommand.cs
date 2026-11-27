using BaiduPCS.Configuration;
using BaiduPCS.Netdisk;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class ShellCommand : ProfileCommand<CommonSettings>
{
    private const int HistoryLimit = 250;

    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        bool interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
        string historyPath = Path.Combine(Store.DirectoryPath, "command-history.txt");
        List<string> history = interactive ? LoadHistory(historyPath) : [];
        var completions = (ShellCompletions)context.Data!;
        int status = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line;
            if (interactive)
            {
                Profile profile = await Store.ReadAsync(cancellationToken);
                Account? account = profile.ActiveAccount;
                string prompt = account is null ? "baidupcs> " : string.Concat(
                    $"{account.Name}:{account.WorkingDirectory}> ".Where(character => !char.IsControl(character)));
                line = await new ShellEditor(prompt, history, prefix => CompleteAsync(completions, profile, prefix, cancellationToken))
                    .ReadAsync(cancellationToken);
            }
            else
                line = await Console.In.ReadLineAsync(cancellationToken);

            if (line is null)
                return status;

            string[] args;
            try
            {
                args = ShellArguments.Parse(line).Select(token => token.Value).ToArray();
            }
            catch (FormatException error)
            {
                Console.Error.WriteLine(error.Message);
                status = 2;
                continue;
            }

            if (args.Length == 0)
                continue;
            if (args.Length == 1 && completions.CanonicalName(args[0]) == "exit")
                return 0;
            if (args[0] == "shell")
            {
                Console.Error.WriteLine("The BaiduPCS.NET shell is already active.");
                status = 2;
                continue;
            }

            if (completions.CanonicalName(args[0]) == "upload" && args.Skip(1).Any(arg => arg is "-" or "--stdin"))
            {
                Console.Error.WriteLine("Piped uploads must run as a one-shot command outside the BaiduPCS.NET shell.");
                status = 2;
                continue;
            }

            if (interactive && CanRecord(args) && (history.Count == 0 || history[^1] != line))
            {
                history.Add(line);
                if (history.Count > HistoryLimit)
                    history.RemoveAt(0);
                AppendHistory(historyPath, line);
            }

            if (args[0] == "help")
                args = [.. args.Skip(1), "--help"];
            status = await Program.RunAsync(args, cancellationToken);
            if (Program.RestartAfterUpdate)
                return status;
        }
    }

    private static async Task<string[]> CompleteAsync(ShellCompletions completions, Profile profile, string line, CancellationToken token)
    {
        List<ShellArgument> words = ShellArguments.Parse(line, incomplete: true);
        if (words.Count == 0 || (words[^1].End < line.Length && char.IsWhiteSpace(line[^1])))
            words.Add(new ShellArgument("", line.Length, line.Length));

        ShellArgument current = words[^1];
        string[]? commands = completions.Matches(words);
        if (commands is not null)
            return commands.Select(word => line[..current.Start] + word + " ").ToArray();
        if (profile.ActiveAccount is not { } account
            || !TakesRemotePaths(completions.CanonicalName(words[0].Value), words) || current.Value.StartsWith('-'))
            return [];

        int slash = current.Value.LastIndexOf('/');
        string prefix = slash < 0 ? "" : current.Value[..(slash + 1)];
        string name = current.Value[(slash + 1)..];
        string directory = RemotePath.Resolve(account.WorkingDirectory, prefix);
        if (name is "." or "..")
            return [line[..current.Start] + ShellArguments.Quote(current.Value + "/")];

        try
        {
            using var client = new NetdiskClient(profile.Settings, account);
            var matches = new List<string>();
            await foreach (RemoteEntry entry in client.ListAsync(directory, cancellationToken: token))
            {
                if (entry.Name.StartsWith(name, StringComparison.Ordinal))
                    matches.Add(line[..current.Start] + ShellArguments.Quote(prefix + entry.Name + (entry.IsDirectory ? "/" : ""))
                        + (entry.IsDirectory ? "" : " "));
                if (matches.Count == 100)
                    break;
            }

            return [.. matches];
        }
        catch (Exception error) when (error is BaiduApiException or HttpRequestException or InvalidDataException)
        {
            return [];
        }
    }

    private static bool TakesRemotePaths(string command, IReadOnlyList<ShellArgument> words) => command switch
    {
        "cd" or "list" or "tree" or "metadata" or "match" or "mkdir" or "remove" or "copy" or "move" or "rename"
            or "download" or "download-links" or "upload" or "export" => words.Count > 1,
        "share" => words.Count > 2 && words[1].Value == "create",
        "search" or "offline" => words.Count > 2 && words[^2].Value == "--path",
        _ => false,
    };

    internal static bool CanRecord(string[] args) => args switch
    {
        ["run", ..] or ["account", "login" or "import-go" or "set-access-token", ..]
            or ["tool", "encrypt" or "decrypt", ..] or ["share", "create" or "import" or "inspect", ..]
            or ["offline", "add", ..] or ["config", "set", ..] => false,
        _ => true,
    };

    private static List<string> LoadHistory(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadLines(path).TakeLast(HistoryLimit).ToList() : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Could not read shell history.");
            return [];
        }
    }

    private static void AppendHistory(string path, string line)
    {
        try
        {
            string directory = Path.GetDirectoryName(path)!;
            if (OperatingSystem.IsWindows())
                Directory.CreateDirectory(directory);
            else
                Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, Share = FileShare.Read };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var output = new StreamWriter(new FileStream(path, options));
            output.WriteLine(line);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Could not save shell history.");
        }
    }
}
