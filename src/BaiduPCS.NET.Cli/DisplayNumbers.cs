using System.Globalization;

namespace BaiduPCS.Cli;

internal readonly struct DisplayNumbers(bool raw)
{
    private static readonly string[] Units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];

    internal string Bytes(long value, bool unit = true) => raw
        ? value.ToString(CultureInfo.InvariantCulture) + (unit ? " bytes" : "")
        : Size(value);

    internal string Speed(double bytesPerSecond) => (raw
        ? FormattableString.Invariant($"{bytesPerSecond:F0} bytes")
        : Size(bytesPerSecond)) + "/s";

    internal string Duration(double seconds)
    {
        if (raw)
            return FormattableString.Invariant($"{seconds:F1}s");
        if (seconds < 60)
            return FormattableString.Invariant($"{seconds:0.#} s");
        if (seconds < 3600)
            return FormattableString.Invariant($"{Math.Floor(seconds / 60):0}m {Math.Floor(seconds % 60):0}s");
        if (seconds < 86400)
            return FormattableString.Invariant($"{Math.Floor(seconds / 3600):0}h {Math.Floor(seconds % 3600 / 60):0}m");
        return FormattableString.Invariant($"{Math.Floor(seconds / 86400):0}d {Math.Floor(seconds % 86400 / 3600):0}h");
    }

    private static string Size(double value)
    {
        int unit = 0;
        while (Math.Abs(value) >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return FormattableString.Invariant($"{value:0.##} {Units[unit]}");
    }
}
