using Proxion.Core;
using Xunit;

namespace Proxion.Core.Tests;

public class ProxySettingsTests
{
    [Fact]
    public void Valid_WhenHostAndPortAreSet()
    {
        var settings = new ProxySettings { Host = "10.0.0.1", Port = 1080 };
        Assert.True(settings.IsValid);
        Assert.Empty(settings.Validate());
    }

    [Theory]
    [InlineData("", 1080)]
    [InlineData("  ", 1080)]
    [InlineData("10.0.0.1", 0)]
    [InlineData("10.0.0.1", 65536)]
    [InlineData("10.0.0.1", -1)]
    public void Invalid_ForMissingHostOrOutOfRangePort(string host, int port)
    {
        var settings = new ProxySettings { Host = host, Port = port };
        Assert.False(settings.IsValid);
        Assert.NotEmpty(settings.Validate());
    }

    [Fact]
    public void TypeLabel_MatchesProxyBridgeExpectedStrings()
    {
        Assert.Equal("socks5", new ProxySettings { Type = ProxyType.Socks5 }.TypeLabel);
        Assert.Equal("http", new ProxySettings { Type = ProxyType.Http }.TypeLabel);
    }
}
