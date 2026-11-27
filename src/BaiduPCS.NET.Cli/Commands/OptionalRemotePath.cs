using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal class OptionalRemotePath : CommonSettings
{
    [CommandArgument(0, "[path]")]
    public string Path { get; init; } = ".";
}
