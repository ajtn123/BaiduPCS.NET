using BaiduPCS.Netdisk;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class QuotaCommand : RemoteCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        var (_, client) = await OpenAsync(settings, cancellationToken);
        using (client)
        {
            Quota quota = await client.GetQuotaAsync(cancellationToken);
            var numbers = new DisplayNumbers(settings.RawNumbers);
            Console.WriteLine($"Total: {numbers.Bytes(quota.Total)}\nUsed: {numbers.Bytes(quota.Used)}\nFree: {numbers.Bytes(quota.Free)}");
        }

        return 0;
    }
}
