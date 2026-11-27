using Spectre.Console.Cli;

namespace BaiduPCS.Cli;

internal sealed class ShellCompletions
{
    private readonly Dictionary<string, List<string>> _commands = new(StringComparer.Ordinal) { [""] = ["help"] };
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);

    internal ICommandConfigurator Register<T>(IConfigurator config, string name, params string[] aliases) where T : class, ICommand
    {
        ICommandConfigurator command = config.AddCommand<T>(name);
        _commands[""].Add(name);
        foreach (string alias in aliases)
        {
            command.WithAlias(alias);
            _aliases.Add(alias, name);
            _commands[""].Add(alias);
        }
        return command;
    }

    internal string CanonicalName(string name) => _aliases.GetValueOrDefault(name, name);

    internal ICommandConfigurator Register<T>(IConfigurator<CommandSettings> config, string branch, string name, params string[] aliases)
        where T : class, ICommandLimiter<CommandSettings>
    {
        if (!_commands.TryGetValue(branch, out List<string>? commands))
        {
            _commands[""].Add(branch);
            _commands.Add(branch, commands = []);
        }

        commands.Add(name);
        ICommandConfigurator command = config.AddCommand<T>(name);
        foreach (string alias in aliases)
        {
            command.WithAlias(alias);
            commands.Add(alias);
        }
        return command;
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
