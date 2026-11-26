using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using BaiduPCS.Utilities;

namespace BaiduPCS.Tests;

public sealed class ReleaseUpdaterTests
{
    [Theory]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("1.0.0", "1.0.0-dev", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "2.0.0-dev", false)]
    public void ComparesStableReleasesNumerically(string stable, string current, bool expected)
    {
        var release = new StableRelease("v" + stable, Version.Parse(stable), new HashSet<string>());
        Assert.Equal(expected, ReleaseUpdater.IsNewer(release, current));
    }

    [Fact]
    public async Task VerifiesStagesAndReplacesTheRuntimeArchiveWithRollback()
    {
        string fileName = OperatingSystem.IsWindows() ? "baidupcs.exe" : "baidupcs";
        const string archiveName = "BaiduPCS.NET-v1.2.3-win-x64.zip";
        byte[] archive = CreateArchive(fileName);
        string checksum = Convert.ToHexStringLower(SHA256.HashData(archive));
        using var http = Fixture(archiveName, archive, checksum);
        var updater = new ReleaseUpdater(http);
        StableRelease? release = await updater.LatestAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(release);
        string directory = Directory.CreateTempSubdirectory("baidupcs-release-fixture-").FullName;
        try
        {
            string target = Path.Combine(directory, fileName);
            File.WriteAllText(target, "previous");
            string staged = await updater.StageStandaloneAsync(release, "win-x64", target, TestContext.Current.CancellationToken);
            Assert.Equal("previous", File.ReadAllText(target));
            string backup = ReleaseUpdater.ReplaceStandalone(target, staged);
            Assert.Equal("replacement", File.ReadAllText(target));
            Assert.Equal("previous", File.ReadAllText(backup));
            Assert.False(File.Exists(Path.Combine(directory, "unexpected.txt")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsAnArchiveWithTheWrongChecksum()
    {
        const string archiveName = "BaiduPCS.NET-v1.2.3-win-x64.zip";
        using var http = Fixture(archiveName, [1, 2, 3], new string('0', 64));
        var updater = new ReleaseUpdater(http);
        StableRelease? release = await updater.LatestAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(release);
        string directory = Directory.CreateTempSubdirectory("baidupcs-release-fixture-").FullName;
        try
        {
            string target = Path.Combine(directory, "baidupcs.exe");
            File.WriteAllText(target, "previous");
            await Assert.ThrowsAsync<InvalidDataException>(() => updater.StageStandaloneAsync(release, "win-x64", target,
                TestContext.Current.CancellationToken));
            Assert.Equal("previous", File.ReadAllText(target));
            Assert.Empty(Directory.EnumerateDirectories(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] CreateArchive(string fileName)
    {
        using var data = new MemoryStream();
        using (var zip = new ZipArchive(data, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var executable = new StreamWriter(zip.CreateEntry(fileName).Open()))
                executable.Write("replacement");
            using var unexpected = new StreamWriter(zip.CreateEntry("../unexpected.txt").Open());
            unexpected.Write("ignored");
        }

        return data.ToArray();
    }

    private static HttpClient Fixture(string name, byte[] archive, string checksum) => new(new Handler(request =>
    {
        HttpContent data = request.RequestUri!.AbsolutePath.Split('/')[^1] switch
        {
            "latest" => JsonContent.Create(new { tag_name = "v1.2.3", assets = new[] { new { name }, new { name = "SHA256SUMS" } } }),
            "SHA256SUMS" => new StringContent(checksum + "  " + name + "\n"),
            var item when item == name => new ByteArrayContent(archive),
            _ => throw new InvalidOperationException("Unexpected fixture request."),
        };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = data };
    }));

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply(request));
    }
}
