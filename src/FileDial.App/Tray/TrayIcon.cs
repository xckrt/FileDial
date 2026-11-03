using System.Drawing;
using System.Drawing.Drawing2D;
using FileDial.App.Interop;
using FileDial.Core.Localization;
using Forms = System.Windows.Forms;

namespace FileDial.App.Tray;

/// <summary>Значок в области уведомлений: меню «Настройки», «История», «Справка», «Выход»; двойной клик — настройки.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _image;
    private readonly Forms.ToolStripMenuItem _header;
    private readonly Forms.ToolStripMenuItem _settings;
    private readonly Forms.ToolStripMenuItem _history;
    private readonly Forms.ToolStripMenuItem _help;
    private readonly Forms.ToolStripMenuItem _exit;
    private string? _hotkey;

    public TrayIcon(string hotkey, Action openSettings, Action openHistory, Action openHelp, Action exit)
    {
        _image = CreateIcon();
        var menu = new Forms.ContextMenuStrip();
        _header = new Forms.ToolStripMenuItem { Enabled = false };
        menu.Items.Add(_header);
        menu.Items.Add(new Forms.ToolStripSeparator());
        _settings = new Forms.ToolStripMenuItem("", null, (_, _) => openSettings())
        {
            Font = new Font(menu.Font, FontStyle.Bold), // действие по двойному клику
        };
        _history = new Forms.ToolStripMenuItem("", null, (_, _) => openHistory());
        _help = new Forms.ToolStripMenuItem("", null, (_, _) => openHelp());
        _exit = new Forms.ToolStripMenuItem("", null, (_, _) => exit());
        menu.Items.AddRange([_settings, _history, _help, _exit]);

        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => openSettings();
        SetHotkey(hotkey);
        Lang.Changed += ApplyTexts;
    }

    /// <summary>Подписи меню и подсказки на текущем языке.</summary>
    private void ApplyTexts()
    {
        _settings.Text = Lang.T.UiTraySettings;
        _history.Text = Lang.T.UiTrayHistory;
        _help.Text = Lang.T.UiTrayHelp;
        _exit.Text = Lang.T.UiTrayExit;
        _header.Text = _hotkey is null ? Lang.T.UiTrayNoHotkey : $"FileDial — {_hotkey}";
        _icon.Text = _hotkey is null ? Lang.T.UiTrayTipNoHotkey : Lang.T.UiTrayTip(_hotkey);
    }

    private Action? _balloonClick;

    /// <summary>Всплывающее уведомление у значка; клик по нему выполняет действие.</summary>
    public void Notify(string title, string text, Action onClick)
    {
        _balloonClick = onClick;
        _icon.BalloonTipClicked -= OnBalloonClicked;
        _icon.BalloonTipClicked += OnBalloonClicked;
        _icon.ShowBalloonTip(10_000, title, text, Forms.ToolTipIcon.Info);
    }

    private void OnBalloonClicked(object? sender, EventArgs e) => _balloonClick?.Invoke();

    /// <summary>Обновить подписи после смены горячей клавиши (null — клавиша не назначена).</summary>
    public void SetHotkey(string? hotkey)
    {
        _hotkey = hotkey;
        ApplyTexts();
    }

    /// <summary>
    /// Значок из ресурсов (FileDial.ico, несколько размеров) — нужного размера для текущего DPI.
    /// Если ресурса нет, рисуем тот же знак кодом: кольцо с выделенным сектором.
    /// </summary>
    private static Icon CreateIcon()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("FileDial.ico");
        if (stream is not null)
            return new Icon(stream, Forms.SystemInformation.SmallIconSize);

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
        Lang.Changed -= ApplyTexts;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
    }
}
