using System.Net;
using System.Net.Http.Json;
using BaiduPCS.Configuration;
using BaiduPCS.Netdisk;

namespace BaiduPCS.Tests;

public sealed class NetdiskClientTests
{
    [Fact]
    public async Task RetriesTransientQuotaResponse()
    {
        using var handler = new FixtureHandler((_, count) =>
        {
            if (count != 1)
                return Json(new { quota = 32, used = 12 });

            var unavailable = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            unavailable.Headers.RetryAfter = new(TimeSpan.Zero);
            return unavailable;
        });
        using var client = Client(handler);

        Quota quota = await client.GetQuotaAsync(TestContext.Current.CancellationToken);

        Assert.Equal(20, quota.Free);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task ListsPastFirstPageAndStopsWhenServerRepeatsPage()
    {
        var fullPage = Enumerable.Range(1, 1000).Select(index => Entry(index, $"/folder/file-{index}")).ToArray();
        var secondPage = Enumerable.Range(1000, 1000).Select(index => Entry(index, $"/folder/file-{index}")).ToArray();
        using var handler = new FixtureHandler((request, count) =>
        {
            Assert.Equal("pan.baidu.com", request.RequestUri!.Host);
            Assert.Equal("/api/list", request.RequestUri.AbsolutePath);
            Assert.Contains("dir=%2Ffolder", request.RequestUri.Query);
            Assert.Contains($"page={count}", request.RequestUri.Query);
            return Json(new { errno = 0, list = count == 1 ? fullPage : secondPage });
        });
        using var client = Client(handler);

        RemoteEntry[] entries = await client.ListAsync("/folder", cancellationToken: TestContext.Current.CancellationToken)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1999, entries.Length);
        Assert.Equal(3, handler.Count);
    }

    [Fact]
    public async Task ExpandsUnicodeWildcardsAndTreatsBracketsLiterally()
    {
        using var handler = new FixtureHandler((request, _) =>
        {
            Assert.Contains("dir=%2Fdocs", request.RequestUri!.Query);
            return Json(new { list = new[] { Entry(1, "/docs/[a]😀.txt"), Entry(2, "/docs/[a]AB.txt"), Entry(3, "/docs/aX.txt") } });
        });
        using var client = Client(handler);

        IReadOnlyList<string> matches = await client.MatchAsync("/work", "../docs/[a]?.txt", TestContext.Current.CancellationToken);

        Assert.Equal(["/docs/[a]😀.txt"], matches);
    }

    [Fact]
    public async Task MapsBaiduFileErrorAndSendsBatchMetadataAsMultipart()
    {
        using var handler = new FixtureHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/rest/2.0/pcs/file", request.RequestUri!.AbsolutePath);
            Assert.Contains("method=meta", request.RequestUri.Query);
            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            Assert.Equal("param", Assert.Single(multipart).Headers.ContentDisposition!.Name);
            return Json(new { error_code = 31066 });
        });
        using var client = Client(handler);

        Assert.Null(await client.FindAsync("/missing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SurfacesMutationFailureReturnedInsideSuccessfulHttpResponse()
    {
        using var handler = new FixtureHandler((request, _) =>
        {
            Assert.Contains("method=move", request.RequestUri!.Query);
            return Json(new { info = new[] { new { errno = -8 } } });
        });
        using var client = Client(handler);

        BaiduApiException error = await Assert.ThrowsAsync<BaiduApiException>(
            () => client.MoveAsync("/source", "/target", TestContext.Current.CancellationToken));
        Assert.Equal(-8, error.Code);
    }

    private static NetdiskClient Client(HttpMessageHandler handler) =>
        new(new ClientSettings(), new Account { Id = 1, Name = "Fixture" }, handler);

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    private static object Entry(long id, string path) => new { fs_id = id, path, server_filename = RemotePath.Name(path) };

    private sealed class FixtureHandler(Func<HttpRequestMessage, int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Count { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request, ++Count));
    }
}
