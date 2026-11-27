using System.Text;

namespace BaiduPCS.Cli;

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
