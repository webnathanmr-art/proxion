using System.Reflection;

namespace Proxion.Windows;

/// <summary>
/// ProxyBridge CLI (v4.0.0, MIT-licensed - https://github.com/InterceptSuite/ProxyBridge) is
/// embedded directly in this assembly so Proxion doesn't require a separate ProxyBridge
/// install. On first use it's extracted to disk (WinDivert's driver has to be loaded from a
/// real file, an embedded resource stream isn't enough), and re-extracted only if missing -
/// the files are static, and extracting a couple hundred KB every run would be wasteful.
/// </summary>
public static class EmbeddedProxyBridge
{
    private const string ResourcePrefix = "Proxion.Windows.Assets.ProxyBridge.";

    private static readonly string[] FileNames =
    {
        "ProxyBridge_CLI.exe",
        "ProxyBridgeCore.dll",
        "WinDivert.dll",
        "WinDivert64.sys",
    };

    /// <summary>Extracts the embedded ProxyBridge files if needed, and returns the path to ProxyBridge_CLI.exe.</summary>
    public static string ExtractIfNeeded()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Proxion", "bin");
        Directory.CreateDirectory(dir);

        var assembly = Assembly.GetExecutingAssembly();
        foreach (var fileName in FileNames)
        {
            var targetPath = Path.Combine(dir, fileName);
            if (File.Exists(targetPath))
            {
                continue;
            }

            using var resourceStream = assembly.GetManifestResourceStream(ResourcePrefix + fileName)
                ?? throw new InvalidOperationException($"Embedded resource not found: {ResourcePrefix}{fileName}");
            using var fileStream = File.Create(targetPath);
            resourceStream.CopyTo(fileStream);
        }

        return Path.Combine(dir, "ProxyBridge_CLI.exe");
    }
}
