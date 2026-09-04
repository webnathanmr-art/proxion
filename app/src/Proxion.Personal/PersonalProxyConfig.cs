using Proxion.Core;

namespace Proxion.Personal;

/// <summary>
/// Hardcoded default proxy configuration for this personal build of Proxion - used
/// until (and unless) an override is saved via the tray icon's Advanced Settings, which
/// takes precedence from then on (see <see cref="UI.PersonalAppContext.LoadEffectiveProxy"/>).
/// The settings window always shows the host/port in effect (never the password) before
/// anything starts, so whoever runs this build can see exactly what it connects to.
///
/// This fork is meant for your own personal use across your own machines - it is not
/// meant to be handed to other people to run, since they'd have no way to see or
/// change what proxy it's hardcoded to.
/// </summary>
internal static class PersonalProxyConfig
{
    /// <summary>Set this if auto-detection of ProxyBridge_CLI.exe doesn't find it (normally unnecessary - it's embedded).</summary>
    public const string? CliPathOverride = null;

    public static ProxySettings Proxy => new()
    {
        // proxy-cheap's own dashboard lists this proxy's "Connection Type" as HTTP, not
        // SOCKS5 - sending a SOCKS5 handshake to an HTTP-only proxy breaks the tunnel
        // for everything routed through it (this was the actual cause of PURPLE's own
        // login failing, and likely Aion 2's connection timing out too). Per
        // ProxyBridge's docs, an HTTP proxy config also means UDP automatically falls
        // back to a direct connection (HTTP proxies can't relay UDP at all), so the
        // Protocol override probably isn't needed once this is correct - but it's
        // still there in Advanced Settings if some other traffic needs it.
        Type = ProxyType.Http,
        Host = "178.94.233.93",
        Port = 42342,
        Username = "IlTdPm49RXatwoz",
        Password = "uyMJ8ldY3kXINeT",
    };
}
