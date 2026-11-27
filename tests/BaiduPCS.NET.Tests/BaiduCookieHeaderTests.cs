using BaiduPCS.Configuration;
using BaiduPCS.Netdisk;

namespace BaiduPCS.Tests;

public sealed class BaiduCookieHeaderTests
{
    [Fact]
    public void LoadsLongBrowserHeaderAndPreservesBaiduCookieValues()
    {
        const string header = """Cookie: BDUSS=fake-base64+/=; STOKEN=fixture-stoken; RT="z=1&dm=baidu.com&bcn=https%3A%2F%2Ffixture.invalid"; Hm_lvt_fixture=1,2,3; BAIDUID_BFESS=fixture:FG=1; PANPSC=abc%2Bdef%3D; empty=;""";
        string extras = string.Join("; ", Enumerable.Range(1, 24).Select(index => $"extra{index}={index}"));
        using var transport = new BaiduTransport(new ClientSettings(), new Credentials { Cookies = header + extras });
        var pan = new Uri("https://pan.baidu.com/");
        string wire = transport.Cookies.GetCookieHeader(pan);
        var parsed = BaiduCookieHeader.Parse(wire);

        Assert.Equal("fake-base64+/=", parsed["BDUSS"]);
        Assert.Equal("fixture-stoken", parsed["STOKEN"]);
        Assert.Equal("z=1&dm=baidu.com&bcn=https%3A%2F%2Ffixture.invalid", parsed["RT"]);
        Assert.Equal("1,2,3", parsed["Hm_lvt_fixture"]);
        Assert.Equal("fixture:FG=1", parsed["BAIDUID_BFESS"]);
        Assert.Equal("abc%2Bdef%3D", parsed["PANPSC"]);
        Assert.Equal("", parsed["empty"]);
        Assert.Contains("Hm_lvt_fixture=\"1,2,3\"", wire);
        Assert.Equal(31, parsed.Count);
        Assert.Empty(transport.Cookies.GetCookieHeader(new Uri("https://fixture.invalid/")));
    }

    [Fact]
    public void DropsMalformedFieldsWithoutTreatingQuotedDelimitersAsCookiePairs()
    {
        const string header = """BDUSS=known-fixture; invalid name=ignored; bad=bare"quote; RT="x; BDUSS=forged; Extra=forged"; STOKEN=good-fixture; $Path=/;""";
        var parsed = BaiduCookieHeader.Parse(header + " large=" + new string('a', 4097));

        Assert.Equal(2, parsed.Count);
        Assert.Equal("known-fixture", parsed["BDUSS"]);
        Assert.Equal("good-fixture", parsed["STOKEN"]);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\0")]
    public void RejectsControlCharactersBetweenCookieFields(string separator)
    {
        var error = Assert.Throws<ArgumentException>(() => BaiduCookieHeader.Parse($"BDUSS=fixture{separator}Other=value"));

        Assert.DoesNotContain("fixture", error.Message);
    }

    [Fact]
    public void UnquotesAnExplicitCredentialBeforeSigningRequests()
    {
        var credentials = new Credentials { Bduss = "\"fixture-bduss\"", Cookies = "BDUSS=older" };
        using var transport = new BaiduTransport(new ClientSettings(), credentials);

        Assert.Equal("fixture-bduss", BaiduCookieHeader.GetBduss(credentials));
        Assert.Equal("BDUSS=fixture-bduss", transport.Cookies.GetCookieHeader(new Uri("https://pan.baidu.com/")));
    }
}
