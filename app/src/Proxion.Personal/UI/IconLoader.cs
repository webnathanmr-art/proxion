using System.Drawing;
using Proxion.Windows;

namespace Proxion.Personal.UI;

/// <summary>Loads Proxion's own icon (embedded in Proxion.Windows) as a System.Drawing.Icon.</summary>
internal static class IconLoader
{
    public static Icon Load()
    {
        using var stream = new MemoryStream(EmbeddedAssets.GetIconBytes());
        return new Icon(stream);
    }
}
