using System.Text.Json;
using System.Text.Json.Serialization;

namespace Proxion.Core;

/// <summary>
/// Builds the JSON for a ProxyBridge ".pbprofile" file: one proxy config, and one
/// PROXY rule matching exactly the process names we've been told to route - PURPLE
/// itself, plus whatever game processes have actually been observed as its children.
/// </summary>
public static class PbProfileBuilder
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Build(
        ProxySettings proxy,
        IEnumerable<string> processNames,
        bool localhostViaProxy = false,
        bool trafficLogging = true)
    {
        var names = processNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var profile = new PbProfile
        {
            Version = "1.0",
            LocalhostViaProxy = localhostViaProxy,
            IsTrafficLoggingEnabled = trafficLogging,
            ProxyConfigs = new List<PbProxyConfig>
            {
                new()
                {
                    Id = 1,
                    Type = proxy.TypeLabel,
                    Host = proxy.Host,
                    Port = proxy.Port.ToString(),
                    Username = proxy.Username ?? string.Empty,
                    Password = proxy.Password ?? string.Empty,
                },
            },
            ProxyRules = new List<PbProxyRule>
            {
                new()
                {
                    ProcessName = string.Join("; ", names),
                    TargetHosts = "*",
                    TargetPorts = "*",
                    Protocol = proxy.ProtocolLabel,
                    Action = "PROXY",
                    IsEnabled = true,
                    ProxyConfigId = 1,
                },
            },
        };

        return JsonSerializer.Serialize(profile, SerializerOptions);
    }

    private sealed class PbProfile
    {
        public string Version { get; set; } = "1.0";
        public bool LocalhostViaProxy { get; set; }
        public bool IsTrafficLoggingEnabled { get; set; }
        public List<PbProxyConfig> ProxyConfigs { get; set; } = new();
        public List<PbProxyRule> ProxyRules { get; set; } = new();
    }

    private sealed class PbProxyConfig
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Port { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    private sealed class PbProxyRule
    {
        public string ProcessName { get; set; } = string.Empty;
        public string TargetHosts { get; set; } = "*";
        public string TargetPorts { get; set; } = "*";
        public string Protocol { get; set; } = "BOTH";
        public string Action { get; set; } = "PROXY";
        public bool IsEnabled { get; set; } = true;
        public int ProxyConfigId { get; set; }
    }
}
