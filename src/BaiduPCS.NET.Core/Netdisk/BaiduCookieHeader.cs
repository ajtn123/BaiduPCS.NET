using System.Net;
using BaiduPCS.Configuration;

namespace BaiduPCS.Netdisk;

internal static class BaiduCookieHeader
{
    internal static Dictionary<string, string> Parse(string header)
    {
        ReadOnlySpan<char> remaining = header.AsSpan().Trim();
        if (remaining.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))
            remaining = remaining["Cookie:".Length..].TrimStart();
        if (remaining.ContainsAny('\r', '\n', '\0'))
            throw new ArgumentException("Supply a single Baidu Cookie request header.");

        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (!remaining.IsEmpty)
        {
            int end = remaining.IndexOf(';');
            int equals = remaining[..(end < 0 ? remaining.Length : end)].IndexOf('=');
            if (equals >= 0)
            {
                ReadOnlySpan<char> value = remaining[(equals + 1)..].TrimStart();
                if (!value.IsEmpty && value[0] == '"')
                {
                    int closing = value[1..].IndexOf('"');
                    if (closing < 0)
                        break; // The remaining delimiters belong to an unfinished quoted field.

                    int afterQuote = closing + 2;
                    int next = value[afterQuote..].IndexOf(';');
                    end = next < 0 ? -1 : remaining.Length - value.Length + afterQuote + next;
                }

                ReadOnlySpan<char> name = remaining[..equals].Trim();
                ReadOnlySpan<char> rawValue = remaining[(equals + 1)..(end < 0 ? remaining.Length : end)];
                if (IsCookieName(name) && TryNormalizeValue(rawValue, out string normalized))
                    cookies[name.ToString()] = normalized;
            }

            remaining = end < 0 ? [] : remaining[(end + 1)..];
        }

        return cookies;
    }

    internal static bool TryNormalizeValue(ReadOnlySpan<char> value, out string normalized)
    {
        value = value.Trim();
        if (value is ['"', .. var quoted, '"'])
            value = quoted;

        normalized = "";
        foreach (char character in value)
        {
            if (character is < ' ' or > '~' or '"' or ';' or '\\')
                return false;
        }

        int quotes = NeedsQuotes(value) ? 2 : 0;
        if (value.Length + quotes > CookieContainer.DefaultCookieLengthLimit)
            return false;

        normalized = value.ToString();
        return true;
    }

    internal static string Format(IEnumerable<KeyValuePair<string, string>> cookies) =>
        string.Join("; ", cookies.Select(pair => $"{pair.Key}={WireValue(pair.Value)}"));

    internal static string WireValue(string value) => NeedsQuotes(value) ? $"\"{value}\"" : value;

    internal static string NormalizeCredential(string name, string value)
    {
        if (!TryNormalizeValue(value, out string normalized) || normalized.Length == 0)
            throw new ArgumentException($"The saved {name} credential has an invalid value.");

        return normalized;
    }

    internal static string GetBduss(Credentials credentials) => credentials.Bduss.Length == 0
        ? Parse(credentials.Cookies).GetValueOrDefault("BDUSS", "")
        : NormalizeCredential("BDUSS", credentials.Bduss);

    private static bool NeedsQuotes(ReadOnlySpan<char> value) => value.ContainsAny(' ', ',');

    private static bool IsCookieName(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty || name[0] == '$')
            return false;

        foreach (char character in name)
        {
            if (!char.IsAsciiLetterOrDigit(character) && !"!#$%&'*+-.^_\u0060|~".Contains(character))
                return false;
        }

        return true;
    }
}
