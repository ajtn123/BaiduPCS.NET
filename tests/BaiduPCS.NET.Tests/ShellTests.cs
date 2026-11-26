using System.Globalization;
using BaiduPCS.Cli;

namespace BaiduPCS.Tests;

public sealed class ShellTests
{
    [Fact]
    public void PreservesSpacesEmptyArgumentsAndWindowsPaths()
    {
        string[] arguments = ShellArguments.Parse("run -- \"two words.exe\" '' C:\\test\\file path\\ with\\ spaces `unicode 中文`")
            .Select(argument => argument.Value).ToArray();

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
}
