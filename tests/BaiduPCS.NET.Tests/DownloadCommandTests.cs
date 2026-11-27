using System.Net;
using BaiduPCS.Cli;
using BaiduPCS.Cli.Commands;
using BaiduPCS.Netdisk;
using BaiduPCS.Transfers;

namespace BaiduPCS.Tests;

public sealed class DownloadCommandTests
{
    [Fact]
    public void ReportsHttpStatusWithoutIncludingTheSignedUrl()
    {
        var error = new HttpRequestException("Request to https://example.com/file?signature=private failed.",
            null, HttpStatusCode.Forbidden);

        Assert.Equal("Download request returned HTTP 403 (Forbidden).", DownloadCommand.DescribeFailure(error));
    }

    [Fact]
    public void ReportsConnectionFailureWithoutIncludingTheExceptionMessage()
    {
        var error = new HttpRequestException(HttpRequestError.NameResolutionError,
            "The signed download host could not be reached.");

        Assert.Equal("Download request failed (NameResolutionError).", DownloadCommand.DescribeFailure(error));
    }

    [Fact]
    public async Task KeepsInterruptedDownloadsAcrossInvocationsForTheOriginalAccount()
    {
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("baidupcs-download-queue-");
        try
        {
            string root = temporary.FullName;
            var original = new DownloadQueue(root, 1);
            var entry = new RemoteEntry(2, 1, "/file", "file", false, 3, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "", []);
            var job = new QueuedDownload(entry, root, Path.Combine(root, "file"),
                new DownloadOptions { Mode = DownloadMode.Stream, Preallocate = false });
            CancellationToken token = TestContext.Current.CancellationToken;
            await original.RecordAsync(job, token);

            var restarted = new DownloadQueue(root, 1);
            QueuedDownload saved = Assert.Single(await restarted.ReadAllAsync(token).ToArrayAsync(token));
            Assert.Equal(job.Target, saved.Target);
            Assert.Equal(job.Options, saved.Options);
            Assert.Equal(entry.Path, saved.Entry.Path);
            Assert.Empty(await new DownloadQueue(root, 2).ReadAllAsync(token).ToArrayAsync(token));
            await Assert.ThrowsAsync<IOException>(() => restarted.RecordAsync(job with { Entry = entry with { Path = "/other" } }, token));

            restarted.Complete(saved);
            Assert.Empty(await restarted.ReadAllAsync(token).ToArrayAsync(token));
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }
}
