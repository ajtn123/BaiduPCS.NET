using System.Diagnostics;
using System.Globalization;
using System.Text;
using BaiduPCS.Configuration;
using BaiduPCS.Netdisk;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace BaiduPCS.Cli;

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
            if (args is ["exit"])
                return 0;
            if (args[0] == "shell")
            {
                Console.Error.WriteLine("The BaiduPCS.NET shell is already active.");
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
        if (profile.ActiveAccount is not { } account || !TakesRemotePaths(words) || current.Value.StartsWith('-'))
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

    private static bool TakesRemotePaths(IReadOnlyList<ShellArgument> words) => words[0].Value switch
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

internal sealed class ShellEditor(string prompt, IReadOnlyList<string> history, Func<string, Task<string[]>> complete)
{
    private readonly StringBuilder _line = new();
    private readonly RenderOptions _renderOptions = RenderOptions.Create(Spectre.Console.AnsiConsole.Console);
    private int _position;
    private int _previousWidth;

    internal async Task<string?> ReadAsync(CancellationToken token)
    {
        int historyIndex = history.Count;
        string draft = "";
        string[] suggestions = [];
        int suggestion = 0;
        Display();
        while (true)
        {
            while (!Console.KeyAvailable)
                await Task.Delay(20, token);

            token.ThrowIfCancellationRequested();
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            bool control = key.Modifiers.HasFlag(ConsoleModifiers.Control);
            if (key.Key != ConsoleKey.Tab)
                suggestions = [];

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    _position = _line.Length;
                    Display();
                    Console.WriteLine();
                    return _line.ToString();
                case ConsoleKey.D when control && _line.Length == 0:
                case ConsoleKey.Z when control && _line.Length == 0:
                    Console.WriteLine();
                    return null;
                case ConsoleKey.LeftArrow when _position > 0:
                    _position = PreviousPosition();
                    break;
                case ConsoleKey.RightArrow when _position < _line.Length:
                    _position = NextPosition();
                    break;
                case ConsoleKey.A when control:
                case ConsoleKey.Home:
                    _position = 0;
                    break;
                case ConsoleKey.E when control:
                case ConsoleKey.End:
                    _position = _line.Length;
                    break;
                case ConsoleKey.UpArrow when historyIndex > 0:
                    if (historyIndex == history.Count)
                        draft = _line.ToString();
                    Replace(history[--historyIndex]);
                    break;
                case ConsoleKey.DownArrow when historyIndex < history.Count:
                    Replace(++historyIndex == history.Count ? draft : history[historyIndex]);
                    break;
                case ConsoleKey.Backspace when _position > 0:
                    int previous = PreviousPosition();
                    _line.Remove(previous, _position - previous);
                    _position = previous;
                    break;
                case ConsoleKey.Delete when _position < _line.Length:
                case ConsoleKey.D when control && _position < _line.Length:
                    _line.Remove(_position, NextPosition() - _position);
                    break;
                case ConsoleKey.Tab when _position == _line.Length:
                    if (suggestions.Length == 0)
                    {
                        suggestions = await complete(_line.ToString());
                        suggestion = 0;
                    }
                    if (suggestions.Length > 0)
                        Replace(suggestions[suggestion++ % suggestions.Length]);
                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                        _line.Insert(_position++, key.KeyChar);
                    break;
            }

            Display();
        }
    }

    private void Replace(string value)
    {
        _line.Clear().Append(value);
        _position = _line.Length;
    }

    private int PreviousPosition() => StringInfo.ParseCombiningCharacters(_line.ToString()).LastOrDefault(index => index < _position);

    private int NextPosition() => StringInfo.ParseCombiningCharacters(_line.ToString())
        .FirstOrDefault(index => index > _position, _line.Length);

    private void Display()
    {
        string text = _line.ToString();
        string prefix = prompt;
        int limit = Math.Max(1, Console.WindowWidth - 1);
        if (DisplayWidth(prefix) >= limit)
            prefix = limit > 2 ? "> " : "";

        int start = 0;
        int end = text.Length;
        int available = limit - DisplayWidth(prefix);
        int[] positions = [.. StringInfo.ParseCombiningCharacters(text), text.Length];
        int beforeCursor = DisplayWidth(text[.._position]);
        for (int index = 1; beforeCursor > available && index < positions.Length && positions[index] <= _position; index++)
        {
            int next = positions[index];
            beforeCursor -= DisplayWidth(text[start..next]);
            start = next;
        }

        int visibleWidth = DisplayWidth(text[start..end]);
        for (int index = positions.Length - 2; visibleWidth > available && index >= 0 && positions[index] >= _position; index--)
        {
            int previous = positions[index];
            visibleWidth -= DisplayWidth(text[previous..end]);
            end = previous;
        }

        int width = DisplayWidth(prefix) + visibleWidth;
        Console.Write('\r');
        Console.Write(prefix);
        Console.Write(text.AsSpan(start, end - start));
        if (_previousWidth > width)
            Console.Write(new string(' ', Math.Min(_previousWidth, limit) - width));
        Console.Write('\r');
        Console.Write(prefix);
        Console.Write(text.AsSpan(start, _position - start));
        _previousWidth = width;
    }

    private int DisplayWidth(string text) => ((IRenderable)new Spectre.Console.Text(text)).Measure(_renderOptions, int.MaxValue).Max;
}

internal sealed record ShellArgument(string Value, int Start, int End);

internal static class ShellArguments
{
    internal static List<ShellArgument> Parse(string line, bool incomplete = false)
    {
        var args = new List<ShellArgument>();
        var token = new StringBuilder();
        char quote = '\0';
        int start = -1;
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (quote == '\0' && char.IsWhiteSpace(character))
            {
                if (start >= 0)
                    args.Add(new(token.ToString(), start, index));
                token.Clear();
                start = -1;
                continue;
            }

            if (start < 0)
                start = index;
            if (character == '\\' && index + 1 < line.Length
                && (char.IsWhiteSpace(line[index + 1]) || IsQuote(line[index + 1]) || line[index + 1] == '\\'))
                token.Append(line[++index]);
            else if (IsQuote(character) && (quote == '\0' || quote == character))
                quote = quote == '\0' ? character : '\0';
            else
                token.Append(character);
        }

        if (quote != '\0' && !incomplete)
            throw new FormatException("The command contains an unclosed quote.");
        if (start >= 0)
            args.Add(new(token.ToString(), start, line.Length));

        return args;
    }

    internal static string Quote(string value) => value.Any(character => char.IsWhiteSpace(character) || IsQuote(character) || character == '\\')
        ? "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
        : value;

    private static bool IsQuote(char character) => character is '\'' or '"' or '`';
}

internal sealed class RunProcessCommand : AsyncCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        string[] arguments = context.Remaining.Raw.ToArray();
        if (arguments.Length == 0)
            throw new ArgumentException("Use 'run -- EXECUTABLE ARG...' to run a local process.");

        var start = new ProcessStartInfo(arguments[0]) { UseShellExecute = false };
        foreach (string argument in arguments.Skip(1))
            start.ArgumentList.Add(argument);

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the requested program.");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}

internal sealed class ClearCommand : Command<CommonSettings>
{
    public override int Execute(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        if (!Console.IsOutputRedirected)
            Console.Clear();
        return 0;
    }
}

internal sealed class ExitCommand : Command<CommonSettings>
{
    public override int Execute(CommandContext context, CommonSettings settings, CancellationToken cancellationToken) => 0;
}

internal sealed class ShellCompletions
{
    private readonly Dictionary<string, List<string>> _commands = new(StringComparer.Ordinal) { [""] = ["help"] };

    internal ICommandConfigurator Register<T>(IConfigurator config, string name) where T : class, ICommand
    {
        _commands[""].Add(name);
        return config.AddCommand<T>(name);
    }

    internal ICommandConfigurator Register<T>(IConfigurator<CommandSettings> config, string branch, string name)
        where T : class, ICommandLimiter<CommandSettings>
    {
        if (!_commands.TryGetValue(branch, out List<string>? commands))
        {
            _commands[""].Add(branch);
            _commands.Add(branch, commands = []);
        }

        commands.Add(name);
        return config.AddCommand<T>(name);
    }

    internal string[]? Matches(IReadOnlyList<ShellArgument> words)
    {
        int start = words.Count > 1 && words[0].Value == "help" ? 1 : 0;
        string parent = string.Join(' ', words.Skip(start).Take(words.Count - start - 1).Select(word => word.Value));
        return _commands.TryGetValue(parent, out List<string>? choices)
            ? choices.Where(word => word.StartsWith(words[^1].Value, StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray()
            : null;
    }
}
