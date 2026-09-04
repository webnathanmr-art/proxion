namespace Proxion.Windows;

/// <summary>Where Proxion keeps its own files - never next to the exe, which might be
/// somewhere read-only or transient like a Downloads folder.</summary>
public static class AppPaths
{
    private static string RootDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Proxion");

    public static string LogsDir => EnsureExists(Path.Combine(RootDir, "logs"));

    public static string RuntimeDir => EnsureExists(Path.Combine(RootDir, "runtime"));

    public static string NewLogFilePath() =>
        Path.Combine(LogsDir, $"proxion-{DateTime.Now:yyyyMMdd-HHmmss}.log");

    /// <summary>ProxyBridge CLI's own captured console output (connection-level detail, not just Proxion's own log).</summary>
    public static string NewProxyBridgeLogFilePath() =>
        Path.Combine(LogsDir, $"proxybridge-{DateTime.Now:yyyyMMdd-HHmmss}.log");

    public static string ProfilePath => Path.Combine(RuntimeDir, "proxion.pbprofile");

    private static string EnsureExists(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
