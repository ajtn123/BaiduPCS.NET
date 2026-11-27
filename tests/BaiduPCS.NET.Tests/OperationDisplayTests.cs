using BaiduPCS.Cli;
using Spectre.Console;

namespace BaiduPCS.Tests;

public sealed class OperationDisplayTests
{
    [Fact]
    public async Task LiveTableAddsAndRemovesActiveRowsAndAdaptsToTerminalWidth()
    {
        using var output = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
            Out = new AnsiConsoleOutput(output),
        });
        console.Profile.Width = 100;
        console.Profile.Height = 20;
        var display = new OperationDisplay(new CommonSettings(), "File", console: console);
        var first = display.Begin("/first", 400);
        var second = display.Begin("/second", 400);
        display.Report(first, 100, 400, null);
        display.Report(second, 200, 400, null);
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task running = display.RunAsync(() => complete.Task);
        string expanded = output.ToString();
        Assert.Contains("Speed", expanded);
        Assert.Contains("25", expanded);
        Assert.Contains("50", expanded);
        Assert.Contains("/first", expanded);
        Assert.Contains("/second", expanded);

        console.Profile.Width = 36;
        display.End(first, "Downloaded");
        await Task.Delay(400, TestContext.Current.CancellationToken);
        string narrow = output.ToString()[expanded.Length..];
        display.End(second, "Downloaded");
        complete.SetResult();
        await running;
        Assert.Contains("/second", narrow);
        Assert.DoesNotContain("/first", narrow);
        Assert.DoesNotContain("Speed", narrow);
        Assert.Contains("Downloaded", output.ToString());
    }

    [Fact]
    public void SpeedUsesOnlyTheLastMinuteAndExcludesPreviouslyDownloadedBytes()
    {
        var rate = new RollingTransferRate();
        rate.Observe(5000, 0, resumed: true);
        Assert.Equal(0, rate.At(1));
        rate.Observe(5600, 5, resumed: false);
        rate.Observe(5550, 6, resumed: false);
        rate.Observe(5600, 7, resumed: false);
        Assert.Equal(150, rate.At(8));
        rate.Observe(6800, 55, resumed: false);
        Assert.Equal(30, rate.At(64));
        Assert.Equal(20, rate.At(66));
        Assert.Equal(0, rate.At(116));
    }

    [Fact]
    public void FormatsBytesAndDurationsInHumanAndRawModes()
    {
        var human = new DisplayNumbers(raw: false);
        var raw = new DisplayNumbers(raw: true);
        Assert.Equal("1.5 MiB", human.Bytes(1572864));
        Assert.Equal("1572864 bytes", raw.Bytes(1572864));
        Assert.Equal("2 KiB/s", human.Speed(2048));
        Assert.Equal("1m 1s", human.Duration(61.5));
        Assert.Equal("61.5s", raw.Duration(61.5));
    }
}
