using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using BaiduPCS.Cli;
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

    [Theory]
    [InlineData("win-x64", ".exe")]
    [InlineData("linux-musl-arm64", "")]
    public async Task VerifiesStagesAndReplacesTheRuntimeExecutableWithRollback(string runtime, string extension)
    {
        string fileName = OperatingSystem.IsWindows() ? "baidupcs.exe" : "baidupcs";
        string assetName = $"BaiduPCS.NET-v1.2.3-{runtime}{extension}";
        byte[] executable = "replacement"u8.ToArray();
        string checksum = Convert.ToHexStringLower(SHA256.HashData(executable));
        using var http = Fixture(assetName, executable, checksum);
        var updater = new ReleaseUpdater(http);
        StableRelease? release = await updater.LatestAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(release);
        string directory = Directory.CreateTempSubdirectory("baidupcs-release-fixture-").FullName;
        try
        {
            string target = Path.Combine(directory, fileName);
            File.WriteAllText(target, "previous");
            string staged = await updater.StageStandaloneAsync(release, runtime, target, TestContext.Current.CancellationToken);
            Assert.Equal("previous", File.ReadAllText(target));
            string backup = ReleaseUpdater.ReplaceStandalone(target, staged);
            Assert.Equal("replacement", File.ReadAllText(target));
            Assert.Equal("previous", File.ReadAllText(backup));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RemovesOnlyTheFinishedWindowsUpdateHelper()
    {
        string directory = Directory.CreateTempSubdirectory("baidupcs-release-fixture-").FullName;
        try
        {
            string finished = CreateStage();
            string pending = CreateStage();
            string failed = CreateStage();
            string unrelated = CreateStage(".baidupcs-update-not-a-guid");
            string backup = Path.Combine(directory, "baidupcs.exe.previous");
            File.WriteAllText(Path.Combine(pending, "baidupcs.exe"), "replacement");
            File.WriteAllText(Path.Combine(failed, "error.txt"), "failure");
            File.WriteAllText(backup, "rollback");

            ApplyUpdateCommand.RemoveCompletedWindowsStages(directory);

            Assert.False(Directory.Exists(finished));
            Assert.True(Directory.Exists(pending));
            Assert.True(Directory.Exists(failed));
            Assert.True(Directory.Exists(unrelated));
            Assert.Equal("rollback", File.ReadAllText(backup));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        string CreateStage(string? name = null)
        {
            string stage = Directory.CreateDirectory(Path.Combine(directory, name ?? $".baidupcs-update-{Guid.NewGuid():N}")).FullName;
            File.WriteAllText(Path.Combine(stage, "baidupcs-update-helper.exe"), "helper");
            return stage;
        }
    }

    [Fact]
    public async Task RejectsAnExecutableWithTheWrongChecksum()
    {
        const string assetName = "BaiduPCS.NET-v1.2.3-win-x64.exe";
        using var http = Fixture(assetName, [1, 2, 3], new string('0', 64));
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

    private static HttpClient Fixture(string name, byte[] executable, string checksum) => new(new Handler(request =>
    {
        HttpContent data = request.RequestUri!.AbsolutePath.Split('/')[^1] switch
        {
            "latest" => JsonContent.Create(new { tag_name = "v1.2.3", assets = new[] { new { name }, new { name = "SHA256SUMS" } } }),
            "SHA256SUMS" => new StringContent(checksum + "  " + name + "\n"),
            var item when item == name => new ByteArrayContent(executable),
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
