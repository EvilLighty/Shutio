using System.Drawing;
using System.Windows.Forms;
using SnippingTool.Core;

namespace SnippingTool.UI;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon? _ownedIcon;
    private bool _disposed;

    public event EventHandler<CaptureMode>? CaptureRequested;
    public event EventHandler? ShowRequested;
    public event EventHandler? HistoryRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? RecordingRequested;
    public event EventHandler? ExitRequested;

    public TrayIcon()
    {
        Icon icon;
        try
        {
            string path = Path.Combine(AppPaths.Root, "Assets", "app.ico");
            if (File.Exists(path))
            {
                var loaded = new Icon(path);
                _ownedIcon = loaded;
                icon = loaded;
            }
            else
            {
                var extracted = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "");
                _ownedIcon = extracted;
                icon = extracted ?? SystemIcons.Application;
            }
        }
        catch
        {
            icon = SystemIcons.Application;
        }

        var menu = new ContextMenuStrip
        {
            Font = new Font("Segoe UI", 9f),
            ShowImageMargin = false,
            RenderMode = ToolStripRenderMode.System
        };
        Add(menu, "Rectangular snip", () => CaptureRequested?.Invoke(this, CaptureMode.Rectangle));
        Add(menu, "Freeform snip", () => CaptureRequested?.Invoke(this, CaptureMode.Freeform));
        Add(menu, "Window snip", () => CaptureRequested?.Invoke(this, CaptureMode.Window));
        Add(menu, "Full desktop snip", () => CaptureRequested?.Invoke(this, CaptureMode.Fullscreen));
        Add(menu, "Single display snip", () => CaptureRequested?.Invoke(this, CaptureMode.Monitor));
        menu.Items.Add(new ToolStripSeparator());
        Add(menu, "Record screen…", () => RecordingRequested?.Invoke(this, EventArgs.Empty));
        Add(menu, "Snip history", () => HistoryRequested?.Invoke(this, EventArgs.Empty));
        Add(menu, "Settings", () => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        Add(menu, "Show Shutio", () => ShowRequested?.Invoke(this, EventArgs.Empty));
        Add(menu, "Exit", () => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new NotifyIcon
        {
            Icon = icon,
            Text = "Shutio",
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    public void UpdateTooltip(string text)
    {
        try
        {
            _icon.Text = text.Length > 120 ? text[..120] : text;
        }
        catch { }
    }

    public void ShowBalloon(string title, string message)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = message;
            _icon.ShowBalloonTip(4000);
        }
        catch { }
    }

    private static void Add(ContextMenuStrip menu, string label, Action action)
    {
        var item = new ToolStripMenuItem(label);
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
        catch { }
        try { _ownedIcon?.Dispose(); } catch { }
    }
}
