using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BaiduPCS.Configuration;

namespace BaiduPCS.Netdisk;

public static class AccountIdentity
{
    public static Task<Account> ResolveAsync(ClientSettings settings, Credentials credentials, ulong? manualId = null,
        CancellationToken cancellationToken = default) => ResolveAsync(settings, credentials, manualId, null, cancellationToken);

    internal static async Task<Account> ResolveAsync(ClientSettings settings, Credentials credentials, ulong? manualId,
        HttpMessageHandler? handler, CancellationToken token)
    {
        string bduss = credentials.Bduss;
        if (bduss.Length == 0)
            BaiduTransport.ParseCookies(credentials.Cookies).TryGetValue("BDUSS", out bduss!);
        if (string.IsNullOrWhiteSpace(bduss))
            throw new ArgumentException("Supply a BDUSS token or cookies containing BDUSS.");

        using var transport = new BaiduTransport(settings, credentials, handler, allowRedirects: false);
        string manualName = settings.ManualAccountName.Trim();
        Exception? lookupError = null;
        try
        {
            return await ReadTiebaIdentityAsync(transport, credentials, bduss, token);
        }
        catch (Exception error) when (IsLookupFailure(error))
        {
            lookupError = error;
        }

        try
        {
            Uri uri = BaiduTransport.Build("https", BaiduTransport.PanHost, "/api/user/getinfo", ("need_selfinfo", "1"));
            JsonElement pan = await transport.JsonAsync(HttpMethod.Get, uri, "account identity", false, token);
            JsonElement entry = pan.Items("records").FirstOrDefault();
            string name = Value(entry, "baidu_name", "username", "name", "netdisk_name");
            if (TryReadId(entry, out ulong id, "uid", "user_id", "id") && (name.Length != 0 || manualName.Length != 0))
                return new Account { Id = id, Name = name.Length == 0 ? manualName : name, Credentials = credentials };

            if (entry.ValueKind == JsonValueKind.Object && manualName.Length > 0 && manualId.GetValueOrDefault() > 0)
                return new Account { Id = manualId!.Value, Name = manualName, Credentials = credentials };

            if (manualName.Length > 0 && TryReadId(entry, out id, "uk"))
                return new Account { Id = id, Name = manualName, Credentials = credentials };
        }
        catch (Exception error) when (IsLookupFailure(error))
        {
            lookupError = error;
        }

        if (manualName.Length != 0)
        {
            await transport.JsonAsync(HttpMethod.Get, transport.Pcs("quota", "info"), "session validation", false, token);
            return new Account { Id = manualId is > 0 ? manualId.Value : 24, Name = manualName, Credentials = credentials };
        }

        if (lookupError is BaiduApiException apiError)
            throw apiError;
        throw new InvalidDataException("Baidu did not return an account identity. Check the session or configure a manual account name.");
    }

    private static async Task<Account> ReadTiebaIdentityAsync(BaiduTransport transport, Credentials credentials, string bduss,
        CancellationToken token)
    {
        const string TiebaLoginHost = "tieba.baidu.com";
        const string TiebaProfileHost = "c.tieba.baidu.com";
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["bdusstoken"] = bduss + "|null",
            ["channel_id"] = "",
            ["channel_uid"] = "",
            ["stErrorNums"] = "0",
            ["subapp_type"] = "mini",
            ["timestamp"] = timestamp,
            ["_client_type"] = "2",
            ["_client_version"] = "7.0.0.0",
            ["from"] = "mini_ad_wandoujia",
        };
        fields["sign"] = TiebaDigest(string.Concat(fields.Select(field => field.Key + "=" + field.Value)));
        using var form = new FormUrlEncodedContent(fields);
        JsonElement response = await transport.JsonAsync(HttpMethod.Post, BaiduTransport.Build("https", TiebaLoginHost, "/c/s/login"),
            "Tieba account identity", false, token, form, configure: request =>
            {
                request.Headers.Remove("User-Agent");
                request.Headers.TryAddWithoutValidation("User-Agent", "bdtb for Android 6.9.2.1");
                request.Headers.TryAddWithoutValidation("net", "1");
                request.Headers.TryAddWithoutValidation("client_logid", timestamp);
            });
        JsonElement user = response.Field("user");
        string name = Value(user, "name", "name_show");
        if (!TryReadId(user, out ulong id, "id", "uid") || name.Length == 0)
            throw new InvalidDataException("Tieba did not identify the signed-in account.");

        string[] queryValues = ["has_plist=0", "need_post_count=1", "rn=1", "uid=" + id.ToString(CultureInfo.InvariantCulture)];
        Uri profileUri = new UriBuilder("https", TiebaProfileHost) { Path = "/c/u/user/profile",
            Query = string.Join('&', queryValues) + "&sign=" + TiebaDigest(string.Concat(queryValues)) }.Uri;
        try
        {
            JsonElement profile = await transport.JsonAsync(HttpMethod.Get, profileUri, "Tieba account profile", false, token);
            JsonElement details = profile.Field("user");
            var age = double.TryParse(details.Text("tb_age"), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : 0;
            string sex = details.Number("sex") switch { 1 => "♂", 2 => "♀", _ => "" };
            string fullName = Value(details, "name", "name_show");
            return new Account { Id = id, Name = fullName.Length == 0 ? name : fullName, Sex = sex, Age = age, Credentials = credentials };
        }
        catch (Exception error) when (IsLookupFailure(error))
        {
            return new Account { Id = id, Name = name, Credentials = credentials };
        }
    }

    private static string TiebaDigest(string fields) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(fields + "tiebaclient!!!")));

    private static bool TryReadId(JsonElement entry, out ulong result, params string[] fields)
    {
        foreach (string field in fields)
        {
            if (ulong.TryParse(entry.Text(field), NumberStyles.None, CultureInfo.InvariantCulture, out result) && result != 0)
                return true;
        }

        result = 0;
        return false;
    }

    internal static string Value(JsonElement entry, params string[] names)
    {
        foreach (string name in names)
        {
            string value = entry.Text(name);
            if (value.Length != 0)
                return value;
        }

        return "";
    }

    private static bool IsLookupFailure(Exception error) => error is BaiduApiException or HttpRequestException or InvalidDataException;
}
