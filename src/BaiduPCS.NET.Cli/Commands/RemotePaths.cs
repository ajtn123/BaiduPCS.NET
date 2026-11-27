using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal class RemotePaths : CommonSettings
{
    [CommandArgument(0, "<path>")]
    [UiDescription("Netdisk paths or wildcard patterns.")]
    public string[] Paths { get; init; } = [];
}
