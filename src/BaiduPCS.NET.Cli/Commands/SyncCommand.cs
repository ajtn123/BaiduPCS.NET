using System.ComponentModel;
using BaiduPCS.Configuration;
using BaiduPCS.Netdisk;
using BaiduPCS.Transfers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class SyncCommand : RemoteCommand<SyncCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<local-folder>")] public string LocalFolder { get; init; } = "";
        [CommandArgument(1, "<remote-folder>")] public string RemoteFolder { get; init; } = "";

        [CommandOption("--mode <MODE>")]
        [Description("upload, download, transfer-missing, or remove-missing.")]
        public string? Mode { get; init; }

        [CommandOption("--direction <DIRECTION>")]
        [Description("Choose upload or download for a missing-only mode. Required for remove-missing.")]
        public string? Direction { get; init; }

        [CommandOption("--compare <FIELDS>")]
        [Description("Comma-separated fields: name,time,length,checksum. Defaults to name,time,length.")]
        public string? Compare { get; init; }

        [CommandOption("-r|--recursive")] public bool Recursive { get; init; }
        [CommandOption("--glob <PATTERN>")] public string[] Glob { get; init; } = [];
        [CommandOption("--dry-run")] public bool DryRun { get; init; }
        [CommandOption("-y|--yes")] public bool Yes { get; init; }
        [CommandOption("--download-mode <MODE>")] public DownloadMode DownloadMode { get; init; }

        internal SyncOptions GetOptions()
        {
            SyncMode mode = Mode?.ToLowerInvariant() switch
            {
                "upload" => SyncMode.Upload,
                "download" => SyncMode.Download,
                "transfer-missing" => SyncMode.TransferMissing,
                "remove-missing" => SyncMode.RemoveMissing,
                _ => throw new ArgumentException("Choose --mode upload, download, transfer-missing, or remove-missing."),
            };
            if (Direction is not null && mode is SyncMode.Upload or SyncMode.Download)
                throw new ArgumentException("--direction is available for transfer-missing and remove-missing.");

            if (Compare is not null && mode is SyncMode.TransferMissing or SyncMode.RemoveMissing)
                throw new ArgumentException("Missing-only modes compare paths. Use --compare with upload or download.");

            SyncDirection direction = Direction?.ToLowerInvariant() switch
            {
                "upload" => SyncDirection.Upload,
                "download" => SyncDirection.Download,
                null when mode == SyncMode.Upload => SyncDirection.Upload,
                null when mode == SyncMode.Download => SyncDirection.Download,
                null when mode == SyncMode.TransferMissing => SyncDirection.Both,
                _ => throw new ArgumentException("Choose --direction upload or download."),
            };
            SyncComparison compare = 0;
            foreach (string field in (Compare ?? "name,time,length").Split(',', StringSplitOptions.TrimEntries))
            {
                compare |= field.ToLowerInvariant() switch
                {
                    "name" => SyncComparison.Name,
                    "time" => SyncComparison.Time,
                    "length" => SyncComparison.Length,
                    "checksum" => SyncComparison.Checksum,
                    _ => throw new ArgumentException("--compare accepts name, time, length, and checksum."),
                };
            }
            return new(mode, direction, compare, Recursive, Glob);
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        SyncOptions options = settings.GetOptions();
        string localRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(settings.LocalFolder));
        var (profile, client) = await OpenProfileAsync(settings, cancellationToken);
        using (client)
        {
            string remoteRoot = RemotePath.Resolve(profile.RequireAccount().WorkingDirectory, settings.RemoteFolder);
            SyncPlan plan = await SyncPlan.CreateAsync(client, localRoot, remoteRoot, options, cancellationToken);
            ShowPlan(plan);
            if (settings.DryRun || plan.Changes.Count == 0)
                return 0;

            if (!settings.Yes)
            {
                if (Console.IsInputRedirected || Console.IsOutputRedirected)
                    throw new ArgumentException("Use --yes to apply a sync with redirected input or output.");
                if (!AnsiConsole.Confirm("Apply these sync changes?", defaultValue: false))
                    return 0;
            }
            return await ApplyAsync(client, profile.Settings, settings, options, plan, Path.Combine(Store.DirectoryPath, "uploads"), cancellationToken);
        }
    }

    private static void ShowPlan(SyncPlan plan)
    {
        Console.WriteLine($"Local: {plan.LocalRoot}{Environment.NewLine}Netdisk: {plan.RemoteRoot}");
        var table = new Table().AddColumns("Action", "Path", "Reason");
        foreach (SyncChange change in plan.Changes)
            table.AddRow(new Text(Label(change.Action)), new Text(change.Relative), new Text(change.Reason));
        if (plan.Changes.Count != 0)
            AnsiConsole.Write(table);
        Console.WriteLine($"{plan.Changes.Count} planned, {plan.Skipped} skipped.");
    }

    internal static async Task<int> ApplyAsync(NetdiskClient client, ClientSettings profile, Settings settings, SyncOptions options,
        SyncPlan plan, string checkpointDirectory, CancellationToken token)
    {
        var display = new OperationDisplay(settings, "Path");
        var queue = plan.Changes.Select(change => (Change: change, Row: display.Enqueue(change.Relative, change.Action switch
        {
            SyncAction.Upload => change.Local!.Length,
            SyncAction.Download => change.Remote!.Size,
            _ => null,
        }))).ToArray();
        int exitCode = 0;
        await display.RunAsync(async () =>
        {
            foreach (var (change, queued) in queue)
            {
                token.ThrowIfCancellationRequested();
                OperationDisplay.Item row = display.Begin(queued);
                try
                {
                    string result = await ApplyChangeAsync(change, row);
                    display.End(row, result, plain: $"{result}: {change.Relative}");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or HttpRequestException
                    or BaiduApiException or ArgumentException or InvalidOperationException or InvalidDataException)
                {
                    string reason = error switch
                    {
                        HttpRequestException { StatusCode: { } status } => $"Baidu returned HTTP {(int)status} ({status}).",
                        HttpRequestException request => $"Baidu request failed ({request.HttpRequestError}).",
                        _ => error.Message,
                    };
                    display.End(row, reason, failed: true, plain: $"{change.Relative}: {reason}");
                    exitCode = 1;
                    break;
                }
            }
        });
        return exitCode;

        async Task<string> ApplyChangeAsync(SyncChange change, OperationDisplay.Item row)
        {
            string local = change.Local?.Path ?? SyncPlan.LocalPath(plan.LocalRoot, change.Relative);
            string remote = change.Remote?.Path ?? SyncPlan.RemoteFilePath(plan.RemoteRoot, change.Relative);
            SyncPlan.RequireSafeParents(local);
            if (SyncPlan.ReadLocal(plan.LocalRoot) is not { IsDirectory: true } || SyncPlan.ReadLocal(local) != change.Local)
                throw new InvalidDataException("The local path changed after the sync preview.");

            if (change.Action == SyncAction.RemoveLocal)
                await RequireRemoteMissingAsync(client, plan.RemoteRoot, remote, token);
            else
                await RequireRemoteMatchAsync(client, remote, change.Remote, token);

            token.ThrowIfCancellationRequested();
            switch (change.Action)
            {
                case SyncAction.CreateLocalDirectory:
                    DownloadDestination.CreateSafeDirectory(plan.LocalRoot, local);
                    return "Created local directory";
                case SyncAction.CreateRemoteDirectory:
                    await client.CreateDirectoryAsync(remote, token);
                    return "Created remote directory";
                case SyncAction.Upload:
                    UploadResult result = await client.UploadAsync(local, remote, checkpointDirectory, new UploadOptions
                    {
                        Parallel = profile.UploadParallel,
                        Conflict = change.Remote is null ? UploadConflict.Skip : UploadConflict.Overwrite,
                        SkipRapidCheck = profile.SkipRapidCheck,
                        PreserveModifiedTime = true,
                    }, display.TrackProgress ? progress => display.Report(row, progress.CompletedBytes, progress.TotalBytes,
                        progress.Part, resumed: progress.Part < 0) : null, token);
                    return result switch
                    {
                        UploadResult.RapidUpload => "Rapid upload",
                        UploadResult.Skipped => "Skipped upload",
                        _ => "Uploaded",
                    };
                case SyncAction.Download:
                    RemoteEntry entry = change.Remote!;
                    string? md5 = options.Compare.HasFlag(SyncComparison.Checksum) && options.Mode == SyncMode.Download
                        ? await client.GetFileMd5Async(entry, token) : null;
                    bool downloaded = await client.DownloadAsync(entry with { Modified = entry.LocalModified ?? entry.Modified },
                        local, new DownloadOptions
                        {
                            Mode = settings.DownloadMode,
                            Parallel = profile.DownloadParallel,
                            Overwrite = change.Local is not null,
                            PreserveModifiedTime = true,
                            Verify = profile.VerifyDownloads,
                            ExpectedMd5 = md5,
                        }, display.TrackProgress ? progress => display.Report(row, progress.CompletedBytes, progress.TotalBytes,
                            progress.Range, progress.Resumed) : null, token);
                    return downloaded ? "Downloaded" : "Skipped download";
                case SyncAction.RemoveLocal:
                    SyncPlan.RequireSafeParents(local);
                    if (SyncPlan.ReadLocal(local) != change.Local)
                        throw new InvalidDataException("The local path changed after the sync preview.");
                    if (change.Local!.IsDirectory)
                    {
                        if (Directory.EnumerateFileSystemEntries(local).Any())
                            return "Kept nonempty directory";
                        Directory.Delete(local);
                    }
                    else
                        File.Delete(local);
                    return "Removed locally";
                case SyncAction.RemoveRemote:
                    if (change.Remote!.IsDirectory && await client.ListCompleteAsync(remote, token).AnyAsync(token))
                        return "Kept nonempty directory";
                    SyncPlan.RequireSafeParents(local);
                    if (SyncPlan.ReadLocal(plan.LocalRoot) is not { IsDirectory: true } || SyncPlan.ReadLocal(local) is not null)
                        throw new InvalidDataException("The local path appeared after the sync preview.");
                    await client.RemoveAsync(remote, token);
                    return "Removed from Netdisk";
                default:
                    throw new InvalidOperationException("Unknown sync action.");
            }
        }
    }

    private static async Task RequireRemoteMatchAsync(NetdiskClient client, string path, RemoteEntry? expected, CancellationToken token)
    {
        RemoteEntry? current = await client.FindAsync(path, token);
        if (expected is null ? current is not null : current is null || current.Id != expected.Id
            || current.IsDirectory != expected.IsDirectory || !current.IsDirectory && (current.Size != expected.Size
                || current.Modified != expected.Modified || current.LocalModified != expected.LocalModified))
            throw new InvalidDataException("The Netdisk path changed after the sync preview.");
    }

    private static async Task RequireRemoteMissingAsync(NetdiskClient client, string root, string path, CancellationToken token)
    {
        StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string parent = RemotePath.Parent(path);
        while (parent != root)
        {
            if (await client.FindAsync(parent, token) is { } found)
            {
                if (!found.IsDirectory)
                    throw new InvalidDataException("The Netdisk parent changed after the sync preview.");
                break;
            }
            path = parent;
            parent = RemotePath.Parent(path);
        }
        if (await client.ListCompleteAsync(parent, token)
            .AnyAsync(entry => entry.Path.Equals(path, comparison), token))
            throw new InvalidDataException("The Netdisk path appeared after the sync preview.");
    }

    private static string Label(SyncAction action) => action switch
    {
        SyncAction.Upload => "Upload",
        SyncAction.Download => "Download",
        SyncAction.CreateLocalDirectory => "Create local directory",
        SyncAction.CreateRemoteDirectory => "Create remote directory",
        SyncAction.RemoveLocal => "Remove locally",
        SyncAction.RemoveRemote => "Remove from Netdisk",
        _ => throw new InvalidOperationException("Unknown sync action."),
    };
}
