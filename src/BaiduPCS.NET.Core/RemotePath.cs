namespace BaiduPCS;

public static class RemotePath
{
    public static bool IsAbsolute(string? path) => path is { Length: > 0 } && path[0] == '/';

    public static string Resolve(string workingDirectory, string path)
    {
        if (!IsAbsolute(workingDirectory))
            throw new ArgumentException("The remote working directory must be absolute.", nameof(workingDirectory));
        if (workingDirectory.Contains('\0') || path.Contains('\0'))
            throw new ArgumentException("A remote path cannot contain a null character.", nameof(path));

        string combined = IsAbsolute(path) ? path : $"{workingDirectory}/{path}";
        var segments = new List<string>();
        foreach (string segment in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (segment)
            {
                case ".":
                    break;
                case "..":
                    if (segments.Count > 0)
                        segments.RemoveAt(segments.Count - 1);
                    break;
                default:
                    segments.Add(segment);
                    break;
            }
        }

        return "/" + string.Join('/', segments);
    }

    public static string Name(string absolutePath) => absolutePath[(absolutePath.LastIndexOf('/') + 1)..];

    public static string Parent(string absolutePath)
    {
        int last = absolutePath.LastIndexOf('/');
        return last > 0 ? absolutePath[..last] : "/";
    }
}
