namespace Proxion.App.Services;

/// <summary>Best-effort auto-detection of ProxyBridge_CLI.exe's install location.</summary>
public static class ProxyBridgeLocator
{
    private const string ExeName = "ProxyBridge_CLI.exe";

    public static string? TryAutoDetect()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, ExeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry; skip it.
            }
        }

        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrEmpty(programFiles))
        {
            var defaultPath = Path.Combine(programFiles, "ProxyBridge", ExeName);
            if (File.Exists(defaultPath))
            {
                return defaultPath;
            }
        }

        return null;
    }
}
