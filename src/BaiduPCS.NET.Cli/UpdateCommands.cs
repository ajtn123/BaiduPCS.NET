using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using BaiduPCS.Utilities;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli;

internal sealed class CheckUpdateCommand : AsyncCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        StableRelease? release = await new ReleaseUpdater(http).LatestAsync(cancellationToken);
        if (release is null)
            Console.WriteLine("No stable BaiduPCS.NET release is available.");
        else if (ReleaseUpdater.IsNewer(release, Program.Version))
            Console.WriteLine($"BaiduPCS.NET {release.Tag} is available. Run 'baidupcs update apply' to update.");
        else
            Console.WriteLine($"BaiduPCS.NET {Program.Version} is up to date.");
        return 0;
    }
}

internal sealed class ApplyUpdateCommand : AsyncCommand<ApplyUpdateCommand.Settings>
{
    internal const string HelperCommand = "__finish-standalone-update";
    private const string HelperName = "baidupcs-update-helper.exe";

    public sealed class Settings : CommonSettings
    {
        [CommandOption("--yes")] public bool Yes { get; init; }
        [CommandOption("--scope <SCOPE>")] public string? Scope { get; init; }
        [CommandOption("--tool-path <PATH>")] public string? ToolPath { get; init; }

        public override ValidationResult Validate() => Scope is not (null or "global" or "local" or "tool-path" or "standalone")
            ? ValidationResult.Error("Scope must be global, local, tool-path, or standalone.")
            : (Scope == "tool-path" && ToolPath is null) || ToolPath is not null && Scope is not (null or "tool-path")
                ? ValidationResult.Error("Use --tool-path with the tool-path scope.") : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        bool standalone = typeof(Program).Assembly.Location.Length == 0;
        string scope = settings.Scope ?? (settings.ToolPath is not null ? "tool-path" : standalone ? "standalone" : "global");
        if (scope == "standalone" && !standalone)
            throw new ArgumentException("The standalone update scope requires a single-file BaiduPCS.NET executable.");

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

        if (scope != "standalone")
        {
            int result = await UpdateToolAsync(scope, settings.ToolPath, release.Version, cancellationToken);
            if (result == 0)
                Program.RestartAfterUpdate = true;
            return result;
        }

        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Could not identify the running application.");
        string staged = await updater.StageStandaloneAsync(release, RuntimeInformation.RuntimeIdentifier, executable, cancellationToken);
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

    private static async Task<int> UpdateToolAsync(string scope, string? path, Version version, CancellationToken token)
    {
        var processInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        foreach (string arg in new[] { "tool", "update", "BaiduPCS.NET", "--version", version.ToString(3) })
            processInfo.ArgumentList.Add(arg);
        processInfo.ArgumentList.Add("--" + scope);
        if (scope == "tool-path")
            processInfo.ArgumentList.Add(Path.GetFullPath(path!));

        using Process process = Process.Start(processInfo) ?? throw new InvalidOperationException("Could not start the .NET tool updater.");
        await process.WaitForExitAsync(token);
        return process.ExitCode;
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
        string folder = Path.GetFileName(directory);
        const string prefix = ".baidupcs-update-";
        if (!folder.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(folder[prefix.Length..], "N", out _)
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
}
