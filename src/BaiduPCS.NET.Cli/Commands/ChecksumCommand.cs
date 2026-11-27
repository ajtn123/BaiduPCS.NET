using BaiduPCS.Transfers;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class ChecksumCommand : AsyncCommand<ChecksumCommand.Settings>
{
    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<file>")]
        public string[] Files { get; init; } = [];
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        int exitCode = 0;
        var display = new OperationDisplay(settings, "File");
        var numbers = new DisplayNumbers(settings.RawNumbers);
        var queue = settings.Files.Select(path => (Path: path, Row: display.Enqueue(path))).ToArray();
        await display.RunAsync(async () =>
        {
            foreach (var (path, queued) in queue)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = display.Begin(queued);
                try
                {
                    await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                        128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    FileChecksums checksum = await FileChecksums.ComputeAsync(file, cancellationToken);
                    string name = Path.GetFileName(path).Replace(' ', '_').Replace('#', '_');
                    string result = $"Length: {numbers.Bytes(checksum.Length, unit: false)}\nMD5: {checksum.Md5}\n"
                        + $"First 256 KiB MD5: {checksum.SliceMd5}\nCRC32: {checksum.Crc32}\n{checksum.FormatLink(name)}";
                    display.End(row, result, plain: $"{path}\n{result}");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    string failure = $"Cannot read {path} ({error.GetType().Name}).";
                    display.End(row, failure, failed: true, plain: failure);
                    exitCode = 1;
                }
            }
        });

        return exitCode;
    }
}
