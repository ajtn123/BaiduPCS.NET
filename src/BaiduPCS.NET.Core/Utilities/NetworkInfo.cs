using System.Net;
using System.Net.NetworkInformation;
using BaiduPCS.Configuration;
using BaiduPCS.Netdisk;

namespace BaiduPCS.Utilities;

public static class NetworkInfo
{
    public static IReadOnlyList<IPAddress> LocalAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
        .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
        .Select(address => address.Address).Distinct().ToArray();

    public static async Task<IPAddress> GetPublicAddressAsync(ClientSettings settings, CancellationToken cancellationToken = default)
    {
        using var transport = new BaiduTransport(settings, new Credentials());
        using HttpResponseMessage response = await transport.SendAsync(HttpMethod.Get, new Uri("https://techain.baidu.com/srcmon"),
            false, cancellationToken, configure: request =>
            {
                request.Headers.Remove("User-Agent");
                request.Headers.TryAddWithoutValidation("User-Agent", "x18/600000101/10.0.63/4.1.3");
                request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
                request.Headers.TryAddWithoutValidation("Accept", "*/*");
                request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN");
                request.Headers.TryAddWithoutValidation("x-auth-ver", "1");
                request.Headers.TryAddWithoutValidation("x-device-id", new string('0', 32));
            });
        response.EnsureSuccessStatusCode();
        string result = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        return IPAddress.TryParse(result, out IPAddress? address)
            ? address : throw new InvalidDataException("Baidu did not return a public IP address.");
    }
}
