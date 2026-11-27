using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace BaiduPCS.Cli;

internal sealed class OperationDisplay(CommonSettings settings, string itemHeading, bool showWorker = false, IAnsiConsole? console = null)
{
    private readonly IAnsiConsole? _console = console ?? GetInteractiveConsole();
    private readonly Lock _gate = new();
    private readonly List<Item> _items = [];
    private readonly LinkedList<Item> _pending = new();
    private readonly List<Item> _active = [];
    private readonly List<Item> _finished = [];
    private readonly DisplayNumbers _numbers = new(settings.RawNumbers);
    private bool _hasTransfers;

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
            if (_finished.Count != 0)
            {
                var table = CreateTable().AddColumns(Ui.Text(itemHeading), Ui.Text("Result"));
                foreach (Item row in _items.Where(item => item.Result is not null))
                {
                    string result = Markup.Escape(row.Result!);
                    table.AddRow(new Text(row.Name), new Markup(row.Failed ? $"[red]{result}[/]" : result));
                }
                _console.Write(table);
            }
        }
    }

    internal Item Enqueue(string name, long? total = null)
    {
        var item = new Item(name, total);
        if (_console is not null)
        {
            lock (_gate)
            {
                _items.Add(item);
                item.Pending = _pending.AddLast(item);
                _hasTransfers |= total.HasValue;
            }
        }
        return item;
    }

    internal Item Begin(string name) => Begin(Enqueue(name));

    internal Item Begin(Item item)
    {
        lock (_gate)
        {
            item.StartedAt = Stopwatch.GetTimestamp();
            if (item.Pending is { } node)
            {
                _pending.Remove(node);
                item.Pending = null;
                _active.Add(item);
            }
        }
        return item;
    }

    internal void Report(Item item, long completed, long total, int? part, bool resumed = false)
    {
        lock (_gate)
        {
            double elapsed = Elapsed(item);
            item.Completed = Math.Clamp(Math.Max(item.Completed, completed), 0, total);
            item.Total = total;
            _hasTransfers = true;
            item.Part = part;
            RollingTransferRate rate = item.Rate ??= new();
            rate.Observe(item.Completed, elapsed, resumed);
            if (_console is null && showWorker && elapsed - item.LastLog >= 1)
            {
                item.LastLog = elapsed;
                string worker = part is >= 0 ? Ui.Format(" part {0}", part) : "";
                Console.Error.WriteLine($"{item.Name}{worker}: {TransferText(item)}, {_numbers.Speed(rate.At(elapsed))}");
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
                item.Result = Ui.Text(message);
                item.Failed = failed;
                item.Rate = null;
                _finished.Add(item);
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

    internal IRenderable Render(int width, int height)
    {
        lock (_gate)
        {
            if (_items.Count == 0)
                return Text.Empty;

            int transferWidth = settings.RawNumbers ? 45 : 25;
            int speedWidth = settings.RawNumbers ? 27 : 13;
            // One observed byte in a 60-second window bounds the longest estimate.
            int etaWidth = settings.RawNumbers ? 24 : 21;
            const int partWidth = 6;
            int progressWidth = Math.Clamp(width / 6, 9, 22);
            // The table reserves left and right edges, column padding, and one separator per column gap.
            int free = width - progressWidth - 7;
            bool bytes = free >= transferWidth + 12 && _hasTransfers;
            if (bytes)
                free -= transferWidth + 3;
            bool speed = bytes && free >= speedWidth + 12;
            if (speed)
                free -= speedWidth + 3;
            bool eta = speed && free >= etaWidth + 12;
            if (eta)
                free -= etaWidth + 3;
            bool worker = showWorker && bytes && free >= partWidth + 12;
            if (worker)
                free -= partWidth + 3;

            var table = CreateTable().AddColumn(Column(itemHeading, Math.Max(1, free)))
                .AddColumn(Column("Progress", progressWidth));
            if (bytes)
                table.AddColumn(Column("Transferred", transferWidth).RightAligned());
            if (speed)
                table.AddColumn(Column("Speed", speedWidth).RightAligned());
            if (eta)
                table.AddColumn(Column("ETA", etaWidth).RightAligned());
            if (worker)
                table.AddColumn(Column("Part", partWidth).RightAligned());

            int capacity = Math.Max(1, height - 3);
            bool overflow = _items.Count > capacity;
            IEnumerable<Item> prioritized = overflow
                ? _active.Concat(_pending).Concat(Enumerable.Range(0, _finished.Count).Select(index => _finished[^(index + 1)]))
                    .Take(Math.Max(1, capacity - 1))
                : _items;
            int shown = 0;
            foreach (Item item in prioritized)
            {
                double average = item.Rate?.At(Elapsed(item)) ?? 0;
                var row = new List<IRenderable> { Cell(item.Name), Progress(item, progressWidth - 6) };
                if (bytes)
                    row.Add(Cell(item.Pending is not null || item.Total is null ? "" : TransferText(item)));
                if (speed)
                    row.Add(Cell(average > 0 ? _numbers.Speed(average) : ""));
                if (eta)
                    row.Add(Cell(item.Total > item.Completed && average > 0
                        ? _numbers.Duration((item.Total.Value - item.Completed) / average) : ""));
                if (worker)
                    row.Add(Cell(item.Result is null && item.Part is >= 0 ? $"{item.Part}" : ""));
                table.AddRow(row);
                shown++;
            }

            return overflow ? new Rows(table, new Text(Ui.Format("+ {0} more", _items.Count - shown))) : table;
        }
    }

    private static Table CreateTable() => new Table().Border(TableBorder.Simple).Expand();
    private static Text Cell(string text) => new(text) { Overflow = Overflow.Ellipsis };
    private static TableColumn Column(string title, int width) => new TableColumn(Ui.Text(title)).Width(width).NoWrap();
    private static double Elapsed(Item item) => Stopwatch.GetElapsedTime(item.StartedAt).TotalSeconds;

    private string TransferText(Item item) => settings.RawNumbers
        ? $"{_numbers.Bytes(item.Completed, unit: false)}/{_numbers.Bytes(item.Total ?? 0)}"
        : $"{_numbers.Bytes(item.Completed)} / {_numbers.Bytes(item.Total ?? 0)}";

    private static IRenderable Progress(Item item, int width)
    {
        if (item.Result is { } result)
            return item.Failed ? new Text(Ui.Text("Failed"), new Style(Color.Red)) : Cell(result);
        if (item.Pending is not null)
            return new Text(Ui.Text("Pending"));
        if (item.Total is not > 0)
            return new Text(Ui.Text("Working…"));

        double fraction = (double)item.Completed / item.Total.Value;
        int filled = (int)(width * fraction);
        return new Markup(FormattableString.Invariant($"[green]{new string('━', filled)}[/][grey]{new string('━', width - filled)}[/] {fraction * 100:0}%"));
    }

    internal sealed class Item(string name, long? total)
    {
        internal string Name { get; } = string.Concat(name.Select(character => char.IsControl(character) ? '�' : character));
        internal LinkedListNode<Item>? Pending { get; set; }
        internal long StartedAt { get; set; }
        internal RollingTransferRate? Rate { get; set; }
        internal string? Result { get; set; }
        internal bool Failed { get; set; }
        internal long? Total { get; set; } = total;
        internal long Completed { get; set; }
        internal int? Part { get; set; }
        internal double LastLog { get; set; } = -1;
    }

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
