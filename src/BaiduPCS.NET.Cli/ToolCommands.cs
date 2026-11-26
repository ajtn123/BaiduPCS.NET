using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using BaiduPCS.Utilities;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli;

internal sealed class BeijingTimeCommand : Command<CommonSettings>
{
    public override int Execute(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        Console.WriteLine(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        return 0;
    }
}

internal sealed class IpCommand : ProfileCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        foreach (IPAddress address in NetworkInfo.LocalAddresses())
            Console.WriteLine($"Local: {address}");

        var settingsSnapshot = (await Store.ReadAsync(cancellationToken)).Settings;
        Console.WriteLine($"Public: {await NetworkInfo.GetPublicAddressAsync(settingsSnapshot, cancellationToken)}");
        return 0;
    }
}

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

internal abstract class ToolCryptoCommand(bool decrypt) : AsyncCommand<ToolCryptoSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ToolCryptoSettings settings, CancellationToken cancellationToken)
    {
        string? secret = settings.Key;
        if (settings.KeyStdin)
            secret = await Console.In.ReadLineAsync(cancellationToken);
        else if (secret is null && !Console.IsInputRedirected && !Console.IsOutputRedirected)
            secret = AnsiConsole.Prompt(new TextPrompt<string>("Encryption key:").Secret());

        if (string.IsNullOrEmpty(secret))
            throw new ArgumentException("Supply a key with --key, --key-stdin, or the masked terminal prompt.");

        byte[] key = Encoding.UTF8.GetBytes(secret);
        int status = 0;
        try
        {
            foreach (string file in settings.Files)
            {
                try
                {
                    string output = await TransformAsync(file, settings, key, cancellationToken);
                    Console.WriteLine(output);
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException)
                {
                    Console.Error.WriteLine($"{file}: {(error is InvalidDataException ? "Invalid encrypted or gzip data." : error.Message)}");
                    status = 1;
                }
            }

            return status;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private async Task<string> TransformAsync(string file, ToolCryptoSettings settings, byte[] key, CancellationToken token)
    {
        string source = Path.GetFullPath(file);
        string output = !decrypt ? source + ".encrypt" : source.EndsWith(".encrypt", StringComparison.Ordinal)
            ? source[..^".encrypt".Length] : source + ".decrypted";
        if (Path.GetFileName(output).Length == 0)
            output = source + ".decrypted";
        if (!settings.Overwrite && File.Exists(output))
            throw new IOException("The output file already exists. Use --overwrite to replace it.");

        string temporary = Path.Combine(Path.GetDirectoryName(source)!, $".baidupcs-{Guid.NewGuid():N}.tmp");
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var target = new FileStream(temporary, options))
            {
                if (decrypt)
                    await LegacyFileCrypto.DecryptAsync(input, target, settings.Method, key, !settings.NoGzip, token);
                else
                    await LegacyFileCrypto.EncryptAsync(input, target, settings.Method, key, !settings.NoGzip, token);
                target.Flush(flushToDisk: true);
            }

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, File.GetUnixFileMode(source));

            token.ThrowIfCancellationRequested();
            File.Move(temporary, output, settings.Overwrite);
            if (settings.RemoveSource)
                File.Delete(source);
            return output;
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

internal sealed class EncryptCommand() : ToolCryptoCommand(decrypt: false);
internal sealed class DecryptCommand() : ToolCryptoCommand(decrypt: true);
