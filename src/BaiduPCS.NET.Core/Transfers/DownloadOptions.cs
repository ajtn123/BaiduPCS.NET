namespace BaiduPCS.Transfers;

public enum DownloadMode { Direct, Pcs, Stream }

public sealed record DownloadOptions
{
    public DownloadMode Mode { get; init; }
    public int LinkIndex { get; init; }
    public int Parallel { get; init; } = 1;
    public int Retries { get; init; } = 3;
    public bool TestOnly { get; init; }
    public bool Overwrite { get; init; }
    public bool Verify { get; init; }
    public string? ExpectedMd5 { get; init; }
    public bool PreserveModifiedTime { get; init; }
    public bool Executable { get; init; }
    public bool Preallocate { get; init; } = true;
}

public sealed record DownloadProgress(long CompletedBytes, long TotalBytes, int? Range)
{
    public bool Resumed { get; init; }
}
