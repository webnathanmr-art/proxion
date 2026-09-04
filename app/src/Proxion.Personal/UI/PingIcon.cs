using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Proxion.Personal.UI;

/// <summary>
/// A tray icon showing either the current ping (in ms) to the proxy as a number, or -
/// when a ping isn't available - the regular cat icon. Icons built from a GDI bitmap via
/// <see cref="Bitmap.GetHicon"/> own a native HICON that .NET does not free automatically
/// (unlike an Icon loaded from a resource/stream), so this wraps that handle and calls
/// DestroyIcon on Dispose to avoid leaking one every refresh.
/// </summary>
internal sealed class PingIcon : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public Icon Icon { get; }

    private readonly IntPtr _ownedHandle;

    private PingIcon(Icon icon, IntPtr ownedHandle)
    {
        Icon = icon;
        _ownedHandle = ownedHandle;
    }

    public static PingIcon Cat() => new(IconLoader.Load(), IntPtr.Zero);

    public static PingIcon FromPingMilliseconds(long pingMs)
    {
        using var bitmap = Render(pingMs);
        var handle = bitmap.GetHicon();
        return new PingIcon(Icon.FromHandle(handle), handle);
    }

    private static Bitmap Render(long pingMs)
    {
        var text = pingMs >= 1000 ? "999" : pingMs.ToString();
        var color = pingMs < 80 ? Color.FromArgb(90, 220, 120)
            : pingMs < 150 ? Color.FromArgb(240, 200, 60)
            : Color.FromArgb(230, 90, 90);

        var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.Clear(Color.Transparent);

        using (var bgPath = RoundedRect(new Rectangle(0, 0, 32, 32), 8))
        using (var bgBrush = new SolidBrush(Color.FromArgb(235, 25, 25, 28)))
        {
            g.FillPath(bgBrush, bgPath);
        }

        var fontSize = text.Length >= 3 ? 12f : 15f;
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, brush, (32 - size.Width) / 2f, (32 - size.Height) / 2f - 1f);

        return bitmap;
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        Icon.Dispose();
        if (_ownedHandle != IntPtr.Zero)
        {
            DestroyIcon(_ownedHandle);
        }
    }
}
