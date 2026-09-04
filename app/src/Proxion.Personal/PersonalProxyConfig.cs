using Proxion.Core;

namespace Proxion.Personal;

/// <summary>
/// Hardcoded proxy configuration for this personal build of Proxion. There is no
/// settings UI in this fork by design - edit these constants and rebuild if the proxy
/// ever changes. The disclaimer window (<see cref="UI.DisclaimerForm"/>) always shows
/// the host/port below (never the password) before anything starts, so whoever runs
/// this build can see exactly what it connects to.
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
        Type = ProxyType.Socks5,
        Host = "178.94.233.93",
        Port = 42342,
        Username = "IlTdPm49RXatwoz",
        Password = "uyMJ8ldY3kXINeT",
    };
}
