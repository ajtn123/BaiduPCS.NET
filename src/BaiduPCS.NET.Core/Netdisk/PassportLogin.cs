using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BaiduPCS.Configuration;

namespace BaiduPCS.Netdisk;

public enum VerificationChannel { Mobile, Email }

public interface IPassportPrompts
{
    ValueTask<string> RequestImageCodeAsync(ReadOnlyMemory<byte> image, CancellationToken cancellationToken);
    ValueTask<VerificationChannel> ChooseChannelAsync(string phone, string email, CancellationToken cancellationToken);
    ValueTask<string> RequestVerificationCodeAsync(VerificationChannel channel, bool retry, CancellationToken cancellationToken);
}

public sealed class PassportLogin : IDisposable
{
    private static readonly Uri Home = new("https://wappass.baidu.com/");
    private static readonly Uri PanHome = new("https://pan.baidu.com/");
    private static readonly Uri Passport = new("https://passport.baidu.com/");
    private const string LegacyScript = "/static/touch/js/login_d9bffc9.js";
    private readonly BaiduTransport _transport;

    public PassportLogin(ClientSettings settings) : this(settings, null) { }

    internal PassportLogin(ClientSettings settings, HttpMessageHandler? handler) =>
        _transport = new BaiduTransport(settings, new Credentials(), handler, allowRedirects: false);

    public async Task<Credentials> SignInAsync(string username, string password, IPassportPrompts prompts,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            throw new ArgumentException("Username and password are required.");

        Bootstrap bootstrap = await StartSessionAsync(cancellationToken);
        string imageId = "";
        string imageCode = "";
        for (int attempt = 0; attempt < 10; attempt++)
        {
            JsonElement result = await SubmitPasswordAsync(username, password, bootstrap, imageId, imageCode, cancellationToken);
            JsonElement data = result.Field("data");
            switch (ErrorCode(result))
            {
                case "0":
                    return RequireCredentials(CollectCredentials(data, ""));
                case "500001" or "500002":
                    imageId = data.Text("codeString");
                    if (imageId.Length == 0 || imageId.Length > 4096 || imageId.Any(char.IsControl))
                        throw new InvalidDataException("Baidu requested image verification without a valid challenge.");

                    using (HttpResponseMessage picture = await FollowGetAsync(new Uri(Home, "/cgi-bin/genimage?" + imageId),
                        Home, cancellationToken))
                    {
                        byte[] png = await ReadCaptchaAsync(picture, cancellationToken);
                        imageCode = await prompts.RequestImageCodeAsync(png, cancellationToken);
                    }

                    if (string.IsNullOrWhiteSpace(imageCode))
                        throw new InvalidOperationException("An image verification code is required.");
                    break;
                case "400023" or "400101":
                    Credentials? verified = await VerifyAccountAsync(data, prompts, cancellationToken);
                    if (verified is not null)
                        return verified;

                    imageId = "";
                    imageCode = "";
                    break;
                default:
                    throw new InvalidOperationException($"Baidu declined password sign-in (code {SafeErrorCode(result)}).");
            }
        }

        throw new InvalidOperationException("Baidu did not complete sign-in within the allowed challenge attempts.");
    }

    private async Task<Bootstrap> StartSessionAsync(CancellationToken token)
    {
        using HttpResponseMessage home = await FollowGetAsync(Home, Home, token);
        string html = await home.Content.ReadAsStringAsync(token);
        string trace = home.Headers.TryGetValues("Trace-Id", out var traces) ? traces.FirstOrDefault() ?? "" : "";

        using HttpResponseMessage replay = await FollowGetAsync(new Uri(Home, "/wp/api/security/antireplaytoken"), Home, token);
        JsonElement replayData = ParseObject(await replay.Content.ReadAsStringAsync(token));
        JsonElement nested = replayData.Field("data");
        string time = AccountIdentity.Value(nested, "time", "servertime");
        if (time.Length == 0)
            time = AccountIdentity.Value(replayData, "time", "servertime");
        if (time.Length == 0)
            throw new InvalidDataException("Baidu did not supply a password sign-in timestamp.");

        string modulus = AccountIdentity.Value(nested, "rsa", "modulus");
        if (modulus.Length == 0)
            modulus = ReadModulus(html);
        if (modulus.Length == 0)
        {
            string path = Regex.Match(html, @"(?:src|data-src)\s*=\s*[""'](?<path>/static/touch/[^""']*login[_-][a-zA-Z0-9_-]+\.js)[""']",
                RegexOptions.NonBacktracking).Groups["path"].Value;
            using HttpResponseMessage script = await FollowGetAsync(new Uri(Home, path.Length == 0 ? LegacyScript : path), Home, token);
            modulus = ReadModulus(await script.Content.ReadAsStringAsync(token));
        }

        if (modulus.Length == 0)
            throw new InvalidDataException("Baidu did not supply the password sign-in public key.");

        return new Bootstrap(time, modulus, trace, AccountIdentity.Value(nested, "FP_UID", "fp_uid"));
    }

    private async Task<JsonElement> SubmitPasswordAsync(string username, string password, Bootstrap bootstrap, string imageId,
        string imageCode, CancellationToken token)
    {
        var values = new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = EncodePassword(password, bootstrap.Time, bootstrap.Modulus),
            ["servertime"] = bootstrap.Time,
            ["verifycode"] = imageCode,
            ["vcodestr"] = imageId,
            ["isphone"] = "0",
            ["loginmerge"] = "1",
            ["action"] = "login",
            ["uid"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            ["skin"] = "default_v2",
            ["connect"] = "0",
            ["logLoginType"] = "wap_loginTouch",
            ["gid"] = Guid.NewGuid().ToString("D"),
        };
        if (bootstrap.Trace.Length != 0)
            values["traceid"] = bootstrap.Trace;
        if (bootstrap.Fingerprint.Length != 0)
            values["FP_UID"] = bootstrap.Fingerprint;

        using var form = new FormUrlEncodedContent(values);
        using HttpResponseMessage response = await _transport.SendAsync(HttpMethod.Post, new Uri(Home, "/wp/api/login"), false, token,
            form, configure: request =>
            {
                request.Headers.Referrer = Home;
                request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
            });
        response.EnsureSuccessStatusCode();
        return ParseObject(await response.Content.ReadAsStringAsync(token));
    }

    private async Task<Credentials?> VerifyAccountAsync(JsonElement data, IPassportPrompts prompts, CancellationToken token)
    {
        Uri verification = RequireAllowedUri(AccountIdentity.Value(data, "verifyUrl", "gotoUrl"));
        using (HttpResponseMessage landing = await FollowGetAsync(verification, Home, token)) { }
        Dictionary<string, string> parameters = ReadQuery(verification);
        string authId = AccountIdentity.Value(data, "authid", "authId");
        if (authId.Length == 0)
            authId = parameters.GetValueOrDefault("authid") ?? "";
        string challengeToken = data.Text("token");
        if (challengeToken.Length == 0)
            challengeToken = parameters.GetValueOrDefault("token") ?? "";

        if (authId.Length == 0 && challengeToken.Length == 0)
            throw new InvalidDataException("Baidu did not identify the account verification challenge.");

        VerificationChannel channel = await prompts.ChooseChannelAsync(data.Text("phone"), data.Text("email"), token);
        string field = channel == VerificationChannel.Mobile ? "mobile" : "email";
        if (authId.Length != 0)
        {
            JsonElement sent = await SendWidgetAsync(verification, authId, field, "send", "", token);
            if (WidgetValue(sent, "send") != "success")
                throw new InvalidOperationException("Baidu could not send the verification code.");
        }
        else
        {
            using HttpResponseMessage sent = await FollowGetAsync(LegacyWidget(("action", "send"), ("type", field),
                ("token", challengeToken)), verification, token);
        }

        for (int index = 0; index < 3; index++)
        {
            string code = await prompts.RequestVerificationCodeAsync(channel, index != 0, token);
            if (string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException("A verification code is required.");

            string proxy = data.Text("loginProxy");
            string authSid = data.Text("authsid");
            JsonElement verified;
            if (authId.Length != 0)
            {
                verified = await SendWidgetAsync(verification, authId, field, "check", code, token);
                if (WidgetValue(verified, "check") != "success" || WidgetValue(verified, "status") != "authed")
                    continue;
            }
            else
            {
                string returnUrl = RequireAllowedUri(AccountIdentity.Value(data, "u", "loginProxy") is { Length: > 0 } fromData
                    ? fromData : parameters.GetValueOrDefault("u")).AbsoluteUri;
                using HttpResponseMessage check = await FollowGetAsync(LegacyWidget(("action", "check"), ("type", field),
                    ("token", challengeToken), ("vcode", code), ("u", returnUrl), ("callback", "jsonp1"),
                    ("v", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture))), verification, token);
                verified = ParseObject(await check.Content.ReadAsStringAsync(token));
                if (ErrorCode(verified) != "0")
                    continue;
                if (proxy.Length == 0)
                    proxy = returnUrl;
            }

            JsonElement verificationData = verified.Field("data");
            if (authSid.Length == 0)
                authSid = AccountIdentity.Value(verificationData, "authsid", "authSid");
            if (proxy.Length == 0)
                proxy = AccountIdentity.Value(verificationData, "loginProxy", "u");
            string completion = "";
            if (proxy.Length > 0)
            {
                Uri destination = AppendQuery(RequireAllowedUri(proxy), ("authsid", authSid), ("fromtype", field), ("bindToSmsLogin", ""));
                using HttpResponseMessage finish = await FollowGetAsync(destination, verification, token);
                completion = await finish.Content.ReadAsStringAsync(token);
            }

            Credentials credentials = CollectCredentials(verificationData, completion);
            if (credentials.Bduss.Length == 0)
                credentials = CollectCredentials(data, completion);
            return credentials.Bduss.Length == 0 ? null : credentials;
        }

        throw new InvalidOperationException("Baidu did not accept the verification code.");
    }

    private async Task<JsonElement> SendWidgetAsync(Uri referer, string authId, string field, string action, string code,
        CancellationToken token)
    {
        var parameters = new Dictionary<string, string> { ["banktype"] = "", ["type"] = "vcode", ["secret"] = "" };
        if (action == "check")
            parameters["vcode"] = code;

        using JsonContent content = JsonContent.Create(new
        {
            tpl = "wi", client = "", clientfrom = "", authid = authId, authField = field,
            authType = "check", action, authParams = parameters,
        });
        using HttpResponseMessage response = await _transport.SendAsync(HttpMethod.Post, new Uri(Home, "/v3/api/auth/widget"),
            false, token, content, configure: request => request.Headers.Referrer = referer);
        response.EnsureSuccessStatusCode();
        return ParseObject(await response.Content.ReadAsStringAsync(token));
    }

    private async Task<HttpResponseMessage> FollowGetAsync(Uri uri, Uri referer, CancellationToken token)
    {
        for (int redirects = 0; redirects <= 5; redirects++)
        {
            HttpResponseMessage response = await _transport.SendAsync(HttpMethod.Get, RequireAllowedUri(uri.AbsoluteUri), false,
                token, configure: request => request.Headers.Referrer = referer);
            if (response.IsSuccessStatusCode)
                return response;

            using (response)
            {
                if ((int)response.StatusCode is < 300 or >= 400 || response.Headers.Location is not { } link)
                    throw new BaiduApiException("passport request", 0, response.StatusCode);
                referer = uri;
                uri = new Uri(uri, link);
            }
        }

        throw new InvalidDataException("Baidu exceeded the passport redirect limit.");
    }

    private Credentials CollectCredentials(JsonElement data, string html)
    {
        var cookies = BaiduCookieHeader.Parse(data.Text("cookieString"));
        foreach (Uri source in new[] { Passport, Home, PanHome })
        {
            foreach (Cookie cookie in _transport.Cookies.GetCookies(source))
            {
                if (BaiduCookieHeader.TryNormalizeValue(cookie.Value, out string value))
                    cookies[cookie.Name] = value;
            }
        }

        string From(string key) => cookies.TryGetValue(key, out string? value) && value.Length != 0
            ? value : data.Text(key.ToLowerInvariant());
        string token = Regex.Match(html, @"<stoken>netdisk#(?<value>[a-z0-9-]+)<", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)
            .Groups["value"].Value;
        return new Credentials
        {
            Bduss = From("BDUSS"), Ptoken = From("PTOKEN"), Stoken = token.Length > 0 ? token : From("STOKEN"),
            Baiduid = From("BAIDUID"), Sboxtkn = From("SBOXTKN"), Cookies = BaiduCookieHeader.Format(cookies),
        };
    }

    private static async Task<byte[]> ReadCaptchaAsync(HttpResponseMessage response, CancellationToken token)
    {
        const int maximumSize = 1024 * 1024;
        if (response.Content.Headers.ContentLength is > maximumSize)
            throw new InvalidDataException("The Baidu verification image is too large.");

        await using Stream content = await response.Content.ReadAsStreamAsync(token);
        byte[] buffer = new byte[maximumSize + 1];
        int count = await content.ReadAtLeastAsync(buffer, buffer.Length, false, token);
        if (count < 8 || count > maximumSize || !buffer.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidDataException("Baidu did not return a valid PNG verification image.");

        return buffer[..count];
    }

    internal static string EncodePassword(string password, string time, string modulus)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(password + time);
        try
        {
            var key = new BigInteger(Convert.FromHexString(modulus), isUnsigned: true, isBigEndian: true);
            var message = new BigInteger(plaintext, isUnsigned: true, isBigEndian: false);
            if (key <= message || key.GetBitLength() < 512)
                throw new InvalidDataException("Baidu returned a password key of an unexpected size.");

            return Convert.ToHexStringLower(BigInteger.ModPow(message, 65537, key).ToByteArray(isUnsigned: true, isBigEndian: true));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static Uri LegacyWidget(params (string Key, string Value)[] values) => AppendQuery(new Uri(Home, "/passport/authwidget"),
        [("tpl", ""), ("skin", ""), ("clientfrom", ""), ("adapter", "2"), ("updatessn", ""), ("bindToSmsLogin", ""), .. values]);

    private static Uri AppendQuery(Uri uri, params (string Key, string Value)[] values) => new UriBuilder(uri)
    {
        Query = (uri.Query.Length == 0 ? "" : uri.Query[1..] + "&") + BaiduTransport.EncodeQuery(values),
    }.Uri;

    private static Uri RequireAllowedUri(string? address) => !string.IsNullOrWhiteSpace(address) && Uri.TryCreate(Home, address, out Uri? result)
        && result.Scheme == "https" && result.UserInfo.Length == 0 && result.IsDefaultPort
        && result.Host is "wappass.baidu.com" or "passport.baidu.com" or "pan.baidu.com" or "m.baidu.com" or "www.baidu.com"
        ? result : throw new InvalidDataException("Baidu supplied an unrecognized passport redirect address.");

    private static Dictionary<string, string> ReadQuery(Uri uri) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(value => value.Split('=', 2)).Where(pair => pair.Length == 2).GroupBy(pair => WebUtility.UrlDecode(pair[0]), StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => WebUtility.UrlDecode(group.First()[1]), StringComparer.Ordinal);

    private static string ErrorCode(JsonElement response) => AccountIdentity.Value(response.Field("errInfo"), "no", "errno");

    private static string SafeErrorCode(JsonElement response) => long.TryParse(ErrorCode(response), out long code)
        ? code.ToString(CultureInfo.InvariantCulture) : "unknown";

    private static string WidgetValue(JsonElement response, string property) => response.Text(property) is { Length: > 0 } value
        ? value : response.Field("data").Text(property);

    private static string ReadModulus(string value) => Regex.Match(value, @"\brsa\s*:\s*[""'](?<value>[0-9A-Fa-f]{128,1024})[""']",
        RegexOptions.NonBacktracking).Groups["value"].Value;

    private static JsonElement ParseObject(string body)
    {
        string json = body.Trim();
        if (json.StartsWith("jsonp1(", StringComparison.Ordinal) && json.TrimEnd(';').EndsWith(')'))
            json = json.TrimEnd(';')[7..^1];

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException();
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new InvalidDataException("Baidu returned an unreadable passport response.");
        }
    }

    private static Credentials RequireCredentials(Credentials value) => value.Bduss.Length > 0
        ? value : throw new InvalidDataException("Baidu accepted sign-in without issuing a BDUSS session cookie.");

    public void Dispose() => _transport.Dispose();

    private sealed record Bootstrap(string Time, string Modulus, string Trace, string Fingerprint);
}
