using System.Text;
using System.Text.RegularExpressions;

namespace BaiduPCS.Netdisk;

public sealed partial class GlobSegment
{
    private readonly bool _negate;
    private readonly Regex[] _matchers;

    public GlobSegment(string pattern, bool ignoreCase = false)
    {
        _negate = IsNegated(pattern);
        string[] alternatives = _negate ? UnescapedPipe().Split(pattern[2..^1]) : [pattern];
        if (alternatives.Any(string.IsNullOrEmpty))
            throw new ArgumentException("Globs require nonempty patterns, such as !(draft*|*.tmp).");

        var options = RegexOptions.NonBacktracking | RegexOptions.Singleline | RegexOptions.CultureInvariant;
        if (ignoreCase)
            options |= RegexOptions.IgnoreCase;
        _matchers = alternatives.Select(alternative => CreateMatcher(alternative, options)).ToArray();
    }

    public bool Matches(string name) => _matchers.Any(matcher => matcher.IsMatch(name)) != _negate;

    public static bool IsPattern(string segment) => IsNegated(segment) || segment.Contains('*') || segment.Contains('?');

    private static bool IsNegated(string segment) => segment.StartsWith("!(", StringComparison.Ordinal) && segment.EndsWith(')');

    [GeneratedRegex(@"(?<!\\)\|", RegexOptions.CultureInvariant)]
    private static partial Regex UnescapedPipe();

    private static Regex CreateMatcher(string segment, RegexOptions options)
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

        return new Regex(pattern.Append(@"\z").ToString(), options);
    }
}
