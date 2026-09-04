using System.Reflection;

namespace Proxion.Windows;

/// <summary>
/// Access to Proxion's own embedded assets (currently just its icon). Returns raw bytes
/// rather than a System.Drawing.Icon, so this library doesn't need to depend on WinForms/GDI+ -
/// callers that do (the UI projects) construct whatever they need from the bytes.
/// </summary>
public static class EmbeddedAssets
{
    private const string IconResourceName = "Proxion.Windows.Assets.proxion.ico";

    public static byte[] GetIconBytes()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(IconResourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {IconResourceName}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
