using BaiduPCS.Netdisk;

namespace BaiduPCS.Transfers;

public static class DownloadDestination
{
    private static readonly char[] InvalidCharacters = Path.GetInvalidFileNameChars();
    private static readonly HashSet<string> WindowsDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "AUX", "CON", "NUL", "PRN", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string Resolve(string root, string relativePath)
    {
        string destination = Path.GetFullPath(root);
        foreach (string component in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
            destination = Path.Combine(destination, SafeName(component));

        return destination;
    }

    public static string ResolvePrefix(string root, string prefix)
    {
        string portable = prefix.Replace('\\', '/');
        if (Path.IsPathRooted(prefix) || portable.StartsWith('/') || portable.Contains(':')
            || portable.Split('/').Any(component => component is "." or ".."))
            throw new ArgumentException("The download prefix must be relative to the save directory.");

        return Resolve(root, portable);
    }

    public static void CreateSafeDirectory(string root, string directory)
    {
        string relative = Path.GetRelativePath(root, directory);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("A download path escaped the selected destination.");

        Directory.CreateDirectory(root);
        if (relative == ".")
            return;

        string current = root;
        foreach (string component in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            NetdiskClient.RejectLink(current);
            Directory.CreateDirectory(current);
        }
    }

    public static string SafeName(string name)
    {
        if (name is "" or "." or "..")
            throw new ArgumentException("A remote file name resolved to an invalid local path.");

        string safe = new(name.Select(character => InvalidCharacters.Contains(character) ? '_' : character).ToArray());
        if (OperatingSystem.IsWindows())
        {
            safe = safe.TrimEnd(' ', '.');
            if (safe.Length == 0 || WindowsDeviceNames.Contains(safe.Split('.', 2)[0].TrimEnd()))
                safe = "_" + safe;
        }

        if (safe.Equals(".baidupcs-partial", StringComparison.OrdinalIgnoreCase))
            safe = "_" + safe;

        return safe;
    }
}
