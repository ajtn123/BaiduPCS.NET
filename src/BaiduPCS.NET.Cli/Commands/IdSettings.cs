using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal class IdSettings : CommonSettings
{
    [CommandArgument(0, "<id>")]
    [UiDescription("One or more positive item IDs.")]
    public long[] Ids { get; init; } = [];

    public override ValidationResult Validate() => Ids.Length > 0 && Ids.All(id => id > 0)
        ? ValidationResult.Success() : ValidationResult.Error("Specify one or more positive IDs.");
}
