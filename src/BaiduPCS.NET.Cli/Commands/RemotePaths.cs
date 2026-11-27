using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal class RemotePaths : CommonSettings
{
    [CommandArgument(0, "<path>")]
    public string[] Paths { get; init; } = [];
}
