using System.Globalization;
using System.Reflection;
using BaiduPCS.Cli;
using BaiduPCS.Cli.Commands;
using Spectre.Console.Cli;

namespace BaiduPCS.Tests;

public sealed class ShellTests
{
    [Fact]
    public void PreservesSpacesEmptyArgumentsAndWindowsPaths()
    {
        string[] arguments = [.. ShellArguments.Parse("run -- \"two words.exe\" '' C:\\test\\file path\\ with\\ spaces `unicode 中文`").Select(argument => argument.Value)];

        Assert.Equal(["run", "--", "two words.exe", "", @"C:\test\file", "path with spaces", "unicode 中文"], arguments);
    }

    [Theory]
    [InlineData("/file with spaces/中文")]
    [InlineData("/file\\name/with\"quotes")]
    [InlineData("/folder/'test`'")]
    public void CompletionEscapesPathsRoundTrip(string path)
    {
        Assert.Equal(path, Assert.Single(ShellArguments.Parse(ShellArguments.Quote(path))).Value);
    }

    [Fact]
    public void OmitsCredentialCommandsFromHistory()
    {
        Assert.False(ShellCommand.CanRecord(["account", "login", "--cookies", "synthetic"]));
        Assert.False(ShellCommand.CanRecord(["tool", "encrypt", "--key", "synthetic"]));
        Assert.False(ShellCommand.CanRecord(["config", "set", "proxy=synthetic"]));
        Assert.True(ShellCommand.CanRecord(["list", "/"]));
    }

    [Theory]
    [InlineData("zh-Hans", "列出网盘目录。")]
    [InlineData("zh-TW", "列出网盘目录。")]
    [InlineData("en-US", "List a remote directory.")]
    public void UsesTheRequestedHelpLocale(string culture, string description)
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal(description, Ui.Text("List a remote directory."));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void EveryCommandArgumentAndOptionHasLocalizedHelp()
    {
        PropertyInfo[] parameters = [.. typeof(CommonSettings).Assembly.GetTypes()
            .Where(type => typeof(CommonSettings).IsAssignableFrom(type))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(property => property.GetCustomAttribute<CommandOptionAttribute>() is not null
                || property.GetCustomAttribute<CommandArgumentAttribute>() is not null)];
        Assert.NotEmpty(parameters);

        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            foreach (PropertyInfo property in parameters)
            {
                UiDescriptionAttribute description = Assert.IsType<UiDescriptionAttribute>(property.GetCustomAttribute<UiDescriptionAttribute>());
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
                string english = description.Description;
                Assert.False(string.IsNullOrWhiteSpace(english), property.ToString());
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-Hans");
                Assert.NotEqual(english, description.Description);
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
