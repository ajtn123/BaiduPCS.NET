using System.Globalization;
using Spectre.Console.Cli;

namespace BaiduPCS.Cli.Commands;

internal sealed class BeijingTimeCommand : Command<CommonSettings>
{
    public override int Execute(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        Console.WriteLine(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        return 0;
    }
}
