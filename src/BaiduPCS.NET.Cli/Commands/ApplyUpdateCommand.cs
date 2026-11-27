using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using BaiduPCS.Utilities;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class ApplyUpdateCommand : AsyncCommand<ApplyUpdateCommand.Settings>
{
    internal const string HelperCommand = "__finish-standalone-update";
    private const string HelperName = "baidupcs-update-helper.exe";
    private const string StagingPrefix = ".baidupcs-update-";

    public sealed class Settings : CommonSettings
    {
        [CommandOption("--yes")] public bool Yes { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
#if BAIDUPCS_STANDALONE
        bool standalone = true;
#else
        bool standalone = false;
#endif
        if (!standalone)
            throw new ArgumentException("Run the standalone BaiduPCS.NET executable to install updates.");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var updater = new ReleaseUpdater(http);
        StableRelease? release = await updater.LatestAsync(cancellationToken);
        if (release is null || !ReleaseUpdater.IsNewer(release, Program.Version))
        {
            Console.WriteLine(release is null ? "No stable BaiduPCS.NET release is available." : "BaiduPCS.NET is up to date.");
            return 0;
        }

        if (!settings.Yes)
        {
            if (Console.IsInputRedirected || Console.IsOutputRedirected)
                throw new ArgumentException("Use --yes to confirm an update when running without a terminal.");
            if (!AnsiConsole.Confirm($"Update BaiduPCS.NET to {release.Tag}?", defaultValue: false))
                return 0;
        }

        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Could not identify the running application.");
#if BAIDUPCS_FRAMEWORK_DEPENDENT
        const bool frameworkDependent = true;
#else
        const bool frameworkDependent = false;
#endif
        string staged = await updater.StageStandaloneAsync(release, RuntimeInformation.RuntimeIdentifier, executable,
            cancellationToken, frameworkDependent: frameworkDependent);
        if (OperatingSystem.IsWindows())
        {
            StartWindowsHelper(staged, executable);
            Console.WriteLine($"Update staged. Exit BaiduPCS.NET to finish. Previous version: {executable}.previous");
        }
        else
        {
            string backup = ReleaseUpdater.ReplaceStandalone(executable, staged);
            Directory.Delete(Path.GetDirectoryName(staged)!);
            Console.WriteLine($"BaiduPCS.NET updated. Previous version: {backup}");
        }

        Program.RestartAfterUpdate = true;
        return 0;
    }

    private static void StartWindowsHelper(string staged, string executable)
    {
        string helper = Path.Combine(Path.GetDirectoryName(staged)!, HelperName);
        File.Copy(staged, helper);
        var info = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        using Process current = Process.GetCurrentProcess();
        foreach (string argument in new[] { HelperCommand, staged, executable,
            current.Id.ToString(CultureInfo.InvariantCulture), current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture) })
            info.ArgumentList.Add(argument);
        using Process worker = Process.Start(info) ?? throw new InvalidOperationException("Could not start the standalone update helper.");
    }

    internal static async Task<int> FinishWindowsUpdateAsync(string[] args, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 5 || !int.TryParse(args[3], out int id) || id <= 0
            || !long.TryParse(args[4], out long startTime) || Environment.ProcessPath is not { } helper)
            return 2;

        string source = Path.GetFullPath(args[1]);
        string target = Path.GetFullPath(args[2]);
        string directory = Path.GetDirectoryName(source)!;
        if (!IsStagingFolder(Path.GetFileName(directory))
            || !string.Equals(helper, Path.Combine(directory, HelperName), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetDirectoryName(directory), Path.GetDirectoryName(target), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(source), "baidupcs.exe", StringComparison.OrdinalIgnoreCase))
            return 2;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            Process? parent;
            try { parent = Process.GetProcessById(id); }
            catch (ArgumentException) { parent = null; }
            using (parent)
            {
                if (parent is not null && !parent.HasExited && parent.StartTime.ToUniversalTime().Ticks == startTime)
                    await parent.WaitForExitAsync(timeout.Token);
            }

            ReleaseUpdater.ReplaceStandalone(target, source);
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException
            or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "error.txt"), $"Standalone update failed ({error.GetType().Name}).");
            return 1;
        }
    }

    internal static void RemoveCompletedWindowsStages(string directory)
    {
        try
        {
            foreach (string stage in Directory.EnumerateDirectories(directory, StagingPrefix + "*"))
            {
                if (!IsStagingFolder(Path.GetFileName(stage)))
                    continue;

                try
                {
                    if ((File.GetAttributes(stage) & FileAttributes.ReparsePoint) != 0)
                        continue;
                    string[] contents = Directory.EnumerateFileSystemEntries(stage).Take(2).ToArray();
                    if (contents is not [var helper] || Path.GetFileName(helper) != HelperName
                        || (File.GetAttributes(helper) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                        continue;

                    File.Delete(helper);
                    Directory.Delete(stage);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // A helper may still be exiting, or another invocation may have cleaned up already.
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Startup must still work from a read-only directory.
        }
    }

    private static bool IsStagingFolder(string name) => name.StartsWith(StagingPrefix, StringComparison.Ordinal)
        && Guid.TryParseExact(name[StagingPrefix.Length..], "N", out _);
}
