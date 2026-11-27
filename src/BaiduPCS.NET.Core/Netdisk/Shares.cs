// Adapts the BaiduPCS-Go share protocol to .NET. See THIRD-PARTY-NOTICES.md.
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BaiduPCS.Netdisk;

public sealed record CreatedShare(long Id, Uri Link, string Code);

public sealed record ShareRecord(long Id, string Link, string Path, string? Code, bool Expired, DateTimeOffset? Expires, long Views);

public sealed record SharedFile(long Id, string Path, string Name, long Size, bool IsDirectory, DateTimeOffset Modified);

public sealed record ShareInventory(IReadOnlyList<SharedFile> Files, int Requests, bool Truncated, IReadOnlyList<string> UnlistedDirectories)
{
    public bool Complete => !Truncated && UnlistedDirectories.Count == 0;
}

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
                catch (Exception error) when (error is BaiduApiException or HttpRequestException or InvalidDataException
                    || error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
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
        if (requestedIds.Any(id => id <= 0))
            throw new ArgumentException("Share file IDs must be positive.");

        ShareSession session = await OpenShareAsync(input, code, cancellationToken);
        Uri share = session.Link;
        SharePageState page = session.Page;
        var items = new Dictionary<long, string>();
        for (int index = 1; ; index++)
        {
            JsonElement listing = await ListSharePageAsync(session, null, index, cancellationToken);
            JsonElement[] batch = listing.Items("list").ToArray();
            int before = items.Count;
            foreach (JsonElement value in batch)
            {
                long id = value.Number("fs_id");
                if (id > 0)
                    items.TryAdd(id, value.Text("server_filename"));
            }
            if (batch.Length < 100)
                break;
            if (index == 200 || before == items.Count)
                throw new InvalidDataException("Baidu did not return a complete list of shared files.");
        }

        long[] selected = (requestedIds.Count == 0 ? items.Keys : requestedIds).Distinct().ToArray();
        if (selected.Length == 0)
            throw new InvalidOperationException("The share contains no selectable files.");

        string? first = selected.Select(id => items.GetValueOrDefault(id)).FirstOrDefault(name => !string.IsNullOrEmpty(name));
        if (collect && selected.Length > 1)
        {
            string name = first is not null && !first.Contains('/') ? first + " and other files" : "Shared files";
            destination = RemotePath.Resolve(destination, name);
            await CreateDirectoryAsync(destination, cancellationToken);
        }

        Uri transfer = ShareUrl("transfer", ("app_id", BaiduTransport.PanAppId), ("channel", "chunlei"), ("clienttype", "0"),
            ("web", "1"), ("shareid", page.ShareId), ("from", page.Owner), ("bdstoken", page.SessionToken),
            ("filename", first ?? "user_specified_file"));
        using var transferForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["fsidlist"] = JsonSerializer.Serialize(selected),
            ["path"] = destination,
        });
        JsonElement result = await _transport.JsonAsync(HttpMethod.Post, transfer, "share import", true, cancellationToken,
            transferForm, configure: request => request.Headers.Referrer = share);

        return result.Items("info").Select(item => item.Text("path")).Where(RemotePath.IsAbsolute).ToArray();
    }

    public async Task<ShareInventory> InspectShareAsync(string input, string? code, CancellationToken cancellationToken = default)
    {
        ShareSession session = await OpenShareAsync(input, code, cancellationToken);
        const int requestLimit = 200;
        const int entryLimit = 20_000;
        var requests = new Queue<string?>();
        requests.Enqueue(null);
        var entries = new List<SharedFile>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var failed = new List<string>();
        int requestCount = 0;
        bool truncated = false;
        string? root = null;
        while (requests.TryDequeue(out string? directory) && !truncated)
        {
            for (int page = 1; ; page++)
            {
                if (requestCount == requestLimit || entries.Count == entryLimit)
                {
                    truncated = true;
                    break;
                }

                if (requestCount++ > 0)
                    await Task.Delay(300, cancellationToken);
                JsonElement result;
                try
                {
                    result = await ListSharePageAsync(session, directory, page, cancellationToken);
                }
                catch (Exception error) when (directory is not null && error is BaiduApiException or InvalidDataException or HttpRequestException)
                {
                    failed.Add(directory);
                    break;
                }

                int added = 0;
                JsonElement[] batch = result.Items("list").ToArray();
                foreach (JsonElement item in batch)
                {
                    string path = item.Text("path");
                    if (!RemotePath.IsAbsolute(path) || RemotePath.Resolve("/", path) != path
                        || (directory is not null && RemotePath.Parent(path) != directory) || !seen.Add(path))
                        continue;
                    if (entries.Count == entryLimit)
                    {
                        truncated = true;
                        break;
                    }

                    root ??= RemotePath.Parent(path);
                    string relative = root != "/" && path.StartsWith(root + '/', StringComparison.Ordinal) ? path[root.Length..] : path;
                    string name = AccountIdentity.Value(item, "server_filename", "filename");
                    bool isDirectory = item.Number("isdir") == 1;
                    entries.Add(new SharedFile(item.Number("fs_id"), relative, name.Length == 0 ? RemotePath.Name(path) : name,
                        item.Number("size"), isDirectory, DateTimeOffset.FromUnixTimeSeconds(item.Number("server_mtime"))));
                    if (isDirectory)
                        requests.Enqueue(path);
                    added++;
                }

                if (batch.Length < 100 || truncated)
                    break;
                if (added == 0)
                {
                    truncated = true;
                    break;
                }
            }
        }

        return new ShareInventory(entries, requestCount, truncated, failed);
    }

    private async Task<ShareSession> OpenShareAsync(string input, string? code, CancellationToken cancellationToken)
    {
        LegacyShare? legacy = ParseLegacyShare(input);
        string? shortId = null;
        Uri share;
        if (legacy is null)
        {
            (string token, string? embeddedCode) = ParseShare(input);
            shortId = token[1..];
            code ??= embeddedCode;
            share = BaiduTransport.Build("https", BaiduTransport.PanHost, $"/s/{token}");
        }
        else
        {
            code ??= ReadShareCode(input);
            share = ShareUrl("link", ("shareid", legacy.Id), ("uk", legacy.Owner));
        }
        if (code is not null)
            ValidateExtractionCode(code);

        SharePageState page = await ReadSharePageAsync(share, _transport.PanPage("/disk/home"), cancellationToken, legacy);
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
            Uri referer = shortId is null ? share : ShareUrl("init", ("surl", shortId));
            page = await ReadSharePageAsync(share, referer, cancellationToken, legacy);
        }

        return new ShareSession(share, shortId, page);
    }

    private async Task<JsonElement> ListSharePageAsync(ShareSession session, string? directory, int page, CancellationToken token)
    {
        (string Key, string Value)[] location = (directory, session.ShortId) switch
        {
            (null, { } shortId) => [("root", "1"), ("shorturl", shortId), ("web", "5")],
            (null, null) => [("root", "1"), ("dir", "/"), ("uk", session.Page.Owner), ("shareid", session.Page.ShareId), ("web", "1"),
                ("order", "other"), ("desc", "1"), ("showempty", "0"), ("clienttype", "0")],
            ({ } path, _) => [("dir", path), ("uk", session.Page.Owner), ("shareid", session.Page.ShareId), ("web", "1"),
                ("order", "other"), ("desc", "1"), ("showempty", "0"), ("clienttype", "0")],
        };
        Uri url = ShareUrl("list", [("bdstoken", session.Page.SessionToken), ("app_id", BaiduTransport.PanAppId), ("channel", "chunlei"),
            ("page", BaiduTransport.Format(page)), ("num", "100"), .. location]);
        JsonElement listing = await _transport.JsonAsync(HttpMethod.Get, url, "shared file listing", true, token,
            configure: request => request.Headers.Referrer = session.Link);
        if (listing.Field("list").ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Baidu did not return a shared file list.");
        return listing;
    }

    internal static (string Token, string? Code) ParseShare(string input)
    {
        Match link = Regex.Match(input, @"(?<![\w.-])(?:https?://)?pan\.baidu\.com/(?:s/(?<id>1[A-Za-z0-9_-]{1,22})(?![A-Za-z0-9_-])|share/init\?[^\s，]*?\bsurl=(?<short>[A-Za-z0-9_-]{1,22})(?![A-Za-z0-9_-]))(?:\?pwd=(?<pwd>[A-Za-z0-9]{4}))?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        if (!link.Success)
            throw new ArgumentException("Provide a Baidu Netdisk share URL or copied share text.");

        string token = link.Groups["id"].Success ? link.Groups["id"].Value : "1" + link.Groups["short"].Value;
        return (token, link.Groups["pwd"].Success ? link.Groups["pwd"].Value : ReadShareCode(input));
    }

    private static string? ReadShareCode(string input)
    {
        Match embedded = Regex.Match(input, @"(?:\bpwd=|提取码\s*[：:]\s*|\bcode\s*:\s*)(?<code>[A-Za-z0-9]{4})(?![A-Za-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        return embedded.Success ? embedded.Groups["code"].Value : null;
    }

    private static LegacyShare? ParseLegacyShare(string input)
    {
        Match match = Regex.Match(input, @"(?<![\w.-])(?:https?://)?pan\.baidu\.com/share/link\?(?<query>[^\s，,<>""'#]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        if (!match.Success)
            return null;

        string id = Parameter("shareid");
        string owner = Parameter("uk");
        return new LegacyShare(id, owner);

        string Parameter(string key)
        {
            string[] values = match.Groups["query"].Value.Split('&')
                .Where(part => part.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                .Select(part => part[(key.Length + 1)..]).ToArray();
            if (values is not [string value] || value.Length is < 1 or > 20
                || !value.All(char.IsAsciiDigit) || !ulong.TryParse(value, out ulong number) || number == 0)
                throw new ArgumentException("A numeric share link requires one positive shareid and uk.");
            return value;
        }
    }

    private async Task<SharePageState> ReadSharePageAsync(Uri share, Uri referer, CancellationToken token, LegacyShare? legacy)
    {
        using HttpResponseMessage response = await _transport.SendAsync(HttpMethod.Get, share, true, token,
            configure: request => request.Headers.Referrer = referer);
        if (!response.IsSuccessStatusCode)
            throw new BaiduApiException("share page", 0, response.StatusCode);
        if (response.RequestMessage?.RequestUri is { Host: not ("pan.baidu.com" or "yun.baidu.com") })
            throw new InvalidDataException("Baidu redirected the share page to an unexpected host.");

        string html = await response.Content.ReadAsStringAsync(token);
        if (html.Contains("platform-non-found", StringComparison.Ordinal) || html.Contains("error-404", StringComparison.Ordinal))
            throw new BaiduApiException("share page", 0, HttpStatusCode.NotFound);

        string owner = ReadPageNumber(html, "share_uk");
        string shareId = ReadPageNumber(html, "shareid");
        string session = PanValue("bdstoken", html);
        if (owner.Length == 0)
            owner = legacy?.Owner ?? "";
        if (shareId.Length == 0)
            shareId = legacy?.Id ?? "";
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
    private sealed record LegacyShare(string Id, string Owner);
    private sealed record ShareSession(Uri Link, string? ShortId, SharePageState Page);
}
