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
            if (!GlobSegment.IsPattern(segment))
            {
                string literal = segment.StartsWith(@"\!(", StringComparison.Ordinal) ? segment[1..] : segment;
                paths = [.. paths.Select(parent => RemotePath.Resolve(parent, literal))];
                continue;
            }

            var matcher = new GlobSegment(segment);
            bool remaining = index < segments.Length - 1;
            var matches = new List<string>();
            foreach (string parent in paths)
            {
                await foreach (RemoteEntry entry in ListAsync(parent, cancellationToken: cancellationToken))
                {
                    if ((!remaining || entry.IsDirectory) && matcher.Matches(entry.Name)
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
}
