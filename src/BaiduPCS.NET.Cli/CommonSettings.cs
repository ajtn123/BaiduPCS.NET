using System.ComponentModel;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli;

internal class CommonSettings : CommandSettings
{
    [CommandOption("--verbose")]
    [Description("Enable diagnostic output.")]
    public bool Verbose { get; init; }

    [CommandOption("--raw-numbers")]
    [Description("Show exact byte counts and durations in seconds.")]
    public bool RawNumbers { get; init; }
}
