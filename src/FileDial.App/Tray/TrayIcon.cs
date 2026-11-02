using System.Drawing;
using System.Drawing.Drawing2D;
using FileDial.App.Interop;
using Forms = System.Windows.Forms;

namespace FileDial.App.Tray;

/// <summary>Значок в области уведомлений с меню «Справка» и «Выход».</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _image;

    public TrayIcon(string hotkey, Action openHelp, Action exit)
    {
        _image = CreateIcon();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem($"FileDial — {hotkey}") { Enabled = false });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Справка (README)", null, (_, _) => openHelp());
        menu.Items.Add("Выход", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = $"FileDial — выделите видео и нажмите {hotkey}",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    /// <summary>Рисуем значок кодом, чтобы не возить с собой .ico: кольцо с выделенным сектором.</summary>
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(0x15, 0x16, 0x1A));
            using var ring = new Pen(Color.FromArgb(0x2B, 0x2D, 0x34), 6);
            using var accent = new Pen(Color.FromArgb(0xC8, 0xF2, 0x50), 6);
            g.FillEllipse(bg, 1, 1, 30, 30);
            g.DrawEllipse(ring, 6, 6, 20, 20);
            g.DrawArc(accent, 6, 6, 20, 20, -112.5f, 45f);
        }

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
    }
}
