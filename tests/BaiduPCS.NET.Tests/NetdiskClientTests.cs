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
    public async Task OpensNumericShareByOwnerAndShareId()
    {
        using var handler = new FixtureHandler((request, _) =>
        {
            Uri uri = request.RequestUri!;
            Assert.Contains("shareid=64", uri.Query);
            Assert.Contains("uk=44", uri.Query);
            if (uri.AbsolutePath == "/share/link")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"bdstoken":"fixture-token"}""") };
            Assert.Equal("/share/list", uri.AbsolutePath);
            Assert.Contains("root=1", uri.Query);
            Assert.DoesNotContain("shorturl=", uri.Query);
            return Json(new { list = new[] { new { path = "/file", fs_id = 7, isdir = 0, size = 42 } } });
        });
        using var client = Client(handler);

        ShareInventory result = await client.InspectShareAsync("https://pan.baidu.com/share/link?uk=44&shareid=64", null,
            TestContext.Current.CancellationToken);

        Assert.True(result.Complete);
        Assert.Equal(42, Assert.Single(result.Files).Size);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListsRemainingSharesAfterAnExtractionCodeTimeoutUnlessTheCallerCancels(bool cancelCaller)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var handler = new FixtureHandler((request, count) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            if (count == 1)
                return Json(new { list = new[] { new { shareId = 1 }, new { shareId = 2 } } });
            if (count == 2)
            {
                if (cancelCaller)
                    cancellation.Cancel();
                throw new TaskCanceledException("Fixture timeout.");
            }
            return Json(new { pwd = "test" });
        });
        using var client = Client(handler);

        if (cancelCaller)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ListSharesAsync(cancellationToken: cancellation.Token));
            Assert.Equal(2, handler.Count);
        }
        else
        {
            IReadOnlyList<ShareRecord> shares = await client.ListSharesAsync(cancellationToken: cancellation.Token);
            Assert.Equal(new string?[] { null, "test" }, shares.Select(share => share.Code));
            Assert.Equal(3, handler.Count);
        }
    }

    [Theory]
    [InlineData("https://pan.baidu.com/s/1fixture", "/s/1fixture")]
    [InlineData("https://pan.baidu.com/share/link?shareid=64&uk=44", "/share/link")]
    public async Task ImportsASelectedShareFileIntoTheRequestedDirectory(string link, string sharePage)
    {
        using var handler = new FixtureHandler(async (request, _, token) =>
        {
            Uri uri = request.RequestUri!;
            if (uri.AbsolutePath == sharePage)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"bdstoken\":\"fixture-token\",\"share_uk\":44,\"shareid\":64}"),
                };

            if (uri.AbsolutePath == "/share/list")
                return Json(new { list = new[] { new { fs_id = 9, server_filename = "report file.txt" } } });

            Assert.Equal("/share/transfer", uri.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("filename=report+file.txt", uri.Query);
            Assert.Contains("shareid=64", uri.Query);
            Assert.Contains("from=44", uri.Query);
            string body = await request.Content!.ReadAsStringAsync(token);
            Assert.Equal("fsidlist=%5B9%5D&path=%2Fshared+files", body);
            return Json(new { info = new[] { new { path = "/shared files/report file.txt" } } });
        });
        using var client = Client(handler);

        IReadOnlyList<string> saved = await client.ImportShareAsync(link, null,
            "/shared files", [9], collect: false, TestContext.Current.CancellationToken);

        Assert.Equal("/shared files/report file.txt", Assert.Single(saved));
        Assert.Equal(3, handler.Count);
    }

    [Fact]
    public async Task ReportsTheBaiduErrorCodeFromAForbiddenDownload()
    {
        using var handler = new FixtureHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"error_code\":31326,\"error_msg\":\"untrusted response text\"}"),
        });
        using var client = Client(handler);
        var entry = new RemoteEntry(1, 0, "/file", "file", false, 10,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "", []);
        var options = new DownloadOptions { Mode = DownloadMode.Pcs, TestOnly = true, Retries = 0 };

        BaiduApiException error = await Assert.ThrowsAsync<BaiduApiException>(() => client.DownloadAsync(entry, "unused",
            options, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, error.Status);
        Assert.Equal(31326, error.Code);
        Assert.DoesNotContain("untrusted", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshesAnExpiredDirectLinkBeforeRetrying(bool hasBaiduError)
    {
        int discoveries = 0;
        using var handler = new FixtureHandler((request, _) =>
        {
            Uri uri = request.RequestUri!;
            if (uri.Query.Contains("method=locatedownload", StringComparison.Ordinal))
                return Json(new { urls = new[] { new { url = $"https://example.com/{++discoveries}", encrypt = 0 } } });
            if (uri.AbsolutePath == "/1")
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = hasBaiduError ? new StringContent("{\"error_code\":31326}") : null,
                };

            RangeItemHeaderValue range = Assert.Single(request.Headers.Range!.Ranges);
            Assert.Equal(0, range.From);
            Assert.Contains(range.To, new long?[] { 32767, 1 });
            byte[] bytes = [42, 43];
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 1, 2);
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
    public async Task ClearsOfflineRecordsWithAnAccountWideRequest()
    {
        using var handler = new FixtureHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("pan.baidu.com", request.RequestUri!.Host);
            Assert.Equal("/rest/2.0/services/cloud_dl", request.RequestUri.AbsolutePath);
            Assert.Equal("?method=clear_task", request.RequestUri.Query);
            return Json(new { total = 3 });
        });
        using var client = Client(handler);

        Assert.Equal(3, await client.ClearOfflineTasksAsync(TestContext.Current.CancellationToken));
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResumesCompletedRangesAfterARequestFails(bool preallocate)
    {
        const int firstRange = 256 * 1024;
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
            var options = new DownloadOptions { Mode = DownloadMode.Pcs, Retries = 0, Preallocate = preallocate };

            await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadAsync(entry, destination, options,
                cancellationToken: TestContext.Current.CancellationToken));
            Assert.False(File.Exists(destination));
            string staged = Assert.Single(Directory.EnumerateFiles(Path.Combine(directory, ".baidupcs-partial"), "*.part"));
            Assert.Equal(preallocate ? length : firstRange, new FileInfo(staged).Length);
            interrupted = false;
            ranges.Clear();
            Assert.True(await client.DownloadAsync(entry, destination, options, cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal([(0L, 32767L), (firstRange, length - 1L)], ranges);
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

    [Theory]
    [InlineData(DownloadMode.Pcs, "")]
    [InlineData(DownloadMode.Stream, "")]
    [InlineData(DownloadMode.Pcs, "fixture-agent/1.0")]
    public async Task TestDownloadHonorsThePcsUserAgentWithoutCreatingOutput(DownloadMode mode, string pcsUserAgent)
    {
        using var handler = new FixtureHandler((request, _) =>
        {
            Assert.Equal(pcsUserAgent, request.Headers.UserAgent.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        });
        using var client = new NetdiskClient(new ClientSettings { PcsUserAgent = pcsUserAgent },
            new Account { Id = 1, Name = "Fixture" }, handler);
        var entry = new RemoteEntry(2, 1, "/file", "file", false, 3, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "", []);
        string target = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "file");
        Assert.True(await client.DownloadAsync(entry, target, new DownloadOptions { TestOnly = true, Mode = mode },
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
    public async Task FallsBackToPcsWhenPanRejectsTheListingSession()
    {
        using var handler = new FixtureHandler((request, count) =>
        {
            if (count == 1)
            {
                Assert.Equal("/api/list", request.RequestUri!.AbsolutePath);
                return Json(new { errno = -6 });
            }

            Assert.Equal("/rest/2.0/pcs/file", request.RequestUri!.AbsolutePath);
            Assert.Contains("method=list", request.RequestUri.Query);
            Assert.Contains("path=%2Fdocs", request.RequestUri.Query);
            Assert.Contains("by=size", request.RequestUri.Query);
            Assert.Contains("order=desc", request.RequestUri.Query);
            return Json(new { list = new[] { Entry(1, "/docs/report.txt") } });
        });
        using var client = Client(handler);

        RemoteEntry[] files = await client.ListAsync("/docs", FileSort.Size, true, TestContext.Current.CancellationToken)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal("/docs/report.txt", Assert.Single(files).Path);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task RejectsATruncatedPcsFallbackListingBeforeYieldingFiles()
    {
        using var handler = new FixtureHandler((_, count) => count == 1 ? Json(new { errno = -6 }) :
            Json(new { list = Enumerable.Range(1, 1000).Select(i => Entry(i, $"/docs/{i}.txt")) }));
        using var client = Client(handler);
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var entries = client.ListAsync("/docs", cancellationToken: token).GetAsyncEnumerator(token);

        await Assert.ThrowsAsync<InvalidDataException>(() => entries.MoveNextAsync().AsTask());
        Assert.Equal(2, handler.Count);
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
    public async Task ExcludesAlternativesWithinOneGlobSegment()
    {
        using var handler = new FixtureHandler((_, _) => Json(new
        {
            list = new[] { Entry(1, "/docs/draft.txt"), Entry(2, "/docs/test.tmp"), Entry(3, "/docs/report.txt") },
        }));
        using var client = Client(handler);

        IReadOnlyList<string> matches = await client.MatchAsync("/docs", "!(draft*|*.tmp)", TestContext.Current.CancellationToken);

        Assert.Equal(["/docs/report.txt"], matches);
    }

    [Fact]
    public async Task RanksSubdirectoriesByRecursiveSizeWithoutCountingTopLevelFiles()
    {
        using var handler = new FixtureHandler((request, _) =>
        {
            (long Id, string Path, int IsDirectory, long Size)[] entries = Uri.UnescapeDataString(request.RequestUri!.Query) switch
            {
                var query when query.Contains("dir=/docs/a/deep&", StringComparison.Ordinal) =>
                    [(6, "/docs/a/deep/file", 0, 9)],
                var query when query.Contains("dir=/docs/a&", StringComparison.Ordinal) =>
                    [(4, "/docs/a/deep", 1, 0), (5, "/docs/a/file", 0, 8)],
                var query when query.Contains("dir=/docs/b&", StringComparison.Ordinal) =>
                    [(7, "/docs/b/file", 0, 10)],
                _ => [(1, "/docs/a", 1, 0), (2, "/docs/b", 1, 0), (3, "/docs/top", 0, 100)],
            };
            return Json(new { list = entries.Select(item => new { fs_id = item.Id, path = item.Path, isdir = item.IsDirectory, size = item.Size }) });
        });
        using var client = Client(handler);

        var sizes = await client.GetSubdirectorySizesAsync("/docs", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { ("/docs/a", 17L), ("/docs/b", 10L) }, sizes.Select(item => (item.Path, item.Size)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemovesAnAlreadyUploadedSourceOnlyWhenItsChecksumMatches(bool matches)
    {
        using var handler = new FixtureHandler((request, _) =>
        {
            Assert.Contains("method=meta", request.RequestUri!.Query);
            return Json(new
            {
                list = new[] { new { fs_id = 2, path = "/target", size = 3, md5 = matches ? "900150983cd24fb0d6963f7d28e17f72" : new string('0', 32) } },
            });
        });
        using var client = Client(handler);
        string file = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(file, "abc"u8.ToArray(), TestContext.Current.CancellationToken);
            Task<UploadResult> upload = client.UploadAsync(file, "/target", Path.GetTempPath(), new UploadOptions { RemoveSource = true },
                cancellationToken: TestContext.Current.CancellationToken);
            if (matches)
                Assert.Equal(UploadResult.Skipped, await upload);
            else
                await Assert.ThrowsAsync<InvalidDataException>(() => upload);
            Assert.Equal(!matches, File.Exists(file));
        }
        finally
        {
            File.Delete(file);
        }
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
