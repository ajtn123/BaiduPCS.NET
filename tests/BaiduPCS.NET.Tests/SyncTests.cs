using System.Net;
using System.Net.Http.Json;
using BaiduPCS.Cli;
using BaiduPCS.Cli.Commands;
using BaiduPCS.Configuration;
using BaiduPCS.Netdisk;

namespace BaiduPCS.Tests;

public sealed class SyncTests : IDisposable
{
    private const long Modified = 1_700_000_000;
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("baidupcs-sync-test-");
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task PlansAllModesAndUsesGlobForRecursiveFiles()
    {
        Write("same.txt", "abc");
        Write("different.txt", "abc");
        Write("local-only.txt", "abc");
        Write("nested/local.csv", "abc");
        Write("nested/ignored.tmp", "abc");
        using var client = Client(new Disk([
            Entry(1, "/same.txt", 3, clientModified: Modified),
            Entry(2, "/different.txt", 4),
            Entry(3, "/remote-only.txt", 3),
            Entry(4, "/nested", directory: true),
            Entry(5, "/nested/remote.csv", 3),
        ]));

        SyncPlan upload = await Plan(client, SyncMode.Upload, SyncDirection.Upload);
        Assert.Equal(["different.txt", "local-only.txt"], upload.Changes.Select(change => change.Relative));
        Assert.All(upload.Changes, change => Assert.Equal(SyncAction.Upload, change.Action));

        SyncPlan download = await Plan(client, SyncMode.Download, SyncDirection.Download);
        Assert.Equal(["different.txt", "remote-only.txt"], download.Changes.Select(change => change.Relative));
        Assert.All(download.Changes, change => Assert.Equal(SyncAction.Download, change.Action));

        SyncPlan missing = await Plan(client, SyncMode.TransferMissing, SyncDirection.Both, recursive: true, patterns: ["**/*.csv"]);
        Assert.Equal([(SyncAction.Upload, "nested/local.csv"), (SyncAction.Download, "nested/remote.csv")],
            missing.Changes.Select(change => (change.Action, change.Relative)));

        SyncPlan remove = await Plan(client, SyncMode.RemoveMissing, SyncDirection.Upload, recursive: true, patterns: ["*.csv"]);
        Assert.Equal("nested/remote.csv", Assert.Single(remove.Changes).Relative);
    }

    [Fact]
    public async Task PlansChecksumDifferenceEvenWhenLengthAndTimeMatch()
    {
        Write("same-length.txt", "abc");
        using var client = Client(new Disk([Entry(1, "/same-length.txt", 3,
            md5: "4ed9407630eb1000c0f6b63842defa7d", clientModified: Modified)]));
        SyncPlan plan = await Plan(client, SyncMode.Download, SyncDirection.Download,
            compare: SyncComparison.Length | SyncComparison.Time | SyncComparison.Checksum);
        Assert.Equal("Checksum differs", Assert.Single(plan.Changes).Reason);
    }

    [Fact]
    public async Task RefusesDeletionWhenBaiduReturnsAnIncompleteListing()
    {
        Write("keep.txt", "abc");
        using var client = Client(new Disk([Entry(1, "/other.txt", 3), Entry(1, "/duplicate-id.txt", 3)]));
        await Assert.ThrowsAsync<InvalidDataException>(() => Plan(client, SyncMode.RemoveMissing, SyncDirection.Download));
        Assert.True(File.Exists(Path.Combine(_root.FullName, "keep.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppliesOnlyPreviewedLocalRemovals(bool remoteChanged)
    {
        Write("keep.txt", "abc");
        Write("folder/missing.txt", "abc");
        var disk = new Disk([Entry(1, "/keep.txt", 3)]);
        using var client = Client(disk);
        SyncOptions options = Options(SyncMode.RemoveMissing, SyncDirection.Download, recursive: true);
        SyncPlan plan = await SyncPlan.CreateAsync(client, _root.FullName, "/", options, _token);
        Assert.Equal(["folder/missing.txt", "folder"], plan.Changes.Select(change => change.Relative));

        if (remoteChanged)
        {
            disk.Entries.Add(Entry(2, "/folder", directory: true));
            disk.Entries.Add(Entry(3, "/folder/missing.txt", 3));
        }

        int result = await SyncCommand.ApplyAsync(client, new ClientSettings(), new SyncCommand.Settings(), options, plan, _root.FullName, _token);
        Assert.Equal(remoteChanged ? 1 : 0, result);
        Assert.Equal(remoteChanged, File.Exists(Path.Combine(_root.FullName, "folder", "missing.txt")));
        Assert.True(File.Exists(Path.Combine(_root.FullName, "keep.txt")));
    }

    [Fact]
    public async Task DoesNotRemoveARemoteFileWhenItsLocalCounterpartAppearsAfterPreview()
    {
        var disk = new Disk([Entry(1, "/missing.txt", 3)]);
        using var client = Client(disk);
        SyncOptions options = Options(SyncMode.RemoveMissing, SyncDirection.Upload);
        SyncPlan plan = await SyncPlan.CreateAsync(client, _root.FullName, "/", options, _token);
        Assert.Single(plan.Changes);
        Write("missing.txt", "abc");

        Assert.Equal(1, await SyncCommand.ApplyAsync(client, new ClientSettings(), new SyncCommand.Settings(), options, plan, _root.FullName, _token));
        Assert.Single(disk.Entries);
    }

    [Fact]
    public async Task DownloadsFileAndPreservesModificationTime()
    {
        var disk = new Disk([Entry(1, "/remote.txt", 3, clientModified: Modified)]);
        using var client = Client(disk);
        SyncOptions options = Options(SyncMode.Download, SyncDirection.Download);
        SyncPlan plan = await SyncPlan.CreateAsync(client, _root.FullName, "/", options, _token);

        Assert.Equal(0, await SyncCommand.ApplyAsync(client, new ClientSettings(), new SyncCommand.Settings(), options, plan, _root.FullName, _token));
        string path = Path.Combine(_root.FullName, "remote.txt");
        Assert.Equal("abc", await File.ReadAllTextAsync(path, _token));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(Modified).UtcDateTime, File.GetLastWriteTimeUtc(path));
        Assert.Empty((await Plan(client, SyncMode.Download, SyncDirection.Download)).Changes);
    }

    [Fact]
    public void ExpandsQuotedLocalUploadGlobs()
    {
        Write("first/report 1.csv", "abc");
        Write("second/report 2.csv", "abc");
        Write("second/report.tmp", "abc");

        string pattern = Path.Combine(_root.FullName, "*", "report ?.csv");
        string[] matches = UploadCommand.ExpandLocalPaths(pattern, _token).Select(Path.GetFileName).Order().ToArray()!;
        Assert.Equal(["report 1.csv", "report 2.csv"], matches);
    }

    [Fact]
    public async Task ResolvesLinksAboveTheRootAndRejectsLinksInsideIt()
    {
        if (OperatingSystem.IsWindows())
            return;

        string target = Directory.CreateDirectory(Path.Combine(_root.FullName, "target", "folder")).FullName;
        string alias = Path.Combine(_root.FullName, "alias");
        Directory.CreateSymbolicLink(alias, Path.GetDirectoryName(target)!);
        using var client = Client(new Disk([]));
        SyncOptions options = Options(SyncMode.Upload, SyncDirection.Upload, recursive: true);

        SyncPlan plan = await SyncPlan.CreateAsync(client, Path.Combine(alias, "folder"), "/", options, _token);
        Assert.EndsWith("/target/folder", plan.LocalRoot, StringComparison.Ordinal);
        Assert.Empty(plan.Changes);

        Directory.CreateSymbolicLink(Path.Combine(target, "link"), Path.GetDirectoryName(target)!);
        await Assert.ThrowsAsync<IOException>(() =>
            SyncPlan.CreateAsync(client, Path.Combine(alias, "folder"), "/", options, _token));
    }

    private async Task<SyncPlan> Plan(NetdiskClient client, SyncMode mode, SyncDirection direction, bool recursive = false,
        string[]? patterns = null, SyncComparison compare = SyncComparison.Name | SyncComparison.Time | SyncComparison.Length) =>
        await SyncPlan.CreateAsync(client, _root.FullName, "/", Options(mode, direction, recursive, patterns, compare), _token);

    private static SyncOptions Options(SyncMode mode, SyncDirection direction, bool recursive = false, string[]? patterns = null,
        SyncComparison compare = SyncComparison.Name | SyncComparison.Time | SyncComparison.Length) =>
        new(mode, direction, compare, recursive, patterns ?? []);

    private void Write(string relative, string contents)
    {
        string path = Path.Combine(_root.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        File.SetLastWriteTimeUtc(path, DateTimeOffset.FromUnixTimeSeconds(Modified).UtcDateTime);
    }

    private static NetdiskClient Client(Disk disk) => new(new ClientSettings(), new Account
    {
        Id = 1,
        Name = "Fixture",
        Credentials = new Credentials { Bduss = "fixture-session" },
    }, disk);

    private static EntryData Entry(int id, string path, int size = 0, bool directory = false, string md5 = "", long? clientModified = null) =>
        new(id, path, directory ? 1 : 0, size, Modified + 1, clientModified, md5);

    private sealed record EntryData(int fs_id, string path, int isdir, long size, long server_mtime, long? local_mtime, string md5);

    private sealed class Disk(IEnumerable<EntryData> entries) : HttpMessageHandler
    {
        internal List<EntryData> Entries { get; } = [.. entries];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Uri uri = request.RequestUri!;
            string query = Uri.UnescapeDataString(uri.Query);
            if (uri.AbsolutePath == "/api/list")
            {
                string folder = query.Split('&').Single(value => value.StartsWith("dir="))[4..];
                return Json(new { list = Entries.Where(entry => RemotePath.Parent(entry.path) == folder) });
            }

            if (query.Contains("method=meta", StringComparison.Ordinal))
            {
                var body = Assert.IsType<MultipartFormDataContent>(request.Content);
                string json = await Assert.Single(body).ReadAsStringAsync(token);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                string? path = doc.RootElement.GetProperty("list")[0].GetProperty("path").GetString();
                return Json(new { list = Entries.Where(entry => entry.path == path) });
            }

            if (query.Contains("method=locatedownload", StringComparison.Ordinal))
                return Json(new { urls = new[] { new { url = "https://d.pcs.baidu.com/content" } } });
            if (uri.AbsolutePath == "/content")
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent("abc"u8.ToArray()) };

            Assert.Fail($"Unexpected fixture request: {uri}");
            return new(HttpStatusCode.BadRequest);
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    public void Dispose() => _root.Delete(recursive: true);
}
