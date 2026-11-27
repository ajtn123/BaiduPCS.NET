using BaiduPCS.Utilities;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class ToolCryptoSettings : CommonSettings
{
    [CommandArgument(0, "<file>")] public string[] Files { get; init; } = [];
    [CommandOption("--method <METHOD>")] public string Method { get; init; } = "aes-128-ctr";
    [CommandOption("--key <KEY>")] public string? Key { get; init; }
    [CommandOption("--key-stdin")] public bool KeyStdin { get; init; }
    [CommandOption("--no-gzip")] public bool NoGzip { get; init; }
    [CommandOption("--remove-source")] public bool RemoveSource { get; init; }
    [CommandOption("--overwrite")] public bool Overwrite { get; init; }

    public override ValidationResult Validate() => !LegacyFileCrypto.Supports(Method)
        ? ValidationResult.Error("Use aes-128, aes-192, or aes-256 with ctr, cfb, or ofb.")
        : Key is not null && KeyStdin ? ValidationResult.Error("Choose --key or --key-stdin.")
        : ValidationResult.Success();
}
