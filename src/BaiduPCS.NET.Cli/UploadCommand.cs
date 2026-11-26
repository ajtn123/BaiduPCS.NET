using System.Collections.Concurrent;
using System.Diagnostics;
using BaiduPCS.Netdisk;
using BaiduPCS.Transfers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli;

internal sealed class UploadCommand : RemoteCommand<UploadCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<source-and-remote-directory>")] public string[] Paths { get; init; } = [];
        [CommandOption("--parallel <WORKERS>")] public int? Parallel { get; init; }
        [CommandOption("--file-concurrency <FILES>")] public int? FileConcurrency { get; init; }
        [CommandOption("--retries <COUNT>")] public int Retries { get; init; } = 3;
        [CommandOption("--skip-rapid-check")] public bool SkipRapidCheck { get; init; }
        [CommandOption("--conflict <POLICY>")] public string? Conflict { get; init; }
        [CommandOption("--as <NAME>")] public string? RemoteName { get; init; }

        public override ValidationResult Validate() => Paths.Length < 2
            ? ValidationResult.Error("Specify at least one local source, followed by the remote directory.")
            : RemoteName is not null && (Paths.Length != 2 || RemoteName is "" or "." or ".." || RemoteName.Contains('/'))
                ? ValidationResult.Error("--as requires one local file and a name within the remote directory.")
            : Parallel is < 1 || FileConcurrency is < 1 || Retries < 0
                ? ValidationResult.Error("Concurrency must be positive and retries cannot be negative.") : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (profile, client) = await OpenProfileAsync(settings, cancellationToken);
        using (client)
        {
            string remote = await client.MatchOneAsync(profile.RequireAccount().WorkingDirectory, settings.Paths[^1], cancellationToken);
            var options = new UploadOptions
            {
                Parallel = settings.Parallel ?? profile.Settings.UploadParallel,
                Retries = settings.Retries,
                SkipRapidCheck = settings.SkipRapidCheck,
                Conflict = settings.Conflict is null ? profile.Settings.UploadConflict : SetConfigCommand.ParseConflict(settings.Conflict),
            };
            string checkpoints = Path.Combine(Store.DirectoryPath, "uploads");
            var failures = new ConcurrentQueue<string>();
            UploadItem[] files = EnumerateFiles(settings.Paths[..^1], remote, settings.RemoteName,
                profile.Settings.CheckUploadNames, failures, cancellationToken);
            if (files.Length == 0)
            {
                foreach (string failure in failures)
                    Console.Error.WriteLine(failure);
                throw new InvalidOperationException("No nonempty local files were found to upload.");
            }

            var timer = Stopwatch.StartNew();
            long totalBytes = 0;
            var parallel = new ParallelOptions
            {
                MaxDegreeOfParallelism = settings.FileConcurrency ?? profile.Settings.UploadConcurrency,
                CancellationToken = cancellationToken,
            };
            await Parallel.ForEachAsync(files, parallel, async (item, token) =>
            {
                try
                {
                    var report = new TransferProgressReporter(item.Target, settings.Verbose);
                    UploadResult result = await client.UploadAsync(item.File.FullName, item.Target, checkpoints, options,
                        settings.Verbose || !Console.IsErrorRedirected
                            ? progress => report.Report(progress.CompletedBytes, progress.TotalBytes, progress.Part) : null, token);
                    if (result != UploadResult.Skipped)
                        Interlocked.Add(ref totalBytes, item.File.Length);

                    string label = result switch
                    {
                        UploadResult.RapidUpload => "Rapid upload",
                        UploadResult.Skipped => "Skipped",
                        _ => "Uploaded",
                    };
                    Console.WriteLine($"{label} {item.Target}");
                }
                catch (Exception error) when (error is IOException or InvalidDataException or HttpRequestException or BaiduApiException
                    or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                    || error is OperationCanceledException && !token.IsCancellationRequested)
                {
                    string message = error is BaiduApiException or InvalidDataException or ArgumentException or InvalidOperationException
                        ? error.Message : error.GetType().Name;
                    failures.Enqueue($"{item.Target}: {message}");
                }
            });

            Console.WriteLine($"Processed {totalBytes} bytes in {timer.Elapsed.TotalSeconds:F1}s.");
            foreach (string failure in failures)
                Console.Error.WriteLine(failure);

            return failures.IsEmpty ? 0 : 1;
        }
    }

    private static UploadItem[] EnumerateFiles(string[] paths, string destination, string? remoteName, bool checkNames,
        ConcurrentQueue<string> failures,
        CancellationToken token)
    {
        var items = new Dictionary<string, UploadItem>(StringComparer.Ordinal);
        var ancestors = new HashSet<string>(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string path in paths)
        {
            string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            FileSystemInfo entry = Directory.Exists(fullPath) ? new DirectoryInfo(fullPath) : new FileInfo(fullPath);
            if (remoteName is not null && entry is DirectoryInfo)
                throw new ArgumentException("--as requires a local file.");
            Visit(entry, RemotePath.Resolve(destination, remoteName ?? entry.Name));
        }

        return [.. items.Values];

        void Visit(FileSystemInfo entry, string target)
        {
            token.ThrowIfCancellationRequested();
            if (checkNames && RemotePath.Name(target).IndexOfAny(['<', '>', '|', ':', '"', '*', '?', '\\']) >= 0)
            {
                failures.Enqueue($"Invalid upload file name: {entry.FullName}");
                return;
            }

            try
            {
                FileSystemInfo? linked = entry.ResolveLinkTarget(returnFinalTarget: true);
                if (linked is not null && !linked.Exists)
                    return;

                FileSystemInfo source = linked ?? entry;
                if (source is DirectoryInfo directory)
                {
                    if (!ancestors.Add(directory.FullName))
                        return;

                    try
                    {
                        foreach (FileSystemInfo child in directory.EnumerateFileSystemInfos())
                            Visit(child, RemotePath.Resolve(target, child.Name));
                    }
                    finally
                    {
                        ancestors.Remove(directory.FullName);
                    }
                    return;
                }

                if (source is FileInfo file && file.Length > 0)
                {
                    if (items.TryGetValue(target, out UploadItem? existing) && existing.File.FullName != file.FullName)
                        throw new ArgumentException($"Multiple local files map to remote path {target}.");

                    items.TryAdd(target, new UploadItem(file, target));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                failures.Enqueue($"{entry.FullName}: {error.GetType().Name}");
            }
        }
    }

    private sealed record UploadItem(FileInfo File, string Target);
}
