using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BaiduPCS.Netdisk;

public sealed record CreatedShare(long Id, Uri Link, string Code);

public sealed record ShareRecord(long Id, string Link, string Path, string? Code, bool Expired, DateTimeOffset? Expires, long Views);

public sealed partial class NetdiskClient
{
    public async Task<CreatedShare> CreateShareAsync(IReadOnlyCollection<string> paths, string? code, int days,
        CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0 || days < 0)
            throw new ArgumentException("Specify paths and a nonnegative share duration.");

        code ??= RandomNumberGenerator.GetString("0123456789abcdefghijklmnopqrstuvwxyz", 4);
        ValidateExtractionCode(code);
        JsonElement result = await SendFormAsync(ShareUrl("pset"), "share creation", new()
        {
            ["path_list"] = JsonSerializer.Serialize(paths),
            ["schannel"] = "4",
            ["channel_list"] = "[]",
            ["period"] = BaiduTransport.Format(days),
            ["pwd"] = code,
            ["share_type"] = "9",
        }, cancellationToken);

        long id = result.Number("shareid");
        Uri link = ParseHttpUri(result.Text("link")) ?? throw new InvalidDataException("Baidu did not return a valid share link.");
        return new CreatedShare(id, link, code);
    }

    public async Task<IReadOnlyList<ShareRecord>> ListSharesAsync(int page = 1, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        JsonElement result = await _transport.JsonAsync(HttpMethod.Get, ShareUrl("record", ("page", BaiduTransport.Format(page)),
            ("desc", "1"), ("order", "time")), "share listing", true, cancellationToken);
        var shares = new List<ShareRecord>();
        foreach (JsonElement entry in result.Items("list"))
        {
            long id = entry.Number("shareId");
            bool expired = entry.Number("expiredType") == -1;
            long remaining = entry.Number("expiredTime");
            string? code = null;
            if (id > 0 && entry.Number("public") == 0 && !expired)
            {
                try
                {
                    string sign = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(BaiduTransport.Format(id) + "_sharesurlinfo!@#")));
                    JsonElement details = await _transport.JsonAsync(HttpMethod.Get, ShareUrl("surlinfoinrecord", ("shareid", BaiduTransport.Format(id)),
                        ("sign", sign)), "share details", true, cancellationToken);
                    code = details.Text("pwd").Trim();
                }
                catch (Exception error) when (error is BaiduApiException or HttpRequestException or InvalidDataException)
                {
                    // The list remains usable when an individual extraction code cannot be retrieved.
                }
            }

            shares.Add(new(id, entry.Text("shortlink"), entry.Text("typicalPath"), code, expired,
                remaining > 0 ? DateTimeOffset.UtcNow.AddSeconds(remaining) : null, entry.Number("vCnt")));
        }

        return shares;
    }

    public async Task CancelShareAsync(long id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        await SendFormAsync(ShareUrl("cancel"), "share cancellation", new() { ["shareid_list"] = $"[{BaiduTransport.Format(id)}]" },
            cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ImportShareAsync(string input, string? code, string destination,
        IReadOnlyCollection<long> requestedIds, bool collect, CancellationToken cancellationToken = default)
    {
        (string token, string? embeddedCode) = ParseShare(input);
        code ??= embeddedCode;
        if (code is not null)
            ValidateExtractionCode(code);
        if (requestedIds.Any(id => id <= 0))
            throw new ArgumentException("Share file IDs must be positive.");

        Uri share = BaiduTransport.Build("https", BaiduTransport.PanHost, $"/s/{token}");
        SharePageState page = await ReadSharePageAsync(share, _transport.PanPage("/disk/home"), cancellationToken);
        if (code is not null)
        {
            Uri verify = ShareUrl("verify", ("shareid", page.ShareId), ("clienttype", "1"), ("uk", page.Owner),
                ("time", BaiduTransport.Format(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())));
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["pwd"] = code,
                ["vcode"] = "null",
                ["vcode_str"] = "null",
                ["bdstoken"] = page.SessionToken,
            });
            await _transport.JsonAsync(HttpMethod.Post, verify, "share verification", true, cancellationToken, form,
                configure: request => request.Headers.Referrer = share);
            page = await ReadSharePageAsync(share, ShareUrl("init", ("surl", token[1..])), cancellationToken);
        }

        Uri listUrl = ShareUrl("list", ("bdstoken", page.SessionToken), ("root", "1"), ("web", "5"),
            ("app_id", BaiduTransport.PanAppId), ("shorturl", token[1..]), ("channel", "chunlei"));
        JsonElement listing = await _transport.JsonAsync(HttpMethod.Get, listUrl, "shared file listing", true, cancellationToken,
            configure: request => request.Headers.Referrer = share);
        var items = listing.Items("list").Select(value => (Id: value.Number("fs_id"), Name: value.Text("server_filename")))
            .Where(item => item.Id > 0).ToArray();
        long[] selected = (requestedIds.Count == 0 ? items.Select(item => item.Id) : requestedIds).Distinct().ToArray();
        if (selected.Length == 0)
            throw new InvalidOperationException("The share contains no selectable files.");

        if (collect && selected.Length > 1)
        {
            string first = items.FirstOrDefault(item => selected.Contains(item.Id)).Name ?? "";
            string name = first.Length != 0 && !first.Contains('/') ? first + " and other files" : "Shared files";
            destination = RemotePath.Resolve(destination, name);
            await CreateDirectoryAsync(destination, cancellationToken);
        }

        Uri transfer = ShareUrl("transfer", ("app_id", BaiduTransport.PanAppId), ("channel", "chunlei"), ("clienttype", "0"),
            ("web", "1"), ("shareid", page.ShareId), ("from", page.Owner), ("bdstoken", page.SessionToken));
        using var transferForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["fsidlist"] = JsonSerializer.Serialize(selected),
            ["path"] = destination,
        });
        JsonElement result = await _transport.JsonAsync(HttpMethod.Post, transfer, "share import", true, cancellationToken,
            transferForm, configure: request => request.Headers.Referrer = share);

        return result.Items("info").Select(item => item.Text("path")).Where(RemotePath.IsAbsolute).ToArray();
    }

    internal static (string Token, string? Code) ParseShare(string input)
    {
        Match link = Regex.Match(input, @"(?<![\w.-])(?:https?://)?pan\.baidu\.com/(?:s/(?<id>1[A-Za-z0-9_-]{1,22})(?![A-Za-z0-9_-])|share/init\?[^\s，]*?\bsurl=(?<short>[A-Za-z0-9_-]{1,22})(?![A-Za-z0-9_-]))(?:\?pwd=(?<pwd>[A-Za-z0-9]{4}))?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        if (!link.Success)
            throw new ArgumentException("Provide a Baidu Netdisk share URL or copied share text.");

        string token = link.Groups["id"].Success ? link.Groups["id"].Value : "1" + link.Groups["short"].Value;
        string code = link.Groups["pwd"].Value;
        if (code.Length == 0)
        {
            Match embedded = Regex.Match(input, @"(?:\bpwd=|提取码\s*[：:]\s*|\bcode\s*:\s*)(?<code>[A-Za-z0-9]{4})(?![A-Za-z0-9])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
            code = embedded.Groups["code"].Value;
        }

        return (token, code.Length == 0 ? null : code);
    }

    private async Task<SharePageState> ReadSharePageAsync(Uri share, Uri referer, CancellationToken token)
    {
        using HttpResponseMessage response = await _transport.SendAsync(HttpMethod.Get, share, true, token,
            configure: request => request.Headers.Referrer = referer);
        if (!response.IsSuccessStatusCode)
            throw new BaiduApiException("share page", 0, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync(token);
        if (html.Contains("platform-non-found", StringComparison.Ordinal) || html.Contains("error-404", StringComparison.Ordinal))
            throw new BaiduApiException("share page", 0, HttpStatusCode.NotFound);

        string owner = ReadPageNumber(html, "share_uk");
        string shareId = ReadPageNumber(html, "shareid");
        string session = PanValue("bdstoken", html);
        if (owner.Length == 0 || shareId.Length == 0 || session.Length == 0)
            throw new InvalidDataException("The Baidu share page did not return session data. Check the share and account cookies.");

        return new SharePageState(owner, shareId, session);
    }

    private static string ReadPageNumber(string html, string key) => Regex.Match(html,
        $"\"{key}\"\\s*:\\s*\"?(?<value>[0-9]+)", RegexOptions.NonBacktracking).Groups["value"].Value;

    private static Uri ShareUrl(string path, params (string Key, string Value)[] query) =>
        BaiduTransport.Build("https", BaiduTransport.PanHost, "/share/" + path, query);

    private static void ValidateExtractionCode(string code)
    {
        if (code.Length != 4 || !code.All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException("A share code must contain four ASCII letters or digits.");
    }

    private sealed record SharePageState(string Owner, string ShareId, string SessionToken);
}
