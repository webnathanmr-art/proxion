using System.Text.Json;
using Proxion.Core;
using Xunit;

namespace Proxion.Core.Tests;

public class PbProfileBuilderTests
{
    private static readonly ProxySettings Socks5 = new()
    {
        Type = ProxyType.Socks5,
        Host = "203.0.113.10",
        Port = 1080,
        Username = "",
        Password = "",
    };

    [Fact]
    public void Build_ProducesOneProxyConfigAndOneRule_WithExpectedShape()
    {
        var json = PbProfileBuilder.Build(Socks5, new[] { "PurpleLauncher.exe", "BnS.exe" });
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("1.0", root.GetProperty("Version").GetString());
        Assert.False(root.GetProperty("LocalhostViaProxy").GetBoolean());
        Assert.True(root.GetProperty("IsTrafficLoggingEnabled").GetBoolean());

        var configs = root.GetProperty("ProxyConfigs");
        Assert.Equal(1, configs.GetArrayLength());
        var config = configs[0];
        Assert.Equal(1, config.GetProperty("Id").GetInt32());
        Assert.Equal("socks5", config.GetProperty("Type").GetString());
        Assert.Equal("203.0.113.10", config.GetProperty("Host").GetString());
        Assert.Equal("1080", config.GetProperty("Port").GetString()); // ProxyBridge expects Port as a string

        var rules = root.GetProperty("ProxyRules");
        Assert.Equal(1, rules.GetArrayLength());
        var rule = rules[0];
        Assert.Equal("PurpleLauncher.exe; BnS.exe", rule.GetProperty("ProcessName").GetString());
        Assert.Equal("*", rule.GetProperty("TargetHosts").GetString());
        Assert.Equal("*", rule.GetProperty("TargetPorts").GetString());
        Assert.Equal("BOTH", rule.GetProperty("Protocol").GetString());
        Assert.Equal("PROXY", rule.GetProperty("Action").GetString());
        Assert.True(rule.GetProperty("IsEnabled").GetBoolean());
        Assert.Equal(1, rule.GetProperty("ProxyConfigId").GetInt32());
    }

    [Fact]
    public void Build_DeduplicatesProcessNamesCaseInsensitively()
    {
        var json = PbProfileBuilder.Build(Socks5, new[] { "BnS.exe", "bns.exe", "BNS.EXE" });
        using var doc = JsonDocument.Parse(json);

        var processName = doc.RootElement.GetProperty("ProxyRules")[0].GetProperty("ProcessName").GetString();

        Assert.Equal("BnS.exe", processName);
    }

    [Fact]
    public void Build_SkipsBlankProcessNames()
    {
        var json = PbProfileBuilder.Build(Socks5, new[] { "PurpleLauncher.exe", "", "   " });
        using var doc = JsonDocument.Parse(json);

        var processName = doc.RootElement.GetProperty("ProxyRules")[0].GetProperty("ProcessName").GetString();

        Assert.Equal("PurpleLauncher.exe", processName);
    }

    [Fact]
    public void Build_UsesHttpTypeLabel_ForHttpProxies()
    {
        var http = new ProxySettings { Type = ProxyType.Http, Host = "10.0.0.1", Port = 8080 };
        var json = PbProfileBuilder.Build(http, new[] { "PurpleLauncher.exe" });
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("http", doc.RootElement.GetProperty("ProxyConfigs")[0].GetProperty("Type").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Build_WritesEmptyStringForMissingCredentials_NeverNull(string? value)
    {
        var proxy = new ProxySettings { Type = ProxyType.Socks5, Host = "10.0.0.1", Port = 1080, Username = value!, Password = value! };
        var json = PbProfileBuilder.Build(proxy, new[] { "PurpleLauncher.exe" });
        using var doc = JsonDocument.Parse(json);

        var config = doc.RootElement.GetProperty("ProxyConfigs")[0];
        Assert.Equal(string.Empty, config.GetProperty("Username").GetString());
        Assert.Equal(string.Empty, config.GetProperty("Password").GetString());
    }
}
