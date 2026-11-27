using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal class OptionalRemotePath : CommonSettings
{
    [CommandArgument(0, "[path]")]
    [UiDescription("Netdisk path. Defaults to the current folder.")]
    public string Path { get; init; } = ".";
}
