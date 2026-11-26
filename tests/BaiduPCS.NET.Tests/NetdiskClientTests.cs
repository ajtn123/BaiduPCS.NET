using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using BaiduPCS.Configuration;
using BaiduPCS.Netdisk;
using BaiduPCS.Transfers;

namespace BaiduPCS.Tests;

public sealed class NetdiskClientTests
{
    [Fact]
    public void PrefersIpv6AndKeepsIpv4AvailableForFallback()
    {
        IPAddress ipv4 = IPAddress.Parse("192.0.2.9");
        IPAddress ipv6 = IPAddress.Parse("2001:db8::9");
        Assert.Equal([ipv6, ipv4], BaiduTransport.OrderAddresses([ipv4, ipv6], NetworkFamily.PreferIpv6));
        Assert.Equal([ipv4], BaiduTransport.OrderAddresses([ipv6, ipv4], NetworkFamily.Ipv4));
    }

    [Fact]
    public async Task ListsPublicShareContentsWithoutAnImportRequest()
    {
        using var handler = new FixtureHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Uri uri = request.RequestUri!;
            if (uri.AbsolutePath == "/s/1fixture")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    "{\"loginstate\":1,\"bdstoken\":\"fixture-token\",\"share_uk\":44,\"shareid\":64}") };

            Assert.Equal("/share/list", uri.AbsolutePath);
            if (uri.Query.Contains("root=1", StringComparison.Ordinal))
                return Json(new { list = new[] { new { path = "/remote/docs", fs_id = 1, isdir = 1 } } });

            Assert.Contains("dir=%2Fremote%2Fdocs", uri.Query);
            return Json(new { list = new[] { new { path = "/remote/docs/file", fs_id = 2, isdir = 0, size = 42 } } });
        });
        using var client = Client(handler);

        ShareInventory result = await client.InspectShareAsync("https://pan.baidu.com/s/1fixture", null, TestContext.Current.CancellationToken);

        Assert.True(result.Complete);
        Assert.Equal(2, result.Requests);
        Assert.Equal(["/docs", "/docs/file"], result.Files.Select(file => file.Path));
        Assert.Equal(42, result.Files[1].Size);
    }

    [Fact]
    public async Task RefreshesAnExpiredDirectLinkBeforeRetrying()
    {
        int discoveries = 0;
        using var handler = new FixtureHandler((request, _) =>
        {
            Uri uri = request.RequestUri!;
            if (uri.Query.Contains("method=locatedownload", StringComparison.Ordinal))
                return Json(new { urls = new[] { new { url = $"https://example.com/{++discoveries}", encrypt = 0 } } });
            if (uri.AbsolutePath == "/1")
                return new HttpResponseMessage(HttpStatusCode.Forbidden);

            RangeItemHeaderValue range = Assert.Single(request.Headers.Range!.Ranges);
            byte[] bytes = range.To == 0 ? [42] : [42, 43];
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, range.To!.Value, 2);
            return response;
        });
        using var client = new NetdiskClient(new ClientSettings(), new Account
        {
            Id = 1, Name = "Fixture", Credentials = new Credentials { Bduss = "fixture" },
        }, handler);
        var entry = new RemoteEntry(2, 1, "/file", "file", false, 2, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "", []);
        var options = new DownloadOptions { TestOnly = true, Retries = 1 };

        Assert.True(await client.DownloadAsync(entry, "unused", options, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(2, discoveries);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/folder/..")]
    [InlineData("//")]
    public void RejectsRemoteRootDeletionBeforeCallingBaidu(string path)
    {
        using var handler = new FixtureHandler((_, _) => throw new InvalidOperationException("No request expected."));
        using var client = Client(handler);

        Assert.Throws<ArgumentException>(() => { _ = client.RemoveAsync(path, TestContext.Current.CancellationToken); });
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task ExportUsesAByteRangeForTheSliceAndReadsFullFileChecksumsFromHeaders()
    {
        byte[] prefix = new byte[FileChecksums.SliceLength];
        const string md5 = "a563415c3e27bf849990d79e71b9bfdc";
        const long fileSize = FileChecksums.SliceLength + 12;
        using var handler = new FixtureHandler((request, count) =>
        {
            if (count == 1)
                return Json(new { urls = new[] { new { url = "https://example.com/file", encrypt = 0 } } });

            Assert.Equal(FileChecksums.SliceLength - 1, Assert.Single(request.Headers.Range!.Ranges).To);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(prefix) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, prefix.Length - 1, fileSize);
            Assert.True(response.Content.Headers.TryAddWithoutValidation("Content-MD5", md5));
            response.Headers.TryAddWithoutValidation("x-bs-meta-crc32", "1234567");
            return response;
        });
        using var client = new NetdiskClient(new ClientSettings(), new Account
        {
            Id = 1, Name = "Fixture", Credentials = new Credentials { Bduss = "fixture" },
        }, handler);
        var entry = new RemoteEntry(2, 1, "/file", "file", false, fileSize, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, "", []);

        FileChecksums result = await client.GetExportChecksumsAsync(entry, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new FileChecksums(fileSize, md5, Convert.ToHexStringLower(MD5.HashData(prefix)), 1234567), result);
    }

    [Theory]
    [InlineData("pan.baidu.com/s/1example AbCd", null)]
    [InlineData("https://pan.baidu.com/share/init?surl=example&pwd=abcd", "abcd")]
    [InlineData("通过网盘分享的文件 链接：https://pan.baidu.com/s/1example 提取码：1234", "1234")]
    public void ParsesOldNewAndPastedShareLinks(string text, string? expectedCode)
    {
        Assert.Equal(("1example", expectedCode), NetdiskClient.ParseShare(text));
    }

    [Fact]
    public async Task RecycleClearDoesNotRetryTheMutationSentAsGet()
    {
        using var handler = new FixtureHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("type=recycle", request.RequestUri!.Query);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });
        using var client = Client(handler);

        await Assert.ThrowsAsync<BaiduApiException>(() => client.ClearRecycleBinAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task OfflineQueryReportsMissingIdsAndDecodesStringNumerics()
    {
        using var handler = new FixtureHandler((request, _) =>
        {
            Assert.Contains("task_ids=21%2C22", request.RequestUri!.Query);
            return Json(new { task_info = new Dictionary<string, object>
            {
                ["21"] = new { status = "1", file_size = "42", finished_size = "10" },
            } });
        });
        using var client = Client(handler);

        IReadOnlyList<OfflineTask> tasks = await client.QueryOfflineTasksAsync([21, 22], TestContext.Current.CancellationToken);

        Assert.Equal(42, tasks[0].Size);
        Assert.Equal(10, tasks[0].CompletedBytes);
        Assert.Equal(1, tasks[1].Result);
    }

    [Fact]
    public async Task ResumesMultipartUploadAndFinalizesInPartOrder()
    {
        const int firstSize = 4 * 1024 * 1024;
        byte[] bytes = new byte[firstSize + 2];
        bytes[^1] = 5;
        string firstHash = Convert.ToHexStringLower(MD5.HashData(bytes.AsSpan(0, firstSize)));
        string lastHash = Convert.ToHexStringLower(MD5.HashData(bytes.AsSpan(firstSize)));
        string directory = Directory.CreateTempSubdirectory("baidupcs-upload-test-").FullName;
        try
        {
            string source = Path.Combine(directory, "source");
            await File.WriteAllBytesAsync(source, bytes, TestContext.Current.CancellationToken);
            bool interrupted = true;
            int precreates = 0;
            var confirmed = new List<int>();
            using var handler = new FixtureHandler(async (request, _, token) =>
            {
                Uri uri = request.RequestUri!;
                if (uri.Query.Contains("method=meta", StringComparison.Ordinal))
                    return Json(new { error_code = 31066 });
                if (uri.AbsolutePath == "/api/precreate")
                {
                    precreates++;
                    return Json(new { return_type = 1, uploadid = "fixture-session" });
                }

                if (uri.AbsolutePath == "/api/create")
                {
                    string parameters = Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync(token));
                    Assert.Contains($"block_list=[\"{firstHash}\",\"{lastHash}\"]", parameters);
                    return Json(new { errno = 0 });
                }

                Assert.Contains("uploadid=fixture-session", uri.Query);
                int part = uri.Query.Contains("partseq=0", StringComparison.Ordinal) ? 0 : 1;
                if (interrupted && part == 1)
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

                var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
                byte[] content = await Assert.Single(multipart).ReadAsByteArrayAsync(token);
                Assert.Equal(part == 0 ? firstSize : 2, content.Length);
                confirmed.Add(part);
                return Json(new { md5 = part == 0 ? firstHash : lastHash });
            });
            using var client = new NetdiskClient(new ClientSettings { FixedPcsHost = true },
                new Account { Id = 1, Name = "Fixture" }, handler);
            var options = new UploadOptions { Parallel = 1, Retries = 0, SkipRapidCheck = true };
            await Assert.ThrowsAsync<BaiduApiException>(() => client.UploadAsync(source, "/file", directory, options,
                cancellationToken: TestContext.Current.CancellationToken));
            interrupted = false;

            Assert.Equal(UploadResult.Uploaded, await client.UploadAsync(source, "/file", directory, options,
                cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(1, precreates);
            Assert.Equal([0, 1], confirmed);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ResumesCompletedRangesAfterARequestFails()
    {
        const int firstRange = 8 * 1024 * 1024;
        const int length = firstRange + 3;
        string directory = Directory.CreateTempSubdirectory("baidupcs-test-").FullName;
        try
        {
            bool interrupted = true;
            var ranges = new List<(long From, long To)>();
            using var handler = new FixtureHandler((request, _) =>
            {
                RangeItemHeaderValue range = Assert.Single(request.Headers.Range!.Ranges);
                long from = range.From!.Value;
                long to = range.To!.Value;
                ranges.Add((from, to));
                if (from == firstRange && interrupted)
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

                byte[] bytes = new byte[checked((int)(to - from + 1))];
                Array.Fill(bytes, (byte)(from == firstRange ? 8 : 3));
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes) };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, length);
                return response;
            });
            using var client = Client(handler);
            string destination = Path.Combine(directory, "result");
            var entry = new RemoteEntry(2, 1, "/result", "result", false, length, DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch, "", []);
            var options = new DownloadOptions { Mode = DownloadMode.Pcs, Retries = 0 };

            await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadAsync(entry, destination, options,
                cancellationToken: TestContext.Current.CancellationToken));
            Assert.False(File.Exists(destination));
            interrupted = false;
            ranges.Clear();
            Assert.True(await client.DownloadAsync(entry, destination, options, cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal([(0L, 0L), (firstRange, length - 1L)], ranges);
            byte[] contents = await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken);
            Assert.Equal(length, contents.Length);
            Assert.Equal(3, contents[firstRange - 1]);
            Assert.Equal(8, contents[firstRange]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TestDownloadReadsTheStreamWithoutCreatingOutput()
    {
        using var handler = new FixtureHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3]),
        });
        using var client = Client(handler);
        var entry = new RemoteEntry(2, 1, "/file", "file", false, 3, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "", []);
        string target = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "file");
        Assert.True(await client.DownloadAsync(entry, target, new DownloadOptions { TestOnly = true, Mode = DownloadMode.Stream },
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(Path.GetDirectoryName(target)));
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task DirectDownloadUsesPanAppIdAndKeepsOnlyUsableLinks()
    {
        using var handler = new FixtureHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            string query = request.RequestUri!.Query;
            Assert.Contains("app_id=250528", query);
            Assert.DoesNotContain("app_id=266719", query);
            Assert.Contains("method=locatedownload", query);
            Assert.Contains("devuid=", query);
            return Json(new { urls = new[] {
                new { url = "http://example.com/file", encrypt = 0 },
                new { url = "https://example.com/encrypted", encrypt = 1 },
                new { url = "file:///tmp/local", encrypt = 0 },
            } });
        });
        using var client = new NetdiskClient(new ClientSettings(), new Account
        {
            Id = 1, Name = "Fixture", Credentials = new Credentials { Bduss = "fixture" },
        }, handler);

        Uri link = Assert.Single(await client.LocateDownloadAsync("/file", TestContext.Current.CancellationToken));

        Assert.Equal("https://example.com/file", link.AbsoluteUri);
    }

    [Fact]
    public void MatchesUpstreamDownloadSignatureVectors()
    {
        Assert.Equal("b6bb7a6f46899e181baea58798d4fdb889775c2c", NetdiskClient.DownloadSignature(
            10086, "test_bduss", "1571140066", "O|1E67351CCE80B2CF48DB511CD77ACD9F"));
        Assert.Equal("8RxCbsVeSzn2UjxJAAiV9QQs/WetOj2FJUGwjsMG6SgxFMWlLS/U1Q==", Convert.ToBase64String(
            NetdiskClient.PanSignature("e8c7d729eea7b54551aa594f942decbe", "37dbe07ade9359c1aa70807e847f768c13360ad2")));
    }

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

    private sealed class FixtureHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public FixtureHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
            : this((request, count, _) => Task.FromResult(respond(request, count))) { }

        public int Count { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            response(request, ++Count, cancellationToken);
    }
}
