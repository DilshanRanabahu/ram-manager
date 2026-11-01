using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using RamManager.Helpers;
using static RamManager.Native.NativeMethods;
using Forms = System.Windows.Forms;

namespace RamManager.Services;

/// <summary>System tray icon that shows the current memory load as a number.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _statusItem;
    private Icon? _currentIcon;
    private int _lastPercent = -1;

    public event Action? OpenRequested;
    public event Action? QuickCleanRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        var menu = new Forms.ContextMenuStrip
        {
            Renderer = new Forms.ToolStripProfessionalRenderer(new DarkColorTable()),
            ShowImageMargin = false,
            Font = new Font("Segoe UI", 9.5f),
        };

        _statusItem = new Forms.ToolStripMenuItem("Memory: —") { Enabled = false };
        var open = new Forms.ToolStripMenuItem("Open RAM Manager") { Font = new Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold) };
        var clean = new Forms.ToolStripMenuItem("Quick clean");
        var exit = new Forms.ToolStripMenuItem("Exit");

        open.Click += (_, _) => OpenRequested?.Invoke();
        clean.Click += (_, _) => QuickCleanRequested?.Invoke();
        exit.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.AddRange([_statusItem, new Forms.ToolStripSeparator(), open, clean, new Forms.ToolStripSeparator(), exit]);
        foreach (Forms.ToolStripItem item in menu.Items)
        {
            item.ForeColor = Color.FromArgb(242, 242, 246);
            item.Padding = new Forms.Padding(4, 3, 4, 3);
        }

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "RAM Manager",
            ContextMenuStrip = menu,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke();
        };
        _notifyIcon.BalloonTipClicked += (_, _) =>
        {
            var click = _notificationClick ?? OpenRequested;
            _notificationClick = null;
            click?.Invoke();
        };

        SetIcon(0);
        _notifyIcon.Visible = true;
    }

    public void Update(MemorySnapshot memory)
    {
        int percent = (int)Math.Round(memory.LoadPercent);
        if (percent != _lastPercent) SetIcon(percent);

        string text = $"RAM Manager\nMemory {percent}% · {Format.Gb(memory.Available)} available";
        _notifyIcon.Text = text.Length > 127 ? text[..127] : text;
        _statusItem.Text = $"Memory: {percent}% used  ·  {Format.Gb(memory.Available)} free";
    }

    public void ShowBalloon(string title, string text) => ShowNotification(title, text, Forms.ToolTipIcon.Info, null);

    /// <summary>Shows a Windows notification. <paramref name="onClick"/> runs if the user clicks it.</summary>
    public void ShowNotification(string title, string text, Forms.ToolTipIcon icon, Action? onClick)
    {
        _notificationClick = onClick;
        _notifyIcon.ShowBalloonTip(5000, title, text, icon);
    }

    private Action? _notificationClick;

    private void SetIcon(int percent)
    {
        _lastPercent = percent;
        var old = _currentIcon;
        _currentIcon = Render(percent);
        _notifyIcon.Icon = _currentIcon;
        old?.Dispose();
    }

    private static Icon Render(int percent)
    {
        int size = Forms.SystemInformation.SmallIconSize.Width;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            Color background = percent < 60 ? Color.FromArgb(52, 211, 153)
                : percent < 80 ? Color.FromArgb(242, 169, 59)
                : Color.FromArgb(242, 84, 91);

            using (var path = RoundedRectangle(new RectangleF(0, 0, size - 1, size - 1), size * 0.24f))
            using (var brush = new SolidBrush(background))
            {
                g.FillPath(brush, path);
            }

            string text = Math.Clamp(percent, 0, 99).ToString();
            using var font = new Font("Segoe UI", size * (text.Length > 1 ? 0.58f : 0.68f), System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.FromArgb(16, 16, 20));
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, textBrush, new RectangleF(0, 0.5f, size, size), format);
        }

        nint handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRectangle(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
    }

    private sealed class DarkColorTable : Forms.ProfessionalColorTable
    {
        private static readonly Color Background = Color.FromArgb(31, 31, 37);
        private static readonly Color Hover = Color.FromArgb(48, 48, 58);
        private static readonly Color Border = Color.FromArgb(52, 52, 62);

        public override Color ToolStripDropDownBackground => Background;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
    }
}
