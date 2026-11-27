using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BaiduPCS.Configuration;

namespace BaiduPCS.Netdisk;

public sealed class BaiduApiException(string operation, int code, HttpStatusCode status)
    : Exception(code == 0 ? $"Baidu {operation} returned HTTP {(int)status}." : $"Baidu {operation} returned error {code}.")
{
    public int Code { get; } = code;
    public HttpStatusCode Status { get; } = status;
    public bool IsMissing => Code is 31066 or 31297 or -9 || (Code == 0 && Status == HttpStatusCode.NotFound);
}

internal sealed class BaiduTransport : IDisposable
{
    internal const string PanHost = "pan.baidu.com";
    internal const string PcsHost = "pcs.baidu.com";
    internal const string PanAppId = "250528";
    private readonly HttpClient _http;
    private readonly Lazy<HttpClient>? _downloads;
    private readonly ClientSettings _settings;
    private string Scheme => _settings.UseHttps ? "https" : "http";

    internal BaiduTransport(ClientSettings settings, Credentials credentials, HttpMessageHandler? handler = null,
        bool allowRedirects = true)
    {
        _settings = settings;
        if (handler is null)
        {
            LoadCookies(credentials, Cookies);
            _downloads = new(() => CreateClient(CreateHandler(settings, Cookies, allowRedirects, DecompressionMethods.None)));
        }
        _http = CreateClient(handler ?? CreateHandler(settings, Cookies, allowRedirects, DecompressionMethods.GZip));
    }

    private static HttpClient CreateClient(HttpMessageHandler handler) => new(handler) { Timeout = TimeSpan.FromSeconds(100) };

    internal CookieContainer Cookies { get; } = new(CookieContainer.DefaultCookieLimit, 100, CookieContainer.DefaultCookieLengthLimit);

    internal Uri Pcs(string resource, string method, params (string Key, string Value)[] query) =>
        Build(Scheme, string.IsNullOrWhiteSpace(_settings.PcsHost) ? PcsHost : _settings.PcsHost,
            $"/rest/2.0/pcs/{resource}", [("method", method), .. query.Any(item => item.Key == "app_id")
                ? query : [("app_id", Format(_settings.AppId)), .. query]]);

    internal Uri Pan(string resource, params (string Key, string Value)[] query) =>
        Build(Scheme, PanHost, $"/api/{resource}", query);

    internal Uri PanPage(string path) => Build("https", PanHost, path);

    internal static Uri Build(string scheme, string host, string path, params (string Key, string Value)[] query) =>
        new UriBuilder(scheme, host) { Path = path, Query = EncodeQuery(query.OrderBy(pair => pair.Key, StringComparer.Ordinal)) }.Uri;

    internal static string EncodeQuery(IEnumerable<(string Key, string Value)> query) => string.Join('&', query
        .Select(pair => $"{EscapeQueryValue(pair.Key)}={EscapeQueryValue(pair.Value)}"));

    private static string EscapeQueryValue(string value) => Uri.EscapeDataString(value).Replace("%20", "+", StringComparison.Ordinal);

    internal static string Format(long number) => number.ToString(CultureInfo.InvariantCulture);

    internal async Task<JsonElement> JsonAsync(HttpMethod method, Uri uri, string operation, bool pan,
        CancellationToken cancellationToken, HttpContent? content = null, bool retryRead = true,
        Action<HttpRequestMessage>? configure = null)
    {
        using HttpResponseMessage response = retryRead && method == HttpMethod.Get && content is null && configure is null
            ? await GetWithRetryAsync(uri, pan, cancellationToken)
            : await SendAsync(method, uri, pan, cancellationToken, content, configure);
        JsonElement root;
        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            root = document.RootElement.Clone();
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException();
        }
        catch (JsonException)
        {
            if (!response.IsSuccessStatusCode)
                throw new BaiduApiException(operation, 0, response.StatusCode);

            throw new InvalidDataException($"Baidu {operation} returned an invalid response.");
        }

        int code = (int)root.Number("errno");
        if (code == 0)
            code = (int)root.Number("error_code");
        if (!response.IsSuccessStatusCode || code != 0)
            throw new BaiduApiException(operation, code, response.StatusCode);

        return root;
    }

    private async Task<HttpResponseMessage> GetWithRetryAsync(Uri uri, bool pan, CancellationToken token)
    {
        const int retryCount = 2;
        for (int attempt = 0; ; attempt++)
        {
            TimeSpan delay = TimeSpan.FromMilliseconds(250 * (attempt + 1));
            try
            {
                HttpResponseMessage response = await SendAsync(HttpMethod.Get, uri, pan, token);
                if (attempt == retryCount || response.StatusCode is not (HttpStatusCode.RequestTimeout
                    or HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.GatewayTimeout))
                    return response;

                if (response.Headers.RetryAfter?.Delta is { } retryAfter)
                    delay = retryAfter;
                if (delay > TimeSpan.FromSeconds(5))
                    return response;

                response.Dispose();
            }
            catch (HttpRequestException error) when (attempt < retryCount && error.HttpRequestError is
                HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.ResponseEnded)
            {
                // Retry only failures where repeating a read is safe.
            }

            await Task.Delay(delay, token);
        }
    }

    internal async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, bool pan,
        CancellationToken cancellationToken, HttpContent? content = null, Action<HttpRequestMessage>? configure = null,
        bool rawDownload = false)
    {
        string agent = pan ? _settings.PanUserAgent : _settings.PcsUserAgent;
        if (string.IsNullOrEmpty(agent) && !uri.AbsolutePath.StartsWith("/rest/2.0/pcs/", StringComparison.Ordinal))
            agent = _settings.UserAgent;

        using var request = new HttpRequestMessage(method, uri) { Content = content };
        if (uri.Scheme == Uri.UriSchemeHttps)
            request.Version = HttpVersion.Version20;
        AddCdnCookies(uri);
        if (agent.Length != 0)
            request.Headers.TryAddWithoutValidation("User-Agent", agent);
        configure?.Invoke(request);
        HttpClient client = rawDownload && _downloads is not null ? _downloads.Value : _http;
        using var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (content is null)
            headersTimeout.CancelAfter(TimeSpan.FromSeconds(25));
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token);
    }

    private void AddCdnCookies(Uri uri)
    {
        if (uri.Host != "baidupcs.com" && !uri.Host.EndsWith(".baidupcs.com", StringComparison.OrdinalIgnoreCase))
            return;

        CookieCollection accountCookies = Cookies.GetCookies(new Uri("https://pcs.baidu.com/"));
        foreach (Cookie cookie in accountCookies)
        {
            if (cookie.Name is "BDUSS" or "STOKEN" or "PTOKEN" or "SBOXTKN" or "BAIDUID")
                Cookies.Add(uri, new Cookie(cookie.Name, cookie.Value, "/") { Secure = uri.Scheme == "https" });
        }
    }

    internal static MultipartFormDataContent JsonForm(object value)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8), "param");
        return content;
    }

    private static void LoadCookies(Credentials credentials, CookieContainer cookies)
    {
        var combined = BaiduCookieHeader.Parse(credentials.Cookies);
        SetCredential("BDUSS", credentials.Bduss);
        SetCredential("PTOKEN", credentials.Ptoken);
        SetCredential("STOKEN", credentials.Stoken);
        SetCredential("BAIDUID", credentials.Baiduid);
        SetCredential("SBOXTKN", credentials.Sboxtkn);
        try
        {
            foreach ((string name, string value) in combined)
                cookies.Add(new Cookie(name, BaiduCookieHeader.WireValue(value), "/", ".baidu.com"));
        }
        catch (CookieException)
        {
            throw new ArgumentException("The saved Baidu cookies contain an invalid name or value.");
        }

        void SetCredential(string name, string value)
        {
            if (value.Length != 0)
                combined[name] = BaiduCookieHeader.NormalizeCredential(name, value);
        }
    }

    private static SocketsHttpHandler CreateHandler(ClientSettings settings, CookieContainer cookies,
        bool allowRedirects, DecompressionMethods decompression)
    {
        var handler = new SocketsHttpHandler
        {
            CookieContainer = cookies,
            AllowAutoRedirect = allowRedirects,
            AutomaticDecompression = decompression,
            MaxAutomaticRedirections = 10,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(90),
            Expect100ContinueTimeout = TimeSpan.FromSeconds(10),
        };
        if (!string.IsNullOrWhiteSpace(settings.Proxy))
            handler.Proxy = new ScopedProxy(settings.Proxy, settings.ProxyHosts);

        string[] addresses = settings.LocalAddresses.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (addresses.Length > 0 || settings.NetworkFamily != NetworkFamily.Auto)
        {
            IPAddress[] bindAddresses = addresses.Select(value => IPAddress.TryParse(value, out IPAddress? address)
                ? address : throw new ArgumentException("Local addresses must be IPv4 or IPv6 addresses.")).ToArray();
            handler.ConnectCallback = (context, token) => ConnectAsync(context.DnsEndPoint, bindAddresses, settings.NetworkFamily, token);
        }

        return handler;
    }

    private static async ValueTask<Stream> ConnectAsync(DnsEndPoint destination, IPAddress[] local, NetworkFamily family, CancellationToken token)
    {
        IPAddress[] remote = await Dns.GetHostAddressesAsync(destination.Host, token);
        SocketException? lastError = null;
        foreach (IPAddress address in OrderAddresses(remote, family))
        {
            IEnumerable<IPAddress?> bindings = local.Length == 0 ? [null]
                : local.Where(candidate => candidate.AddressFamily == address.AddressFamily).Cast<IPAddress?>();
            foreach (IPAddress? source in bindings)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    if (source is not null)
                        socket.Bind(new IPEndPoint(source, 0));
                    await socket.ConnectAsync(new IPEndPoint(address, destination.Port), token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (SocketException error)
                {
                    socket.Dispose();
                    lastError = error;
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        }

        throw new HttpRequestException("Unable to connect using the configured network addresses.", lastError);
    }

    internal static IEnumerable<IPAddress> OrderAddresses(IEnumerable<IPAddress> addresses, NetworkFamily family) => family switch
    {
        NetworkFamily.Ipv4 => addresses.Where(address => address.AddressFamily == AddressFamily.InterNetwork),
        NetworkFamily.PreferIpv6 => addresses.OrderByDescending(address => address.AddressFamily == AddressFamily.InterNetworkV6),
        _ => addresses,
    };

    public void Dispose()
    {
        if (_downloads is { IsValueCreated: true })
            _downloads.Value.Dispose();
        _http.Dispose();
    }

    private sealed class ScopedProxy : IWebProxy
    {
        private readonly Uri _address;
        private readonly string[] _hosts;

        internal ScopedProxy(string address, string hosts)
        {
            if (!address.Contains("://", StringComparison.Ordinal))
                address = "http://" + address;
            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? parsed) || parsed.Host.Length == 0
                || parsed.Scheme is not ("http" or "https" or "socks5"))
                throw new ArgumentException("Configure an HTTP, HTTPS, or SOCKS5 proxy address.");

            _address = new UriBuilder(parsed) { UserName = "", Password = "" }.Uri;
            _hosts = hosts.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parsed.UserInfo.Length > 0)
            {
                string[] credentials = parsed.UserInfo.Split(':', 2);
                Credentials = new NetworkCredential(Uri.UnescapeDataString(credentials[0]),
                    credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "");
            }
        }

        public ICredentials? Credentials { get; set; }

        private bool Matches(Uri destination) => _hosts.Length == 0 || _hosts.Any(host =>
            destination.Host.Equals(host.TrimStart('.'), StringComparison.OrdinalIgnoreCase)
            || destination.Host.EndsWith('.' + host.TrimStart('.'), StringComparison.OrdinalIgnoreCase));

        public Uri? GetProxy(Uri destination) => Matches(destination) ? _address : HttpClient.DefaultProxy.GetProxy(destination);
        public bool IsBypassed(Uri host) => !Matches(host) && HttpClient.DefaultProxy.IsBypassed(host);
    }
}
