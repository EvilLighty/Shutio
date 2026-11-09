using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SnippingTool.Core;
using CaptureMode = SnippingTool.Core.CaptureMode;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Point = System.Windows.Point;

namespace SnippingTool.Capture;

public partial class CaptureOverlayWindow : Window
{
    private readonly Bitmap _frozen;
    private readonly Rectangle _virtualScreen;
    private readonly List<WindowInfo> _windows;
    private double _scale = 1.0;
    private CaptureMode _mode;
    private bool _committed;
    private Int32Rect _lastCrop;

    public CaptureResult? SelectedResult { get; private set; }
    public bool RegionOnly { get; }

    public CaptureOverlayWindow(Bitmap frozen, Rectangle virtualScreen, CaptureMode mode, bool regionOnly = false)
    {
        InitializeComponent();
        _frozen = frozen;
        _virtualScreen = virtualScreen;
        _mode = mode;
        RegionOnly = regionOnly;
        _windows = CaptureService.EnumerateWindows();
        Surface.RegionOnly = regionOnly;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
        Surface.Committed += OnCommitted;
        Surface.Cancelled += (_, _) => CancelSelection();
        Surface.CursorMoved += OnCursorMoved;
        ToolRect.Click += (_, _) => SetMode(CaptureMode.Rectangle);
        ToolFreeform.Click += (_, _) => SetMode(CaptureMode.Freeform);
        ToolWindow.Click += (_, _) => SetMode(CaptureMode.Window);
        ToolFullscreen.Click += (_, _) => SetMode(CaptureMode.Fullscreen);
        ToolMonitor.Click += (_, _) => SetMode(CaptureMode.Monitor);
        CloseButton.Click += (_, _) => CancelSelection();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int corners = 1; // DWMWCP_DONOTROUND
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
            int dark = 1;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }
        catch { }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            uint dpi = NativeMethods.GetDpiForWindow(hwnd);
            _scale = dpi > 0 ? dpi / 96.0 : 1.0;

            Left = _virtualScreen.Left / _scale;
            Top = _virtualScreen.Top / _scale;
            Width = _virtualScreen.Width / _scale;
            Height = _virtualScreen.Height / _scale;
            UpdateLayout();
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
                _virtualScreen.Left, _virtualScreen.Top, _virtualScreen.Width, _virtualScreen.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

            Surface.SetCapturedDesktop(_frozen, _virtualScreen, _scale, _mode, _windows);
            SetMode(_mode);
            PositionChrome();
            UpdateLayout();
            PositionChrome();
            Activate();
            Focus();
            Keyboard.Focus(this);
            Logger.Info($"Capture overlay shown over {_virtualScreen.Width}x{_virtualScreen.Height} at {_virtualScreen.X},{_virtualScreen.Y} (dpi {dpi}).");
        }
        catch (Exception ex)
        {
            Logger.Error("Could not initialise capture overlay", ex);
            Close();
        }
    }

    private void PositionChrome()
    {
        var cursorScreen = new Point(_virtualScreen.Left + _virtualScreen.Width / 2.0, _virtualScreen.Top + 60);
        if (NativeMethods.GetCursorPosition(out var cursor)) cursorScreen = new Point(cursor.X, cursor.Y);
        var monitor = CaptureService.MonitorAt(new System.Drawing.Point((int)cursorScreen.X, (int)cursorScreen.Y));

        double panelWidth = ToolbarPanel.ActualWidth > 0 ? ToolbarPanel.ActualWidth : ToolbarPanel.DesiredSize.Width;
        if (panelWidth <= 0) panelWidth = 330;
        double monitorCenterLocal = (monitor.Bounds.Left - _virtualScreen.Left + monitor.Bounds.Width / 2.0) / _scale;
        double topLocal = (monitor.Bounds.Top - _virtualScreen.Top) / _scale + 16;
        double left = Math.Clamp(monitorCenterLocal - panelWidth / 2, 8, Math.Max(8, ActualWidth - panelWidth - 8));
        ToolbarPanel.Margin = new Thickness(left, Math.Max(8, topLocal), 0, 0);

        double bottomGap = (_virtualScreen.Bottom - monitor.Bounds.Bottom) / _scale + 26;
        HintPanel.Margin = new Thickness(0, 0, 0, Math.Max(20, bottomGap));
    }

    private void SetMode(CaptureMode mode)
    {
        _mode = mode;
        Surface.Mode = mode;
        ToolRect.IsChecked = mode == CaptureMode.Rectangle;
        ToolFreeform.IsChecked = mode == CaptureMode.Freeform;
        ToolWindow.IsChecked = mode == CaptureMode.Window;
        ToolFullscreen.IsChecked = mode == CaptureMode.Fullscreen;
        ToolMonitor.IsChecked = mode == CaptureMode.Monitor;
        HintText.Text = mode switch
        {
            CaptureMode.Rectangle => "Drag to select an area",
            CaptureMode.Freeform => "Draw around the area you want to capture",
            CaptureMode.Window => "Click the window you want to capture",
            CaptureMode.Fullscreen => "Click to capture the entire desktop",
            CaptureMode.Monitor => "Click the display you want to capture",
            _ => "Select an area"
        };
        HideLoupe();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CancelSelection();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                SetMode(CaptureMode.Rectangle);
                e.Handled = true;
                break;
            case Key.D2:
            case Key.NumPad2:
                SetMode(CaptureMode.Freeform);
                e.Handled = true;
                break;
            case Key.D3:
            case Key.NumPad3:
                SetMode(CaptureMode.Window);
                e.Handled = true;
                break;
            case Key.D4:
            case Key.NumPad4:
                SetMode(CaptureMode.Fullscreen);
                e.Handled = true;
                break;
            case Key.D5:
            case Key.NumPad5:
                SetMode(CaptureMode.Monitor);
                e.Handled = true;
                break;
        }
    }

    private void OnCommitted(object? sender, CaptureResult e)
    {
        if (_committed) return;
        _committed = true;
        SelectedResult = e;
        HideLoupe();
        Close();
    }

    private void CancelSelection()
    {
        if (_committed) return;
        _committed = true;
        SelectedResult = null;
        HideLoupe();
        Close();
    }

    private void OnCursorMoved(object? sender, (Point ScreenPoint, bool Show) e)
    {
        if (!e.Show)
        {
            HideLoupe();
            return;
        }
        var source = Surface.FrozenSource;
        if (source == null) return;

        int sx = (int)Math.Round(e.ScreenPoint.X) - _virtualScreen.Left;
        int sy = (int)Math.Round(e.ScreenPoint.Y) - _virtualScreen.Top;
        const int sample = 16;
        int left = Math.Clamp(sx - sample / 2, 0, Math.Max(0, _frozen.Width - sample));
        int top = Math.Clamp(sy - sample / 2, 0, Math.Max(0, _frozen.Height - sample));
        int width = Math.Min(sample, _frozen.Width - left);
        int height = Math.Min(sample, _frozen.Height - top);
        if (width <= 0 || height <= 0) return;

        var crop = new Int32Rect(left, top, width, height);
        if (crop != _lastCrop)
        {
            try
            {
                var cropped = new CroppedBitmap(source, crop);
                cropped.Freeze();
                LoupeImage.Source = cropped;
                _lastCrop = crop;
            }
            catch { }
        }

        double localX = (e.ScreenPoint.X - _virtualScreen.Left) / _scale;
        double localY = (e.ScreenPoint.Y - _virtualScreen.Top) / _scale;
        double px = localX + 26;
        double py = localY + 26;
        if (px + 120 > ActualWidth) px = localX - 126;
        if (py + 140 > ActualHeight) py = localY - 134;
        px = Math.Clamp(px, 4, Math.Max(4, ActualWidth - 112));
        py = Math.Clamp(py, 4, Math.Max(4, ActualHeight - 132));
        Canvas.SetLeft(LoupeBorder, px);
        Canvas.SetTop(LoupeBorder, py);
        Canvas.SetLeft(LoupeLabel, px);
        Canvas.SetTop(LoupeLabel, py + 112);
        LoupeCoords.Text = $"{sx + _virtualScreen.Left}, {sy + _virtualScreen.Top}";
        LoupeBorder.Visibility = Visibility.Visible;
        LoupeLabel.Visibility = Visibility.Visible;
    }

    private void HideLoupe()
    {
        LoupeBorder.Visibility = Visibility.Collapsed;
        LoupeLabel.Visibility = Visibility.Collapsed;
    }
}

