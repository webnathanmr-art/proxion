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
        // Must always match this proxy's "Connection Type" as shown on proxy-cheap's own
        // dashboard - currently SOCKS5 (it was briefly HTTP there, which is why this was
        // set to Http for a while). A mismatch in either direction breaks the tunnel for
        // everything routed through it - garbled at the protocol level, not just slow -
        // so double check the dashboard again before assuming this value is stale.
        Type = ProxyType.Socks5,
        Host = "178.94.233.93",
        Port = 42342,
        Username = "IlTdPm49RXatwoz",
        Password = "uyMJ8ldY3kXINeT",
    };
}
