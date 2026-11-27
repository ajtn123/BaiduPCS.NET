using System.ComponentModel;
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

    internal static string Format(string english, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, Text(english), values);
}

[AttributeUsage(AttributeTargets.Property)]
internal sealed class UiDescriptionAttribute(string english) : DescriptionAttribute(english)
{
    public override string Description => Ui.Text(base.Description);
}
