using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using BaiduPCS.Netdisk;
using BaiduPCS.Transfers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli;

internal sealed class DownloadCommand : RemoteCommand<DownloadCommand.Settings>
{
    public sealed class Settings : RemotePaths
    {
        [CommandOption("--mode <MODE>")] public DownloadMode Mode { get; init; }
        [CommandOption("--link-index <INDEX>")] public int LinkIndex { get; init; }
        [CommandOption("--test")] public bool Test { get; init; }
        [CommandOption("--overwrite")] public bool Overwrite { get; init; }
        [CommandOption("--worker-status")] public bool WorkerStatus { get; init; }
        [CommandOption("--output-directory <PATH>")] public string? OutputDirectory { get; init; }
        [CommandOption("--full-path")] public bool FullPath { get; init; }
        [CommandOption("--executable")] public bool Executable { get; init; }
        [CommandOption("--parallel <WORKERS>")] public int? Parallel { get; init; }
        [CommandOption("--file-concurrency <FILES>")] public int? FileConcurrency { get; init; }
        [CommandOption("--retries <COUNT>")] public int Retries { get; init; } = 3;
        [CommandOption("--verify")] public bool Verify { get; init; }
        [CommandOption("--skip-verification")] public bool SkipVerification { get; init; }
        [CommandOption("--preserve-mtime")] public bool PreserveMtime { get; init; }

        public override ValidationResult Validate() => Parallel is < 1 || FileConcurrency is < 1 || LinkIndex < 0 || Retries < 0
            ? ValidationResult.Error("Concurrency must be positive, and link index and retries cannot be negative.")
            : Verify && SkipVerification ? ValidationResult.Error("Choose either --verify or --skip-verification.") : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (profile, client) = await OpenProfileAsync(settings, cancellationToken);
        using (client)
        {
            var account = profile.RequireAccount();
            string[] sources = await ExpandAsync(client, account, settings.Paths, cancellationToken);
            string outputRoot = settings.OutputDirectory is { } selected ? Path.GetFullPath(selected)
                : profile.Settings.DownloadPrefix is { } prefix ? DownloadDestination.ResolvePrefix(profile.Settings.SaveDirectory, prefix)
                : Path.GetFullPath(Path.Combine(profile.Settings.SaveDirectory, DownloadDestination.SafeName($"{account.Id}_{account.Name}")));
            IReadOnlyCollection<DownloadItem> items = await PlanDownloadsAsync(client, sources, outputRoot, settings.FullPath, cancellationToken);
            if (!settings.Test)
            {
                foreach (DownloadItem directory in items.Where(item => item.Entry.IsDirectory))
                    DownloadDestination.CreateSafeDirectory(outputRoot, directory.Target);
            }

            DownloadItem[] files = items.Where(item => !item.Entry.IsDirectory).OrderBy(item => item.Entry.Size).ToArray();
            int concurrency = Math.Clamp(settings.FileConcurrency ?? profile.Settings.DownloadConcurrency, 1, Math.Max(1, files.Length));
            var options = new DownloadOptions
            {
                Mode = settings.Mode,
                LinkIndex = settings.LinkIndex,
                Parallel = Math.Max(1, (settings.Parallel ?? profile.Settings.DownloadParallel) / concurrency),
                Retries = settings.Retries,
                TestOnly = settings.Test,
                Overwrite = settings.Overwrite,
                Verify = settings.Verify || !settings.SkipVerification && profile.Settings.VerifyDownloads,
                PreserveModifiedTime = settings.PreserveMtime,
                Executable = settings.Executable,
            };
            var failures = new ConcurrentQueue<string>();
            var timer = Stopwatch.StartNew();
            var display = new OperationDisplay(settings, "File", settings.WorkerStatus);
            var numbers = new DisplayNumbers(settings.RawNumbers);
            long totalBytes = 0;
            await display.RunAsync(() => Parallel.ForEachAsync(files,
                new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = cancellationToken },
                async (item, token) =>
                {
                    string path = item.Entry.Path;
                    OperationDisplay.Item row = display.Begin(path, item.Entry.Size);
                    try
                    {
                        if (!settings.Test)
                            DownloadDestination.CreateSafeDirectory(outputRoot, Path.GetDirectoryName(item.Target)!);

                        bool processed = await client.DownloadAsync(item.Entry, item.Target, options,
                            display.TrackProgress ? progress => display.Report(row, progress.CompletedBytes,
                                progress.TotalBytes, progress.Range, progress.Resumed) : null, token);
                        if (processed)
                            Interlocked.Add(ref totalBytes, item.Entry.Size);

                        string result = processed ? settings.Test ? "Tested" : "Downloaded" : "Skipped";
                        display.End(row, result, plain: $"{result} {path}");
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or HttpRequestException or BaiduApiException
                        or UnauthorizedAccessException or InvalidOperationException || error is OperationCanceledException && !token.IsCancellationRequested)
                    {
                        failures.Enqueue(path);
                        string failure = DescribeFailure(error);
                        display.End(row, failure, failed: true, plain: $"{path}: {failure}");
                    }
                }));

            Console.WriteLine($"Processed {numbers.Bytes(totalBytes)} in {numbers.Duration(timer.Elapsed.TotalSeconds)}.");
            if (!failures.IsEmpty && !display.IsInteractive)
                Console.Error.WriteLine($"Failed downloads ({failures.Count}): {string.Join(", ", failures)}");

            return failures.IsEmpty ? 0 : 1;
        }
    }

    internal static string DescribeFailure(Exception error) => error switch
    {
        BaiduApiException { Status: HttpStatusCode.Forbidden, Code: not 0 } denied =>
            $"Download request returned HTTP 403 (Forbidden), Baidu error {denied.Code}.",
        HttpRequestException { StatusCode: { } status } => $"Download request returned HTTP {(int)status} ({status}).",
        HttpRequestException request => $"Download request failed ({request.HttpRequestError}).",
        OperationCanceledException => "The download request timed out.",
        BaiduApiException or InvalidDataException or InvalidOperationException or EndOfStreamException => error.Message,
        _ => error.GetType().Name,
    };

    private static async Task<IReadOnlyCollection<DownloadItem>> PlanDownloadsAsync(NetdiskClient client, string[] sources,
        string outputRoot, bool fullPath, CancellationToken token)
    {
        var targets = new Dictionary<string, DownloadItem>(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string source in sources)
        {
            RemoteEntry entry = source == "/"
                ? new(0, 0, "/", "", true, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "", [])
                : await client.FindAsync(source, token) ?? throw new InvalidOperationException($"No remote file or directory at {source}.");

            var queue = new Queue<RemoteEntry>();
            queue.Enqueue(entry);
            string relativeRoot = fullPath ? "/" : RemotePath.Parent(entry.Path);
            int prefixLength = relativeRoot == "/" ? 1 : relativeRoot.Length + 1;
            while (queue.TryDequeue(out RemoteEntry? current))
            {
                string target = DownloadDestination.Resolve(outputRoot, current.Path[prefixLength..]);
                if (targets.TryGetValue(target, out DownloadItem? previous) && previous.Entry.Path != current.Path)
                    throw new ArgumentException("Multiple remote paths map to the same local download destination. Use --full-path or separate output directories.");

                if (!targets.TryAdd(target, new DownloadItem(current, target)))
                    continue;
                if (!current.IsDirectory)
                    continue;

                await foreach (RemoteEntry child in client.ListAsync(current.Path, cancellationToken: token))
                {
                    if (child.Path == current.Path || RemotePath.Parent(child.Path) != current.Path)
                        throw new InvalidDataException("Baidu returned a file outside the requested directory.");

                    queue.Enqueue(child);
                }
            }
        }

        return targets.Values;
    }

    private sealed record DownloadItem(RemoteEntry Entry, string Target);
}
