using BaiduPCS.Configuration;

namespace BaiduPCS.Transfers;

public sealed record UploadOptions
{
    public UploadConflict Conflict { get; init; }
    public int Parallel { get; init; } = 4;
    public int Retries { get; init; } = 3;
    public bool SkipRapidCheck { get; init; }
    public bool RemoveSource { get; init; }
}

public enum UploadResult { Uploaded, RapidUpload, Skipped }

public sealed record UploadProgress(long CompletedBytes, long TotalBytes, int Part);
