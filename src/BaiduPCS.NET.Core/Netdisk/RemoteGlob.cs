using System.Text;
using System.Text.RegularExpressions;

namespace BaiduPCS.Netdisk;

public sealed partial class NetdiskClient
{
    public async Task<IReadOnlyList<string>> MatchAsync(string workingDirectory, string pattern,
        CancellationToken cancellationToken = default)
    {
        string absolute = RemotePath.Resolve(workingDirectory, pattern);
        var paths = new List<string> { "/" };
        string[] segments = absolute.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < segments.Length; index++)
        {
            string segment = segments[index];
            bool negate = segment.StartsWith("!(", StringComparison.Ordinal) && segment.EndsWith(')');
            if (!negate && !segment.Contains('*') && !segment.Contains('?'))
            {
                string literal = segment.StartsWith(@"\!(", StringComparison.Ordinal) ? segment[1..] : segment;
                paths = paths.Select(parent => RemotePath.Resolve(parent, literal)).ToList();
                continue;
            }

            string[] alternatives = negate ? Regex.Split(segment[2..^1], @"(?<!\\)\|") : [segment];
            if (alternatives.Any(string.IsNullOrEmpty))
                throw new ArgumentException("Negated globs require nonempty alternatives, such as !(draft*|*.tmp).");
            Regex[] matchers = alternatives.Select(CreateMatcher).ToArray();
            bool remaining = index < segments.Length - 1;
            var matches = new List<string>();
            foreach (string parent in paths)
            {
                await foreach (RemoteEntry entry in ListAsync(parent, cancellationToken: cancellationToken))
                {
                    if ((!remaining || entry.IsDirectory) && matchers.Any(matcher => matcher.IsMatch(entry.Name)) != negate
                        && RemotePath.Parent(entry.Path) == parent)
                        matches.Add(entry.Path);
                }
            }

            paths = matches;
        }

        return paths;
    }

    public async Task<string> MatchOneAsync(string workingDirectory, string pattern, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> matches = await MatchAsync(workingDirectory, pattern, cancellationToken);
        return matches.Count switch
        {
            0 => throw new InvalidOperationException("The remote pattern matched no paths."),
            1 => matches[0],
            _ => throw new ArgumentException("The remote pattern must match exactly one path."),
        };
    }

    private static Regex CreateMatcher(string segment)
    {
        var pattern = new StringBuilder(@"\A");
        for (int index = 0; index < segment.Length; index++)
        {
            char character = segment[index];
            if (character == '\\' && index + 1 < segment.Length)
                pattern.Append(Regex.Escape(segment[++index].ToString()));
            else if (character == '*')
                pattern.Append(".*");
            else if (character == '?')
                pattern.Append(@"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[^\uD800-\uDFFF])");
            else
                pattern.Append(Regex.Escape(character.ToString()));
        }

        return new Regex(pattern.Append(@"\z").ToString(), RegexOptions.NonBacktracking | RegexOptions.Singleline);
    }
}
