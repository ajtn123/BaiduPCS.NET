using System.Globalization;
using System.Text;
using Spectre.Console.Rendering;

namespace BaiduPCS.Cli;

internal sealed class ShellEditor(string prompt, IReadOnlyList<string> history, Func<string, Task<string[]>> complete)
{
    private readonly StringBuilder _line = new();
    private readonly RenderOptions _renderOptions = RenderOptions.Create(Spectre.Console.AnsiConsole.Console);
    private int _position;
    private int _previousWidth;

    internal async Task<string?> ReadAsync(CancellationToken token)
    {
        int historyIndex = history.Count;
        string draft = "";
        string[] suggestions = [];
        int suggestion = 0;
        Display();
        while (true)
        {
            while (!Console.KeyAvailable)
                await Task.Delay(20, token);

            token.ThrowIfCancellationRequested();
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            bool control = key.Modifiers.HasFlag(ConsoleModifiers.Control);
            if (key.Key != ConsoleKey.Tab)
                suggestions = [];

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    _position = _line.Length;
                    Display();
                    Console.WriteLine();
                    return _line.ToString();
                case ConsoleKey.D when control && _line.Length == 0:
                case ConsoleKey.Z when control && _line.Length == 0:
                    Console.WriteLine();
                    return null;
                case ConsoleKey.LeftArrow when _position > 0:
                    _position = PreviousPosition();
                    break;
                case ConsoleKey.RightArrow when _position < _line.Length:
                    _position = NextPosition();
                    break;
                case ConsoleKey.A when control:
                case ConsoleKey.Home:
                    _position = 0;
                    break;
                case ConsoleKey.E when control:
                case ConsoleKey.End:
                    _position = _line.Length;
                    break;
                case ConsoleKey.UpArrow when historyIndex > 0:
                    if (historyIndex == history.Count)
                        draft = _line.ToString();
                    Replace(history[--historyIndex]);
                    break;
                case ConsoleKey.DownArrow when historyIndex < history.Count:
                    Replace(++historyIndex == history.Count ? draft : history[historyIndex]);
                    break;
                case ConsoleKey.Backspace when _position > 0:
                    int previous = PreviousPosition();
                    _line.Remove(previous, _position - previous);
                    _position = previous;
                    break;
                case ConsoleKey.Delete when _position < _line.Length:
                case ConsoleKey.D when control && _position < _line.Length:
                    _line.Remove(_position, NextPosition() - _position);
                    break;
                case ConsoleKey.Tab when _position == _line.Length:
                    if (suggestions.Length == 0)
                    {
                        suggestions = await complete(_line.ToString());
                        suggestion = 0;
                    }
                    if (suggestions.Length > 0)
                        Replace(suggestions[suggestion++ % suggestions.Length]);
                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                        _line.Insert(_position++, key.KeyChar);
                    break;
            }

            Display();
        }
    }

    private void Replace(string value)
    {
        _line.Clear().Append(value);
        _position = _line.Length;
    }

    private int PreviousPosition() => StringInfo.ParseCombiningCharacters(_line.ToString()).LastOrDefault(index => index < _position);

    private int NextPosition() => StringInfo.ParseCombiningCharacters(_line.ToString())
        .FirstOrDefault(index => index > _position, _line.Length);

    private void Display()
    {
        string text = _line.ToString();
        string prefix = prompt;
        int limit = Math.Max(1, Console.WindowWidth - 1);
        if (DisplayWidth(prefix) >= limit)
            prefix = limit > 2 ? "> " : "";

        int start = 0;
        int end = text.Length;
        int available = limit - DisplayWidth(prefix);
        int[] positions = [.. StringInfo.ParseCombiningCharacters(text), text.Length];
        int beforeCursor = DisplayWidth(text[.._position]);
        for (int index = 1; beforeCursor > available && index < positions.Length && positions[index] <= _position; index++)
        {
            int next = positions[index];
            beforeCursor -= DisplayWidth(text[start..next]);
            start = next;
        }

        int visibleWidth = DisplayWidth(text[start..end]);
        for (int index = positions.Length - 2; visibleWidth > available && index >= 0 && positions[index] >= _position; index--)
        {
            int previous = positions[index];
            visibleWidth -= DisplayWidth(text[previous..end]);
            end = previous;
        }

        int width = DisplayWidth(prefix) + visibleWidth;
        Console.Write('\r');
        Console.Write(prefix);
        Console.Write(text.AsSpan(start, end - start));
        if (_previousWidth > width)
            Console.Write(new string(' ', Math.Min(_previousWidth, limit) - width));
        Console.Write('\r');
        Console.Write(prefix);
        Console.Write(text.AsSpan(start, _position - start));
        _previousWidth = width;
    }

    private int DisplayWidth(string text) => ((IRenderable)new Spectre.Console.Text(text)).Measure(_renderOptions, int.MaxValue).Max;
}
