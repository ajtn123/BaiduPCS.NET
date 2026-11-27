using System.Text.Json;
using System.Text.Json.Serialization;
using BaiduPCS.Configuration;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class ShowConfigCommand : ProfileCommand<CommonSettings>
{
    private static readonly JsonSerializerOptions Output = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<UploadConflict>(JsonNamingPolicy.KebabCaseLower),
            new JsonStringEnumConverter<NetworkFamily>(JsonNamingPolicy.KebabCaseLower) },
    };

    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        ClientSettings current = (await Store.ReadAsync(cancellationToken)).Settings;
        Console.WriteLine(JsonSerializer.Serialize(current with { Proxy = current.Proxy.Length == 0 ? "" : "[configured]" }, Output));
        return 0;
    }
}
