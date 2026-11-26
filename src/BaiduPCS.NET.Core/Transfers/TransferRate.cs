using System.Diagnostics;

namespace BaiduPCS.Transfers;

internal sealed class TransferRate(long bytesPerSecond)
{
    private readonly object _gate = new();
    private double _reservedThrough;

    internal Task WaitAsync(int bytes, CancellationToken token)
    {
        if (bytesPerSecond <= 0)
            return Task.CompletedTask;

        double waitSeconds;
        lock (_gate)
        {
            double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            _reservedThrough = Math.Max(now, _reservedThrough) + bytes / (double)bytesPerSecond;
            waitSeconds = _reservedThrough - now;
        }

        return WaitAsync(waitSeconds, token);
    }

    private static async Task WaitAsync(double seconds, CancellationToken token)
    {
        while (seconds > 3600)
        {
            await Task.Delay(TimeSpan.FromHours(1), token);
            seconds -= 3600;
        }

        await Task.Delay(TimeSpan.FromSeconds(seconds), token);
    }
}
