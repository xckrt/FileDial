using System.Drawing;
using System.Drawing.Drawing2D;
using FileDial.App.Interop;
using Forms = System.Windows.Forms;

namespace FileDial.App.Tray;

/// <summary>Значок в области уведомлений: меню «Настройки», «История», «Справка», «Выход»; двойной клик — настройки.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _image;
    private readonly Forms.ToolStripMenuItem _header;

    public TrayIcon(string hotkey, Action openSettings, Action openHistory, Action openHelp, Action exit)
    {
        _image = CreateIcon();
        var menu = new Forms.ContextMenuStrip();
        _header = new Forms.ToolStripMenuItem { Enabled = false };
        menu.Items.Add(_header);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Настройки…", null, (_, _) => openSettings())
        {
            Font = new Font(menu.Font, FontStyle.Bold), // действие по двойному клику
        });
        menu.Items.Add("История…", null, (_, _) => openHistory());
        menu.Items.Add("Справка (README)", null, (_, _) => openHelp());
        menu.Items.Add("Выход", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => openSettings();
        SetHotkey(hotkey);
    }

    /// <summary>Обновить подписи после смены горячей клавиши (null — клавиша не назначена).</summary>
    public void SetHotkey(string? hotkey)
    {
        _header.Text = hotkey is null ? "FileDial — клавиша не назначена" : $"FileDial — {hotkey}";
        _icon.Text = hotkey is null
            ? "FileDial — назначьте горячую клавишу в настройках"
            : $"FileDial — выделите файл и нажмите {hotkey}";
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
