using BaiduPCS.Utilities;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class ToolCryptoSettings : CommonSettings
{
    [CommandArgument(0, "<file>")]
    [UiDescription("Local files to encrypt or decrypt.")]
    public string[] Files { get; init; } = [];
    [CommandOption("--method <METHOD>")]
    [UiDescription("Choose aes-{128,192,256}-{ctr,cfb,ofb}. Defaults to aes-128-ctr.")]
    public string Method { get; init; } = "aes-128-ctr";
    [CommandOption("--key <KEY>")]
    [UiDescription("Supply the encryption or decryption key.")]
    public string? Key { get; init; }
    [CommandOption("--key-stdin")]
    [UiDescription("Read the key from standard input.")]
    public bool KeyStdin { get; init; }
    [CommandOption("--no-gzip")]
    [UiDescription("Disable gzip compression on encryption or decompression on decryption.")]
    public bool NoGzip { get; init; }
    [CommandOption("--remove-source")]
    [UiDescription("Delete each source file after writing its output successfully.")]
    public bool RemoveSource { get; init; }
    [CommandOption("--overwrite")]
    [UiDescription("Replace existing local destination files.")]
    public bool Overwrite { get; init; }

    public override ValidationResult Validate() => !LegacyFileCrypto.Supports(Method)
        ? ValidationResult.Error("Use aes-128, aes-192, or aes-256 with ctr, cfb, or ofb.")
        : Key is not null && KeyStdin ? ValidationResult.Error("Choose --key or --key-stdin.")
        : ValidationResult.Success();
}
