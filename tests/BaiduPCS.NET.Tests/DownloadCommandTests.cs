using System.Net;
using BaiduPCS.Cli;

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
}
