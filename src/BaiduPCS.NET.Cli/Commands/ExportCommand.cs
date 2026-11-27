using System.Globalization;
using System.Text.Json;
using BaiduPCS.Configuration;
using BaiduPCS.Netdisk;
using BaiduPCS.Transfers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class ExportCommand : RemoteCommand<ExportCommand.Settings>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "[path]")] public string[] Paths { get; init; } = [];
        [CommandOption("--root <PATH>")] public string? Root { get; init; }
        [CommandOption("--out <FILE>")] public string? Output { get; init; }
        [CommandOption("--stdout")] public bool StandardOutput { get; init; }
        [CommandOption("--recursive")] public bool Recursive { get; init; }
        [CommandOption("--format <FORMAT>")] public string Format { get; init; } = "manifest";
        [CommandOption("--retries <COUNT>")] public int Retries { get; init; } = 3;

        public override ValidationResult Validate() => Format is not ("manifest" or "checksum-link")
            ? ValidationResult.Error("Format must be manifest or checksum-link.")
            : Retries < 0 ? ValidationResult.Error("Retry count cannot be negative.")
            : StandardOutput && Output is not null ? ValidationResult.Error("Choose --stdout or --out.")
            : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (account, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            string[] sources = await ExpandAsync(client, account, settings.Paths.Length == 0 ? ["."] : settings.Paths, cancellationToken);
            if (settings.StandardOutput)
                return await ExportAsync(client, account, sources, settings, Console.Out, null, cancellationToken);

            DateTimeOffset time = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
            string output = settings.Output ?? "BaiduPCS.NET_export_" + time.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)
                + (settings.Format == "manifest" ? ".jsonl" : ".txt");
            await using var stream = new StreamWriter(output, append: true);
            var display = new OperationDisplay(settings, "File");
            int status = 0;
            await display.RunAsync(async () =>
            {
                status = await ExportAsync(client, account, sources, settings, stream, display, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            });
            Console.Error.WriteLine($"Export written to {Path.GetFullPath(output)}");
            return status;
        }
    }

    private static async Task<int> ExportAsync(NetdiskClient client, Account account, string[] sources, Settings settings,
        TextWriter output, OperationDisplay? display, CancellationToken token)
    {
        var written = new Dictionary<string, string>(StringComparer.Ordinal);
        int failures = 0;
        foreach (string source in sources)
        {
            if (source == "/")
            {
                await VisitDirectoryAsync(source, source);
                continue;
            }

            RemoteEntry? entry;
            try
            {
                entry = await client.FindAsync(source, token) ?? throw new InvalidDataException("Path was not found.");
            }
            catch (Exception error) when (IsRemoteFailure(error))
            {
                ReportFailure(source, error);
                continue;
            }

            string prefix = source == account.WorkingDirectory ? source : RemotePath.Parent(source);
            if (entry.IsDirectory)
                await VisitDirectoryAsync(source, prefix);
            else
                await WriteFileAsync(entry, prefix);
        }

        return failures == 0 ? 0 : 1;

        async Task VisitDirectoryAsync(string path, string prefix)
        {
            var pending = new Queue<string>();
            pending.Enqueue(path);
            while (pending.TryDequeue(out string? directory))
            {
                RemoteEntry[] children;
                try
                {
                    children = await client.ListAsync(directory, cancellationToken: token).ToArrayAsync(token);
                    if (children.Any(child => child.Path == directory || RemotePath.Parent(child.Path) != directory))
                        throw new InvalidDataException("Baidu returned an entry outside the requested directory.");
                }
                catch (Exception error) when (IsRemoteFailure(error))
                {
                    ReportFailure(directory, error);
                    continue;
                }

                if (children.Length == 0 && settings.Format == "manifest")
                {
                    string directoryPath = Remap(directory, prefix, settings.Root);
                    if (CanWrite(directory, directoryPath))
                    {
                        await output.WriteLineAsync(JsonSerializer.Serialize(new { type = "directory", path = directoryPath }, Json).AsMemory(), token);
                        written.Add(directoryPath, directory);
                    }
                }

                foreach (RemoteEntry child in children)
                {
                    if (!child.IsDirectory)
                        await WriteFileAsync(child, prefix);
                    else if (settings.Recursive)
                        pending.Enqueue(child.Path);
                }
            }
        }

        async Task WriteFileAsync(RemoteEntry entry, string prefix)
        {
            string path = Remap(entry.Path, prefix, settings.Root);
            if (!CanWrite(entry.Path, path))
                return;

            FileChecksums checksums;
            OperationDisplay.Item? row = display?.Begin(entry.Path);
            try
            {
                checksums = await client.GetExportChecksumsAsync(entry, settings.Retries, token);
            }
            catch (Exception error) when (IsRemoteFailure(error) || error is ArgumentException)
            {
                ReportFailure(entry.Path, error, row);
                return;
            }

            string record = settings.Format == "checksum-link" ? checksums.FormatLink(RemotePath.Name(entry.Path))
                : JsonSerializer.Serialize(new { type = "file", path, checksums.Length, checksums.Md5, checksums.SliceMd5, checksums.Crc32 }, Json);
            await output.WriteLineAsync(record.AsMemory(), token);
            written.Add(path, entry.Path);
            if (row is not null)
                display!.End(row, "Exported");
        }

        bool CanWrite(string source, string path)
        {
            if (!written.TryGetValue(path, out string? previous))
                return true;
            if (source != previous)
                ReportFailure(source, new InvalidDataException($"Both {source} and {previous} map to {path}."));
            return false;
        }

        void ReportFailure(string path, Exception error, OperationDisplay.Item? row = null)
        {
            failures++;
            string message = error is HttpRequestException ? "Baidu request failed." : error.Message;
            string plain = $"Export {path}: {message}";
            if (display is null)
                Console.Error.WriteLine(plain);
            else
                display.End(row ?? display.Begin(path), message, failed: true, plain: plain);
        }

        bool IsRemoteFailure(Exception error) => error is BaiduApiException or HttpRequestException or InvalidDataException or EndOfStreamException
            || error is OperationCanceledException && !token.IsCancellationRequested;
    }

    private static string Remap(string path, string prefix, string? root)
    {
        if (string.IsNullOrEmpty(root))
            return path;

        string relative = path[prefix.Length..].TrimStart('/');
        string result = RemotePath.Resolve("/", root.TrimEnd('/') + "/" + relative);
        return RemotePath.IsAbsolute(root) ? result : result.TrimStart('/');
    }
}
