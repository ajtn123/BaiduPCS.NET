using System.Net;
using System.Net.Http.Json;
using BaiduPCS.Configuration;
using BaiduPCS.Netdisk;

namespace BaiduPCS.Tests;

public sealed class PassportLoginTests
{
    private static readonly string Modulus = new('a', 128);
    private const string Time = "fixture-time";

    [Fact]
    public async Task CompletesImageChallengeWithUserInput()
    {
        var prompts = new Prompts(VerificationChannel.Mobile);
        int submissions = 0;
        using var client = Create(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/cgi-bin/genimage")
            {
                Assert.Equal("?image-id", request.RequestUri.Query);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([137, 80, 78, 71, 13, 10, 26, 10, 0]) };
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            string form = await request.Content!.ReadAsStringAsync(token);
            Assert.Contains("password=" + PassportLogin.EncodePassword("test-password", Time, Modulus), form, StringComparison.Ordinal);
            Assert.DoesNotContain("test-password", form, StringComparison.Ordinal);
            if (++submissions == 1)
                return Json(new { errInfo = new { no = "500001" }, data = new { codeString = "image-id" } });

            Assert.Contains("verifycode=fixture-image&vcodestr=image-id", form);
            return Json(new { errInfo = new { no = "0" }, data = new { bduss = "test-session", stoken = "test-stoken" } });
        });

        Credentials session = await client.SignInAsync("fixture-user", "test-password", prompts, TestContext.Current.CancellationToken);

        Assert.Equal("test-session", session.Bduss);
        Assert.Equal("test-stoken", session.Stoken);
        Assert.Equal(1, prompts.Images);
        Assert.Equal(2, submissions);
    }

    [Fact]
    public async Task CompletesWidgetPhoneChallengeAndHonorsRetry()
    {
        var prompts = new Prompts(VerificationChannel.Mobile);
        int checks = 0;
        using var client = Create(async (request, token) =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/wp/api/login":
                    return Json(new { errInfo = new { no = "400023" }, data = new
                    {
                        verifyUrl = "/verify?authid=fixture", authsid = "receipt", loginProxy = "/complete", phone = "***1234",
                    } });
                case "/verify":
                    return Text("");
                case "/v3/api/auth/widget":
                    string body = await request.Content!.ReadAsStringAsync(token);
                    Assert.Contains("\"authField\":\"mobile\"", body);
                    if (body.Contains("\"action\":\"send\"", StringComparison.Ordinal))
                        return Json(new { send = "success", status = "unauth" });
                    if (++checks == 1)
                        return Json(new { check = "fail", status = "unauth" });
                    return Json(new { check = "success", status = "authed", data = new { bduss = "widget-session" } });
                case "/complete":
                    Assert.Contains("authsid=receipt&fromtype=mobile", request.RequestUri.Query);
                    return Text("<stoken>netdisk#pan-cookie</stoken>");
                default:
                    throw new InvalidOperationException("Unexpected fixture request.");
            }
        });

        Credentials session = await client.SignInAsync("fixture-user", "test-password", prompts, TestContext.Current.CancellationToken);

        Assert.Equal("widget-session", session.Bduss);
        Assert.Equal("pan-cookie", session.Stoken);
        Assert.Equal([false, true], prompts.Retries);
    }

    [Fact]
    public async Task CompletesLegacyEmailChallengeWithJsonpResponse()
    {
        var prompts = new Prompts(VerificationChannel.Email);
        bool sent = false;
        using var client = Create((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/wp/api/login" => Json(new { errInfo = new { no = "400101" }, data = new
            {
                verifyUrl = "/verify?token=fixture&u=%2Fcomplete", email = "t***@example.com",
            } }),
            "/verify" => Text(""),
            "/passport/authwidget" => Widget(request.RequestUri.Query),
            "/complete" => Text(""),
            _ => throw new InvalidOperationException("Unexpected fixture request."),
        }));

        Credentials session = await client.SignInAsync("fixture-user", "test-password", prompts, TestContext.Current.CancellationToken);

        Assert.True(sent);
        Assert.Equal("legacy-session", session.Bduss);
        Assert.Equal("legacy-ptoken", session.Ptoken);

        HttpResponseMessage Widget(string query)
        {
            Assert.Contains("type=email", query);
            if (query.Contains("action=send", StringComparison.Ordinal))
            {
                sent = true;
                return Text("");
            }

            Assert.Contains("vcode=fixture-code", query);
            return Text("jsonp1({\"errInfo\":{\"no\":\"0\"},\"data\":{\"bduss\":\"legacy-session\",\"ptoken\":\"legacy-ptoken\",\"authsid\":\"sid\"}});");
        }
    }

    [Fact]
    public async Task UsesManualIdentityAfterLookupAndValidatesTheSession()
    {
        using var handler = new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/c/s/login" or "/api/user/getinfo" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "/rest/2.0/pcs/quota" => Json(new { quota = 100, used = 10 }),
            _ => throw new InvalidOperationException("Unexpected fixture request."),
        }));

        Account account = await AccountIdentity.ResolveAsync(new ClientSettings { ManualAccountName = "Manual" },
            new Credentials { Cookies = "BDUSS=fixture-cookie" }, 33, handler, TestContext.Current.CancellationToken);

        Assert.Equal(33UL, account.Id);
        Assert.Equal("Manual", account.Name);
    }

    private static PassportLogin Create(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new ClientSettings(), new Handler((request, token) => request.RequestUri!.AbsolutePath switch
        {
            "/" => Task.FromResult(Text("")),
            "/wp/api/security/antireplaytoken" => Task.FromResult(Json(new { data = new { time = Time, rsa = Modulus } })),
            _ => send(request, token),
        }));

    private static HttpResponseMessage Text(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(data) };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class Prompts(VerificationChannel channel) : IPassportPrompts
    {
        public int Images { get; private set; }
        public List<bool> Retries { get; } = [];

        public ValueTask<string> RequestImageCodeAsync(ReadOnlyMemory<byte> image, CancellationToken cancellationToken)
        {
            Assert.Equal(137, image.Span[0]);
            Images++;
            return ValueTask.FromResult("fixture-image");
        }

        public ValueTask<VerificationChannel> ChooseChannelAsync(string phone, string email, CancellationToken cancellationToken) =>
            ValueTask.FromResult(channel);

        public ValueTask<string> RequestVerificationCodeAsync(VerificationChannel selected, bool retry, CancellationToken cancellationToken)
        {
            Assert.Equal(channel, selected);
            Retries.Add(retry);
            return ValueTask.FromResult("fixture-code");
        }
    }
}
