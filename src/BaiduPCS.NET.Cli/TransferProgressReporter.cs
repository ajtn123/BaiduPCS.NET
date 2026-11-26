using System.Diagnostics;

namespace BaiduPCS.Cli;

internal sealed class TransferProgressReporter(string path, bool partStatus)
{
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly object _gate = new();
    private double _lastTime = -1;
    private long _lastBytes;

    internal void Report(long completed, long total, int? part = null)
    {
        lock (_gate)
        {
            double elapsed = _elapsed.Elapsed.TotalSeconds;
            if (_lastTime >= 0 && elapsed - _lastTime < 1 && completed < total)
                return;

            string worker = partStatus && part is >= 0 ? $" part {part}" : "";
            if (_lastTime < 0)
                Console.Error.WriteLine($"{path}{worker}: {completed}/{total} bytes");
            else
            {
                double speed = Math.Max(0, completed - _lastBytes) / Math.Max(0.001, elapsed - _lastTime);
                string remaining = speed > 0 && total > completed ? $", {(total - completed) / speed:F0}s remaining" : "";
                Console.Error.WriteLine($"{path}{worker}: {completed}/{total} bytes, {speed:F0} bytes/s{remaining}");
            }

            _lastTime = elapsed;
            _lastBytes = completed;
        }
    }
}
