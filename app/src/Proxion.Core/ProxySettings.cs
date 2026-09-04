namespace Proxion.Core;

public enum ProxyType
{
    Socks5,
    Http,
}

/// <summary>
/// Which traffic ProxyBridge's rule should actually route through the proxy. Exists
/// mainly as an escape hatch for a common failure mode: most SOCKS5 proxies do not
/// support the UDP ASSOCIATE command, so a game that relies on UDP for its real-time
/// connection can appear to "open but time out" once its traffic is being force-routed
/// through such a proxy - even though the TCP-based parts (login, chat, store, PURPLE
/// itself) work fine. Switching to TcpOnly lets UDP fall back to a direct connection
/// instead of failing through a proxy tunnel that can't actually relay it.
/// </summary>
public enum RuleProtocol
{
    Both,
    TcpOnly,
    UdpOnly,
}

/// <summary>
/// The proxy a user wants PURPLE's traffic routed through. Pure data + validation -
/// no I/O, so it's usable and testable on any platform.
/// </summary>
public sealed class ProxySettings
{
    public ProxyType Type { get; set; } = ProxyType.Socks5;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public RuleProtocol Protocol { get; set; } = RuleProtocol.Both;

    /// <summary>The lowercase label ProxyBridge's .pbprofile format expects.</summary>
    public string TypeLabel => Type == ProxyType.Socks5 ? "socks5" : "http";

    /// <summary>The label ProxyBridge's .pbprofile format expects for the rule's Protocol field.</summary>
    public string ProtocolLabel => Protocol switch
    {
        RuleProtocol.TcpOnly => "TCP",
        RuleProtocol.UdpOnly => "UDP",
        _ => "BOTH",
    };

    public IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            yield return "Proxy host is required.";
        }
        if (Port is < 1 or > 65535)
        {
            yield return "Proxy port must be between 1 and 65535.";
        }
    }

    public bool IsValid => !Validate().Any();
}
