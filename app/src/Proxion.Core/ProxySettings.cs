namespace Proxion.Core;

public enum ProxyType
{
    Socks5,
    Http,
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

    /// <summary>The lowercase label ProxyBridge's .pbprofile format expects.</summary>
    public string TypeLabel => Type == ProxyType.Socks5 ? "socks5" : "http";

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
