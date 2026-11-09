using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SnippingTool.Core;
using CaptureMode = SnippingTool.Core.CaptureMode;

namespace SnippingTool.UI;

public partial class FloatingToolbarWindow : Window
{
    public event EventHandler<CaptureMode>? CaptureRequested;
    public event EventHandler? HistoryRequested;
    public event EventHandler? HiddenRequested;

    public FloatingToolbarWindow()
    {
        InitializeComponent();
        TbRect.Click += (_, _) => Capture(CaptureMode.Rectangle);
        TbFreeform.Click += (_, _) => Capture(CaptureMode.Freeform);
        TbWindow.Click += (_, _) => Capture(CaptureMode.Window);
        TbFull.Click += (_, _) => Capture(CaptureMode.Fullscreen);
        TbMonitor.Click += (_, _) => Capture(CaptureMode.Monitor);
        TbHistory.Click += (_, _) => HistoryRequested?.Invoke(this, EventArgs.Empty);
        TbClose.Click += (_, _) => { Hide(); HiddenRequested?.Invoke(this, EventArgs.Empty); };
        Loaded += (_, _) =>
        {
            var work = SystemParameters.WorkArea;
            Left = work.Left + (work.Width - ActualWidth) / 2;
            Top = work.Top + 10;
            ThemeManager.ApplyWindowChrome(this);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Hide(); HiddenRequested?.Invoke(this, EventArgs.Empty); }
        };
    }

    private void Capture(CaptureMode mode)
    {
        foreach (var toggle in new[] { TbRect, TbFreeform, TbWindow, TbFull, TbMonitor })
            toggle.IsChecked = false;
        Hide();
        CaptureRequested?.Invoke(this, mode);
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }
}
