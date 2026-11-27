using System.Security.Cryptography;
using System.Text;
using BaiduPCS.Utilities;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal abstract class ToolCryptoCommand(bool decrypt) : AsyncCommand<ToolCryptoSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ToolCryptoSettings settings, CancellationToken cancellationToken)
    {
        string? secret = settings.Key;
        if (settings.KeyStdin)
            secret = await Console.In.ReadLineAsync(cancellationToken);
        else if (secret is null && !Console.IsInputRedirected && !Console.IsOutputRedirected)
            secret = AnsiConsole.Prompt(new TextPrompt<string>(Ui.Text("Encryption key:")).Secret());

        if (string.IsNullOrEmpty(secret))
            throw new ArgumentException("Supply a key with --key, --key-stdin, or the masked terminal prompt.");

        byte[] key = Encoding.UTF8.GetBytes(secret);
        int status = 0;
        var display = new OperationDisplay(settings, "File");
        var queue = settings.Files.Select(file => (File: file, Row: display.Enqueue(file))).ToArray();
        try
        {
            await display.RunAsync(async () =>
            {
                foreach (var (file, queued) in queue)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = display.Begin(queued);
                    try
                    {
                        string output = await TransformAsync(file, settings, key, cancellationToken);
                        display.End(row, output, plain: output);
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException)
                    {
                        string failure = error is InvalidDataException ? "Invalid encrypted or gzip data." : error.Message;
                        display.End(row, failure, failed: true, plain: $"{file}: {failure}");
                        status = 1;
                    }
                }
            });

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
