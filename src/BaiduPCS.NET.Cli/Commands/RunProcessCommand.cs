using System.Diagnostics;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class RunProcessCommand : AsyncCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        string[] arguments = [.. context.Remaining.Raw];
        if (arguments.Length == 0)
            throw new ArgumentException("Use 'run -- EXECUTABLE ARG...' to run a local process.");

        var start = new ProcessStartInfo(arguments[0]) { UseShellExecute = false };
        foreach (string argument in arguments.Skip(1))
            start.ArgumentList.Add(argument);

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the requested program.");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
