using BaiduPCS.Cli;
using Spectre.Console;

namespace BaiduPCS.Tests;

public sealed class OperationDisplayTests
{
    [Fact]
    public async Task LiveTableKeepsFinishedRowsAndAdaptsToTerminalWidth()
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
        var first = display.Begin(display.Enqueue("/first", 400));
        var second = display.Begin(display.Enqueue("/second", 400));
        display.Report(first, 100, 400, null);
        display.Report(second, 200, 400, null);
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task running = display.RunAsync(() => complete.Task);
        string expanded = output.ToString();
        Assert.Contains(Ui.Text("Speed"), expanded);
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
        Assert.Contains("/first", narrow);
        Assert.DoesNotContain(Ui.Text("Speed"), narrow);
        Assert.Contains(Ui.Text("Downloaded"), output.ToString());
    }

    [Fact]
    public void LiveTablePrefersActiveAndSoonestPendingRowsWhenHeightIsLimited()
    {
        using var output = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(output) });
        console.Profile.Width = 100;
        var display = new OperationDisplay(new CommonSettings(), "File", console: console);
        var finished = display.Enqueue("/finished", 1000);
        display.Enqueue("/next", 1000);
        var active = display.Enqueue("/active", 1000);
        display.Enqueue("/later", 1000);
        display.Enqueue("/last", 1000);
        display.Begin(finished);
        display.End(finished, "Downloaded");
        display.Begin(active);
        display.Report(active, 500, 1000, null);

        console.Write(display.Render(100, 20));
        string full = output.ToString();
        Assert.Contains("/finished", full);
        Assert.Contains(Ui.Text("Downloaded"), full);
        Assert.Contains("/next", full);
        Assert.Contains(Ui.Text("Pending"), full);
        Assert.Contains("/active", full);
        Assert.Contains("50%", full);
        Assert.Contains("/later", full);
        Assert.Contains("/last", full);
        Assert.True(full.IndexOf("/finished", StringComparison.Ordinal) < full.IndexOf("/last", StringComparison.Ordinal));

        output.GetStringBuilder().Clear();
        console.Write(display.Render(100, 6));
        string compact = output.ToString();
        Assert.Contains("/active", compact);
        Assert.Contains("/next", compact);
        Assert.True(compact.IndexOf("/active", StringComparison.Ordinal) < compact.IndexOf("/next", StringComparison.Ordinal));
        Assert.Contains(Ui.Format("+ {0} more", 3), compact);
        Assert.DoesNotContain("/later", compact);
        Assert.DoesNotContain("/finished", compact);

        output.GetStringBuilder().Clear();
        var otherFinished = display.Begin("/other-finished");
        display.End(otherFinished, "Downloaded");
        display.End(active, "Downloaded");
        console.Write(display.Render(100, 8));
        string pendingFirst = output.ToString();
        Assert.DoesNotContain("/finished", pendingFirst);
        Assert.DoesNotContain("/other-finished", pendingFirst);
        Assert.Contains("/active", pendingFirst);
        Assert.True(pendingFirst.IndexOf("/next", StringComparison.Ordinal) < pendingFirst.IndexOf("/last", StringComparison.Ordinal));
        Assert.True(pendingFirst.IndexOf("/last", StringComparison.Ordinal) < pendingFirst.IndexOf("/active", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransferColumnsKeepTheirPositionsAsNumbersGrow(bool raw)
    {
        using var output = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(output) });
        console.Profile.Width = 150;
        var display = new OperationDisplay(new CommonSettings { RawNumbers = raw }, "File", console: console);
        var item = display.Begin(display.Enqueue("/file", long.MaxValue));
        display.Report(item, 1, long.MaxValue, null);
        console.Write(display.Render(150, 20));
        string header = Header();
        Assert.Contains(Ui.Text("Transferred"), header);
        Assert.Contains(Ui.Text("Speed"), header);
        Assert.Contains(Ui.Text("ETA"), header);

        output.GetStringBuilder().Clear();
        display.Report(item, long.MaxValue - 1000, long.MaxValue, null);
        console.Write(display.Render(150, 20));
        Assert.Equal(header, Header());
        string transferred = raw ? "9223372036854774807/9223372036854775807 bytes" : "8 EiB / 8 EiB";
        Assert.True(output.ToString().Contains(transferred, StringComparison.Ordinal), output.ToString());

        string Header() => output.ToString().Split('\n').Single(line => line.Contains(Ui.Text("Transferred"), StringComparison.Ordinal));
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
