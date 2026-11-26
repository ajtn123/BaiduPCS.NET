using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BaiduPCS.Netdisk;

public sealed record PanDownloadLink(long FileId, Uri Url);
public sealed record DirectoryDiff(string Cursor, bool HasMore, JsonElement Entries);

public sealed partial class NetdiskClient
{
    private const string DownloadSignatureSalt = "ebrcUYiuxaZv2XGu7KIYKxUrqfnOfpDF";

    public async Task<IReadOnlyList<Uri>> LocateDownloadAsync(string path, CancellationToken cancellationToken = default)
    {
        Uri uri = _transport.Pcs("file", "locatedownload", [("path", path), ("app_id", BaiduTransport.PanAppId),
            ("ant", "1"), ("check_blue", "1"), ("es", "1"), ("esl", "1"), ("ver", "4.0"), ("clienttype", "17"),
            ("channel", "0"), ("apn_id", "1_0"), ("freeisp", "0"), ("queryfree", "0"), ("use", "0"), .. SignedParameters()]);
        JsonElement root = await _transport.JsonAsync(HttpMethod.Post, uri, "direct download links", true, cancellationToken);
        var links = new List<Uri>();
        foreach (JsonElement item in root.Items("urls"))
        {
            if (item.Number("encrypt") == 0 && ParseHttpUri(item.Text("url")) is { } address)
                links.Add(new UriBuilder(address) { Scheme = _settings.UseHttps ? "https" : "http", Port = address.IsDefaultPort ? -1 : address.Port }.Uri);
        }

        if (links.Count == 0)
            throw new InvalidDataException("Baidu returned no usable direct download links.");

        return links;
    }

    public async Task<IReadOnlyList<PanDownloadLink>> LocatePanDownloadAsync(IEnumerable<long> ids,
        CancellationToken cancellationToken = default)
    {
        long[] fileIds = ids.Distinct().ToArray();
        if (fileIds.Length == 0)
            return [];

        for (int attempt = 0; ; attempt++)
        {
            (string signature, string timestamp) = await GetPanSignatureAsync(cancellationToken);
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["sign"] = signature,
                ["timestamp"] = timestamp,
                ["fidlist"] = JsonSerializer.Serialize(fileIds),
            });
            try
            {
                JsonElement root = await _transport.JsonAsync(HttpMethod.Post, _transport.Pan("download"),
                    "Pan download links", true, cancellationToken, form);
                var links = new List<PanDownloadLink>();
                foreach (JsonElement item in root.Items("dlink"))
                {
                    if (ParseHttpUri(item.Text("dlink")) is { } address)
                        links.Add(new PanDownloadLink(item.Number("fs_id"), address));
                }

                return links;
            }
            catch (BaiduApiException error) when (attempt == 0 && error.Code is 112 or 113)
            {
                // The Pan home signature may expire between the two requests.
            }
        }
    }

    public async Task<DirectoryDiff> GetDirectoryDiffAsync(string? cursor = null, CancellationToken cancellationToken = default)
    {
        Uri uri = _transport.Pan("batch/filediff", [("cursor", string.IsNullOrEmpty(cursor) ? "null" : cursor),
            ("clienttype", "1"), .. SignedParameters()]);
        JsonElement root = await _transport.JsonAsync(HttpMethod.Get, uri, "directory diff", false, cancellationToken);
        JsonElement entries = root.Field("entries");
        if (entries.ValueKind == JsonValueKind.Undefined)
            entries = root.Field("list");

        return new DirectoryDiff(root.Text("cursor"), root.Number("has_more") != 0 || root.Field("has_more").ValueKind == JsonValueKind.True,
            entries.ValueKind == JsonValueKind.Undefined ? JsonSerializer.SerializeToElement(Array.Empty<object>()) : entries);
    }

    internal Uri DownloadEndpoint(string path, bool streaming) =>
        _transport.Pcs(streaming ? "stream" : "file", "download", ("path", path));

    private (string Key, string Value)[] SignedParameters()
    {
        string bduss = _account.Credentials.Bduss;
        if (bduss.Length == 0)
            BaiduTransport.ParseCookies(_account.Credentials.Cookies).TryGetValue("BDUSS", out bduss!);
        if (string.IsNullOrEmpty(bduss))
            throw new InvalidOperationException("The account requires a BDUSS session for this operation.");

        string timestamp = BaiduTransport.Format(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        string device = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(bduss))) + "|0";
        return [("time", timestamp), ("rand", DownloadSignature(_account.Id, bduss, timestamp, device)),
            ("devuid", device), ("cuid", device)];
    }

    internal static string DownloadSignature(ulong accountId, string session, string timestamp, string device)
    {
        string sessionHash = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(session)));
        string payload = string.Concat(sessionHash, accountId.ToString(CultureInfo.InvariantCulture), DownloadSignatureSalt, timestamp, device);
        return Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private async Task<(string Signature, string Timestamp)> GetPanSignatureAsync(CancellationToken token)
    {
        using HttpResponseMessage response = await _transport.SendAsync(HttpMethod.Get, _transport.PanPage("/disk/home"), true, token,
            configure: request =>
            {
                request.Headers.Remove("User-Agent");
                request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            });
        if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri is { Host: not BaiduTransport.PanHost })
            throw new BaiduApiException("Pan home", 0, response.StatusCode);

        string page = await response.Content.ReadAsStringAsync(token);
        string sign1 = PanValue("sign1", page);
        string sign3 = PanValue("sign3", page);
        Match timestamp = Regex.Match(page, "\"timestamp\"\\s*:\\s*\"?(?<seconds>[0-9]+)", RegexOptions.NonBacktracking);
        if (sign1.Length == 0 || sign3.Length == 0 || !timestamp.Success)
            throw new InvalidDataException("Baidu Pan did not return download signature data. Check the session cookies.");

        return (Convert.ToBase64String(PanSignature(sign3, sign1)), timestamp.Groups["seconds"].Value);
    }

    private static string PanValue(string key, string page)
    {
        Match match = Regex.Match(page, $"\"{key}\"\\s*:\\s*\"(?<value>[^\"]*)\"", RegexOptions.NonBacktracking);
        return match.Groups["value"].Value;
    }

    internal static byte[] PanSignature(string key, string input)
    {
        if (key.Length == 0)
            throw new ArgumentException("The Pan signature key is empty.", nameof(key));

        Span<byte> state = stackalloc byte[256];
        for (int index = 0; index < state.Length; index++)
            state[index] = (byte)index;

        int cursor = 0;
        for (int index = 0; index < state.Length; index++)
        {
            cursor = (cursor + state[index] + key[index % key.Length]) & 255;
            (state[index], state[cursor]) = (state[cursor], state[index]);
        }

        byte[] output = new byte[input.Length];
        cursor = 0;
        for (int index = 0; index < input.Length; index++)
        {
            int left = (index + 1) & 255;
            cursor = (cursor + state[left]) & 255;
            (state[left], state[cursor]) = (state[cursor], state[left]);
            output[index] = (byte)(input[index] ^ state[(state[left] + state[cursor]) & 255]);
        }

        return output;
    }

    private static Uri? ParseHttpUri(string value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 ? uri : null;
}
