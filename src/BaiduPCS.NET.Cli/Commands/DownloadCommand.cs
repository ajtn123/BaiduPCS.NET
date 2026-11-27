using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using BaiduPCS.Netdisk;
using BaiduPCS.Transfers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class DownloadCommand : RemoteCommand<DownloadCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "[path]")] public string[] Paths { get; init; } = [];
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
        [CommandOption("--no-preallocate")] public bool NoPreallocate { get; init; }
        [CommandOption("--stream-discovery")] public bool StreamDiscovery { get; init; }
        [CommandOption("--resume-all")] public bool ResumeAll { get; init; }

        public override ValidationResult Validate() => Parallel is < 1 || FileConcurrency is < 1 || LinkIndex < 0 || Retries < 0
            ? ValidationResult.Error("Concurrency must be positive, and link index and retries cannot be negative.")
            : Verify && SkipVerification ? ValidationResult.Error("Choose either --verify or --skip-verification.")
            : ResumeAll && (Paths.Length != 0 || Test || StreamDiscovery || OutputDirectory is not null || Mode != DownloadMode.Direct
                || LinkIndex != 0 || Parallel.HasValue || Retries != 3 || Overwrite || Verify || SkipVerification
                || FullPath || PreserveMtime || Executable || NoPreallocate)
                ? ValidationResult.Error("--resume-all uses saved paths, destinations, and transfer options.")
            : !ResumeAll && Paths.Length == 0 ? ValidationResult.Error("Specify a download path or --resume-all.")
            : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (profile, client) = await OpenProfileAsync(settings, cancellationToken);
        using (client)
        {
            var account = profile.RequireAccount();
            var journal = new DownloadQueue(Store.DirectoryPath, account.Id);
            var display = new OperationDisplay(settings, "File", settings.WorkerStatus);
            int concurrency = settings.FileConcurrency ?? profile.Settings.DownloadConcurrency;
            var options = new DownloadOptions
            {
                Mode = settings.Mode,
                LinkIndex = settings.LinkIndex,
                Parallel = settings.Parallel ?? profile.Settings.DownloadParallel,
                Retries = settings.Retries,
                TestOnly = settings.Test,
                Overwrite = settings.Overwrite,
                Verify = settings.Verify || !settings.SkipVerification && profile.Settings.VerifyDownloads,
                PreserveModifiedTime = settings.PreserveMtime,
                Executable = settings.Executable,
                Preallocate = !settings.NoPreallocate,
            };
            IAsyncEnumerable<DownloadWork> work;
            if (settings.ResumeAll)
            {
                QueuedDownload[] pending = await journal.ReadAllAsync(cancellationToken).ToArrayAsync(cancellationToken);
                if (pending.Length == 0)
                {
                    Console.WriteLine("No pending downloads for the active account.");
                    return 0;
                }
                concurrency = Math.Min(concurrency, pending.Length);
                work = pending.OrderBy(item => item.Entry.Size).Select(item => MakeWork(item)).ToArray().ToAsyncEnumerable();
            }
            else
            {
                string[] sources = await ExpandAsync(client, account, settings.Paths, cancellationToken);
                string root = settings.OutputDirectory is { } selected ? Path.GetFullPath(selected)
                    : profile.Settings.DownloadPrefix is { } prefix ? DownloadDestination.ResolvePrefix(profile.Settings.SaveDirectory, prefix)
                    : Path.GetFullPath(Path.Combine(profile.Settings.SaveDirectory, DownloadDestination.SafeName($"{account.Id}_{account.Name}")));
                IAsyncEnumerable<DownloadItem> discovered = DiscoverAsync(client, sources, root, settings.FullPath, cancellationToken);
                if (settings.StreamDiscovery)
                    work = DiscoverWorkAsync(discovered, root, cancellationToken);
                else
                {
                    DownloadItem[] items = await discovered.ToArrayAsync(cancellationToken);
                    concurrency = Math.Min(concurrency, Math.Max(1, items.Count(item => !item.Entry.IsDirectory)));
                    DownloadWork[] queue = await DiscoverWorkAsync(items.OrderBy(item => item.Entry.Size).ToAsyncEnumerable(),
                        root, cancellationToken).ToArrayAsync(cancellationToken);
                    work = queue.ToAsyncEnumerable();
                }
            }

            var failures = new ConcurrentQueue<string>();
            var timer = Stopwatch.StartNew();
            var numbers = new DisplayNumbers(settings.RawNumbers);
            long totalBytes = 0;
            await display.RunAsync(() => Parallel.ForEachAsync(work,
                new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = cancellationToken },
                async (planned, token) =>
                {
                    QueuedDownload item = planned.Download;
                    string path = item.Entry.Path;
                    OperationDisplay.Item row = display.Begin(planned.Row);
                    try
                    {
                        if (!item.Options.TestOnly)
                            DownloadDestination.CreateSafeDirectory(item.Root, Path.GetDirectoryName(item.Target)!);
                        RemoteEntry entry = item.Entry;
                        if (settings.ResumeAll)
                        {
                            RemoteEntry? current = await client.FindAsync(path, token);
                            if (current is null || current.IsDirectory || current.Id != entry.Id
                                || current.Size != entry.Size || current.Modified != entry.Modified)
                                throw new InvalidDataException("The queued remote file is missing or has changed. Start a new download for its current contents.");
                            entry = current;
                        }

                        bool processed = await client.DownloadAsync(entry, item.Target, item.Options,
                            display.TrackProgress ? progress => display.Report(row, progress.CompletedBytes,
                                progress.TotalBytes, progress.Range, progress.Resumed) : null, token);
                        if (!item.Options.TestOnly)
                            journal.Complete(item);
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

            DownloadWork MakeWork(QueuedDownload download) =>
                new(download, display.Enqueue(download.Entry.Path, download.Entry.Size));

            async IAsyncEnumerable<DownloadWork> DiscoverWorkAsync(IAsyncEnumerable<DownloadItem> items, string root,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
            {
                DownloadOptions perFile = options with { Parallel = Math.Max(1, options.Parallel / concurrency) };
                await foreach (DownloadItem item in items.WithCancellation(token))
                {
                    if (item.Entry.IsDirectory)
                    {
                        if (!perFile.TestOnly)
                            DownloadDestination.CreateSafeDirectory(root, item.Target);
                        continue;
                    }
                    var download = new QueuedDownload(item.Entry, root, item.Target, perFile);
                    if (!perFile.TestOnly)
                        await journal.RecordAsync(download, token);
                    yield return MakeWork(download);
                }
            }
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

    private static async IAsyncEnumerable<DownloadItem> DiscoverAsync(NetdiskClient client, string[] sources,
        string outputRoot, bool fullPath, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        var targets = new Dictionary<string, DownloadItem>(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string source in sources)
        {
            RemoteEntry entry = source == "/"
                ? new(0, 0, "/", "", true, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "", [])
                : await client.FindAsync(source, token) ?? throw new InvalidOperationException($"No remote file or directory at {source}.");

            string relativeRoot = fullPath ? "/" : RemotePath.Parent(entry.Path);
            int prefixLength = relativeRoot == "/" ? 1 : relativeRoot.Length + 1;
            if (AddTarget(entry, prefixLength) is { } root)
                yield return root;
            if (entry.IsDirectory)
            {
                await foreach (RemoteEntry child in client.ListDescendantsAsync(entry.Path, token))
                {
                    if (AddTarget(child, prefixLength) is { } item)
                        yield return item;
                }
            }
        }

        DownloadItem? AddTarget(RemoteEntry entry, int prefixLength)
        {
            string target = DownloadDestination.Resolve(outputRoot, entry.Path[prefixLength..]);
            if (targets.TryGetValue(target, out DownloadItem? previous))
            {
                if (previous.Entry.Path != entry.Path)
                    throw new ArgumentException("Multiple remote paths map to the same local download destination. Use --full-path or separate output directories.");
                return null;
            }
            var item = new DownloadItem(entry, target);
            targets.Add(target, item);
            return item;
        }
    }

    private sealed record DownloadItem(RemoteEntry Entry, string Target);
    private sealed record DownloadWork(QueuedDownload Download, OperationDisplay.Item Row);
}
