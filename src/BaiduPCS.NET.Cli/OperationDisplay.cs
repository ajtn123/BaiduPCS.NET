using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace BaiduPCS.Cli;

internal sealed class OperationDisplay(CommonSettings settings, string itemHeading, bool showWorker = false, IAnsiConsole? console = null)
{
    private readonly IAnsiConsole? _console = console ?? GetInteractiveConsole();
    private readonly object _gate = new();
    private readonly List<Item> _active = [];
    private readonly List<Result> _results = [];
    private readonly DisplayNumbers _numbers = new(settings.RawNumbers);

    internal async Task RunAsync(Func<Task> action)
    {
        if (_console is null)
        {
            await action();
            return;
        }

        try
        {
            await _console.Live(Text.Empty).AutoClear(true).StartAsync(async context =>
            {
                Task work = action();
                using var refresh = new PeriodicTimer(TimeSpan.FromMilliseconds(150));
                while (!work.IsCompleted)
                {
                    context.UpdateTarget(Render(_console.Profile.Width, _console.Profile.Height));
                    context.Refresh();
                    Task<bool> tick = refresh.WaitForNextTickAsync().AsTask();
                    if (await Task.WhenAny(work, tick) == work)
                        break;
                }
                await work;
            });
        }
        finally
        {
            if (_results.Count != 0)
            {
                var table = CreateTable().AddColumns(itemHeading, "Result");
                foreach (Result row in _results)
                {
                    string result = Markup.Escape(row.Message);
                    table.AddRow(new Text(row.Name), new Markup(row.Failed ? $"[red]{result}[/]" : result));
                }
                _console.Write(table);
            }
        }
    }

    internal Item Begin(string name, long? total = null)
    {
        var item = new Item(name, total);
        if (_console is not null)
        {
            lock (_gate)
                _active.Add(item);
        }
        return item;
    }

    internal void Report(Item item, long completed, long total, int? part, bool resumed = false)
    {
        lock (_gate)
        {
            double elapsed = item.Elapsed.Elapsed.TotalSeconds;
            item.Completed = Math.Clamp(Math.Max(item.Completed, completed), 0, total);
            item.Total = total;
            item.Part = part;
            item.Rate.Observe(item.Completed, elapsed, resumed);
            if (_console is null && showWorker && elapsed - item.LastLog >= 1)
            {
                item.LastLog = elapsed;
                string worker = part is >= 0 ? $" part {part}" : "";
                Console.Error.WriteLine($"{item.Name}{worker}: {TransferText(item)}, {_numbers.Speed(item.Rate.At(elapsed))}");
            }
        }
    }

    internal void End(Item item, string message, bool failed = false, string? plain = null)
    {
        if (_console is not null)
        {
            lock (_gate)
            {
                _active.Remove(item);
                _results.Add(new Result(item.Name, message, failed));
            }
            return;
        }

        if (plain is not null)
            (failed ? Console.Error : Console.Out).WriteLine(plain);
    }

    internal bool TrackProgress => _console is not null || showWorker;
    internal bool IsInteractive => _console is not null;

    private static IAnsiConsole? GetInteractiveConsole() =>
        !Console.IsInputRedirected && !Console.IsOutputRedirected
        && AnsiConsole.Profile.Capabilities is { Interactive: true, Ansi: true } ? AnsiConsole.Console : null;

    private IRenderable Render(int width, int height)
    {
        lock (_gate)
        {
            bool bytes = width >= 44;
            bool speed = width >= 62;
            bool eta = width >= 79;
            bool worker = showWorker && width >= 94;
            int barWidth = Math.Clamp(width / 9, 5, 16);
            var table = CreateTable().AddColumns(itemHeading, "Progress");
            if (bytes)
                table.AddColumn("Transferred");
            if (speed)
                table.AddColumn("Speed");
            if (eta)
                table.AddColumn("ETA");
            if (worker)
                table.AddColumn("Part");

            int capacity = Math.Min(_active.Count, Math.Max(1, height - 6));
            for (int index = 0; index < capacity; index++)
            {
                Item item = _active[index];
                double average = item.Rate.At(item.Elapsed.Elapsed.TotalSeconds);
                var row = new List<IRenderable> { new Text(item.Name) { Overflow = Overflow.Ellipsis }, Progress(item, barWidth) };
                if (bytes)
                    row.Add(new Text(item.Total is null ? "" : TransferText(item)));
                if (speed)
                    row.Add(new Text(item.Total is null ? "" : _numbers.Speed(average)));
                if (eta)
                    row.Add(new Text(item.Total > item.Completed && average > 0
                        ? _numbers.Duration((item.Total.Value - item.Completed) / average) : ""));
                if (worker)
                    row.Add(new Text(item.Part is >= 0 ? $"{item.Part}" : ""));
                table.AddRow(row);
            }

            if (_active.Count == 0)
                return new Text($"{_results.Count} finished");
            return capacity == _active.Count ? table : new Rows(table, new Text($"+ {_active.Count - capacity} active"));
        }
    }

    private static Table CreateTable() => new Table().Border(TableBorder.Simple).Expand();

    private string TransferText(Item item) => settings.RawNumbers
        ? $"{_numbers.Bytes(item.Completed, unit: false)}/{_numbers.Bytes(item.Total ?? 0)}"
        : $"{_numbers.Bytes(item.Completed)} / {_numbers.Bytes(item.Total ?? 0)}";

    private static IRenderable Progress(Item item, int width)
    {
        if (item.Total is not > 0)
            return new Text("Working…");

        double fraction = (double)item.Completed / item.Total.Value;
        int filled = (int)(width * fraction);
        return new Markup($"[green]{new string('━', filled)}[/][grey]{new string('━', width - filled)}[/] {fraction:P0}");
    }

    internal sealed class Item(string name, long? total)
    {
        internal string Name { get; } = string.Concat(name.Select(character => char.IsControl(character) ? '�' : character));
        internal Stopwatch Elapsed { get; } = Stopwatch.StartNew();
        internal RollingTransferRate Rate { get; } = new();
        internal long? Total { get; set; } = total;
        internal long Completed { get; set; }
        internal int? Part { get; set; }
        internal double LastLog { get; set; } = -1;
    }

    private sealed record Result(string Name, string Message, bool Failed);
}

internal sealed class RollingTransferRate
{
    private const int Window = 60;
    private readonly Queue<(long Second, long Bytes)> _samples = new();
    private long _second = -1;
    private long _bytes;
    private long _highWater;
    private double _startedAt = -1;

    internal void Observe(long completed, double elapsed, bool resumed)
    {
        if (resumed)
        {
            _highWater = completed;
            return;
        }

        long delta = completed - _highWater;
        if (delta <= 0)
            return;
        _highWater = completed;
        if (_startedAt < 0)
            _startedAt = Math.Max(0, elapsed - 1);

        long second = (long)elapsed;
        if (second != _second)
        {
            if (_bytes != 0)
                _samples.Enqueue((_second, _bytes));
            _second = second;
            _bytes = 0;
        }
        _bytes += delta;
        Expire(elapsed);
    }

    internal double At(double elapsed)
    {
        if (_startedAt < 0)
            return 0;

        Expire(elapsed);
        long total = _samples.Sum(sample => sample.Bytes);
        if (_second + 1 > elapsed - Window)
            total += _bytes;
        return total / Math.Clamp(elapsed - _startedAt, 1, Window);
    }

    private void Expire(double elapsed)
    {
        while (_samples.TryPeek(out var sample) && sample.Second + 1 <= elapsed - Window)
            _samples.Dequeue();
    }
}
