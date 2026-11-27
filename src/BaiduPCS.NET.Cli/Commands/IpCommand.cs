using System.Net;
using BaiduPCS.Utilities;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

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
