using System.Globalization;
using System.Resources;

namespace BaiduPCS.Cli;

internal static class Ui
{
    private static readonly ResourceManager Resources = new("BaiduPCS.Cli.Resources.Ui", typeof(Ui).Assembly);
    private static readonly CultureInfo Chinese = CultureInfo.GetCultureInfo("zh-Hans");

    internal static string Text(string english) => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh"
        ? Resources.GetString(english, Chinese) ?? english
        : english;
}
