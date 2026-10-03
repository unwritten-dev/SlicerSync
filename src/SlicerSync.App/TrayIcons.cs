using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SlicerSync.App;

public enum TrayState { Ok, Running, Error, NotConfigured }

/// <summary>App-Icon plus farbiger Statuspunkt (grün = ok, blau = läuft, rot = Fehler, grau = nicht eingerichtet).</summary>
public sealed class TrayIcons : IDisposable
{
    private readonly Dictionary<TrayState, Icon> _icons = new();

    public TrayIcons()
    {
        AppIcon = LoadAppIcon();
        var size = SystemInformation.SmallIconSize.Width;
        using var baseBitmap = new Icon(AppIcon, new Size(size * 2, size * 2)).ToBitmap();
        _icons[TrayState.Ok] = Overlay(baseBitmap, size, Color.FromArgb(22, 163, 74));
        _icons[TrayState.Running] = Overlay(baseBitmap, size, Color.FromArgb(250, 204, 21));
        _icons[TrayState.Error] = Overlay(baseBitmap, size, Color.FromArgb(220, 38, 38));
        _icons[TrayState.NotConfigured] = Overlay(baseBitmap, size, Color.FromArgb(148, 163, 184));
    }

    public Icon AppIcon { get; }

    public Icon this[TrayState state] => _icons[state];

    public static Icon LoadAppIcon()
    {
        using var stream = typeof(TrayIcons).Assembly.GetManifestResourceStream("SlicerSync.ico");
        return stream != null ? new Icon(stream) : SystemIcons.Application;
    }

    private static Icon Overlay(Bitmap source, int size, Color dot)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, 0, 0, size, size);
            var d = size * 0.5f;
            var rect = new RectangleF(size - d - 0.5f, size - d - 0.5f, d, d);
            using var brush = new SolidBrush(dot);
            using var pen = new Pen(Color.White, Math.Max(1f, size / 16f));
            g.FillEllipse(brush, rect);
            g.DrawEllipse(pen, rect);
        }
        var handle = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        foreach (var icon in _icons.Values)
            icon.Dispose();
    }
}
