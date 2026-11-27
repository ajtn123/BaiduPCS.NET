using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class PageSettings : CommonSettings
{
    [CommandOption("--page <PAGE>")]
    public int Page { get; init; } = 1;

    public override ValidationResult Validate() => Page >= 1
        ? ValidationResult.Success() : ValidationResult.Error("Page numbers start at 1.");
}
