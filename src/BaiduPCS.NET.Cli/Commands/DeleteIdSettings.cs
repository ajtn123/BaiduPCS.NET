using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class DeleteIdSettings : CommonSettings
{
    [CommandArgument(0, "[id]")]
    public long[] Ids { get; init; } = [];
    [CommandOption("--all")] public bool All { get; init; }
    [CommandOption("--yes")] public bool Yes { get; init; }

    public override ValidationResult Validate() => All ? Ids.Length == 0
        ? ValidationResult.Success() : ValidationResult.Error("Choose IDs or --all.")
        : Ids.Length > 0 && Ids.All(id => id > 0)
            ? ValidationResult.Success() : ValidationResult.Error("Specify positive IDs or --all.");
}
