using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnippingTool.Core;
using SnippingTool.Imaging;
using SnippingTool.Storage;
using CaptureMode = SnippingTool.Core.CaptureMode;

namespace SnippingTool.UI;

public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private readonly DispatcherTimer _hintTimer;
    private HwndSource? _source;
    private bool _loading = true;

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
        // The footer reflects wherever THIS copy of the app actually lives, so it matches
        // the folder the user installed (or copied) the app to — not a hard-coded path.
        FooterText.Text = $"Portable · data stays in {AppPaths.Data}";
        _hintTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
        _hintTimer.Tick += (_, _) => { _hintTimer.Stop(); HintBar.Visibility = Visibility.Collapsed; };

        ModeRect.Click += (_, _) => Launch(CaptureMode.Rectangle);
        ModeFreeform.Click += (_, _) => Launch(CaptureMode.Freeform);
        ModeWindow.Click += (_, _) => Launch(CaptureMode.Window);
        ModeFullscreen.Click += (_, _) => Launch(CaptureMode.Fullscreen);
        ModeMonitor.Click += (_, _) => Launch(CaptureMode.Monitor);
        NewSnipButton.Click += (_, _) => Launch(_host.Settings.DefaultMode);
        RecordButton.Click += (_, _) => _host.BeginRecordingFromUi();
        ViewHistoryButton.Click += (_, _) => _host.ShowHistory();
        HistoryButton.Click += (_, _) => _host.ShowHistory();
        SettingsButton.Click += (_, _) => _host.ShowSettings();
        TrayButton.Click += (_, _) => HideToTray();
        ThemeButton.Click += (_, _) => ToggleTheme();
        Delay0.Click += (_, _) => SetDelay(0);
        Delay3.Click += (_, _) => SetDelay(3);
        Delay5.Click += (_, _) => SetDelay(5);
        Delay10.Click += (_, _) => SetDelay(10);

        AutoCopyToggle.Checked += (_, _) => SetOption(() => _host.Settings.AutoCopy = true);
        AutoCopyToggle.Unchecked += (_, _) => SetOption(() => _host.Settings.AutoCopy = false);
        AutoSaveToggle.Checked += (_, _) => SetOption(() => _host.Settings.AutoSave = true);
        AutoSaveToggle.Unchecked += (_, _) => SetOption(() => _host.Settings.AutoSave = false);
        OpenEditorToggle.Checked += (_, _) => SetOption(() => _host.Settings.OpenEditorAfterCapture = true);
        OpenEditorToggle.Unchecked += (_, _) => SetOption(() => _host.Settings.OpenEditorAfterCapture = false);

        Loaded += (_, _) =>
        {
            ThemeManager.ApplyWindowChrome(this);
            _host.History.Changed += OnHistoryChanged;
            _host.SettingsChanged += OnSettingsChanged;
            RefreshAll();
            _loading = false;
        };
        StateChanged += (_, _) =>
            MaximizeButton.Style = WindowState == WindowState.Maximized
                ? (System.Windows.Style)TryFindResource("TitleBarRestoreButton")
                : (System.Windows.Style)TryFindResource("TitleBarMaxButton");
        Closed += (_, _) =>
        {
            _host.History.Changed -= OnHistoryChanged;
            _host.SettingsChanged -= OnSettingsChanged;
            _source?.RemoveHook(WndProc);
            _source = null;
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeManager.ApplyWindowChrome(this);
        var hwnd = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
        _host.AttachHotkeys(this);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (SingleInstance.ActivateMessage != 0 && (uint)msg == SingleInstance.ActivateMessage)
        {
            _host.ShowMain();
            handled = true;
        }
        else if (msg == NativeMethods.WM_HOTKEY)
        {
            if (_host.Hotkeys.HandleHotkey(wParam.ToInt32()))
                handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            _host.ShowSettings();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && HintBar.Visibility == Visibility.Visible)
        {
            HintBar.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
    }

    private void Launch(CaptureMode mode)
    {
        _ = _host.CaptureAsync(mode);
    }

    private void HideForCapture()
    {
        if (IsVisible) Hide();
    }

    private void SetDelay(int seconds)
    {
        if (_host.Settings.DelaySeconds == seconds)
        {
            UpdateDelayButtons();
            return;
        }
        _host.Settings.DelaySeconds = seconds;
        SettingsService.Save(_host.Settings);
        UpdateDelayButtons();
        ShowHint(seconds == 0 ? "No delay" : $"Capture starts in {seconds} seconds");
    }

    private void SetOption(Action apply)
    {
        if (_loading) return;
        apply();
        SettingsService.Save(_host.Settings);
        _host.RaiseSettingsChanged();
    }

    private void ToggleTheme()
    {
        var next = ThemeManager.IsDark ? AppTheme.Light : AppTheme.Dark;
        _host.Settings.Theme = next;
        SettingsService.Save(_host.Settings);
        ThemeManager.Apply(next);
        ThemeGlyph.Text = ThemeManager.IsDark ? "\uE706" : "\uE708";
        _host.RaiseSettingsChanged();
    }

    private void HideToTray()
    {
        Hide();
        if (_host.Settings.ShowNotifications)
            ToastWindow.Show("Shutio is still running. Use the tray icon or your global shortcuts.",
                "Show", _host.ShowMain);
    }

    private void OnHistoryChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(RefreshRecent);

    private void OnSettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(RefreshAll);

    public void RefreshAll()
    {
        _loading = true;
        AutoCopyToggle.IsChecked = _host.Settings.AutoCopy;
        AutoSaveToggle.IsChecked = _host.Settings.AutoSave;
        OpenEditorToggle.IsChecked = _host.Settings.OpenEditorAfterCapture;
        ThemeGlyph.Text = ThemeManager.IsDark ? "\uE706" : "\uE708";
        UpdateDelayButtons();
        UpdateShortcutLabels();
        RefreshRecent();
        HotkeyStateText.Text = _host.HotkeyStatus;
        HotkeyDot.Fill = (Brush)FindResource(_host.Hotkeys.AnyRegistered ? "SuccessBrush" : "DangerBrush");
        TrayHintText.Text = _host.Settings.CloseToTray
            ? "Closing this window keeps the app in the notification area so shortcuts keep working."
            : "The app can keep running in the notification area so shortcuts work while it is minimized. Enable this in Settings.";
        _loading = false;
    }

    private void UpdateDelayButtons()
    {
        int delay = _host.Settings.DelaySeconds;
        Delay0.IsChecked = delay == 0;
        Delay3.IsChecked = delay == 3;
        Delay5.IsChecked = delay == 5;
        Delay10.IsChecked = delay == 10;
    }

    private void UpdateShortcutLabels()
    {
        RectShortcut.Text = GestureLabel(HotkeyDefaults.CaptureRect);
        FreeformShortcut.Text = GestureLabel(HotkeyDefaults.CaptureFreeform);
        WindowShortcut.Text = GestureLabel(HotkeyDefaults.CaptureWindow);
        FullShortcut.Text = GestureLabel(HotkeyDefaults.CaptureFullscreen);
        MonitorShortcut.Text = GestureLabel(HotkeyDefaults.CaptureMonitor);
        HotkeySummaryText.Text = $"{GestureLabel(HotkeyDefaults.CaptureRect)} for a rectangular snip · {GestureLabel(HotkeyDefaults.CaptureFreeform)} for freeform · {GestureLabel(HotkeyDefaults.CaptureWindow)} for a window";
    }

    private string GestureLabel(string action) =>
        _host.Settings.Hotkeys.TryGetValue(action, out var gesture) && !string.IsNullOrWhiteSpace(gesture)
            ? gesture.Replace(" + ", " + ")
            : "Not set";

    public void RefreshRecent()
    {
        RecentHost.Children.Clear();
        var recent = _host.History.Recent(3);
        RecentCountText.Text = _host.History.Count == 1 ? "1 snip" : $"{_host.History.Count} snips";
        if (recent.Count == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(14, 10, 14, 10) };
            empty.Children.Add(new TextBlock
            {
                Text = "Nothing captured yet. Take your first snip with the buttons above.",
                FontSize = 12.5
            });
            empty.Children.Add(new TextBlock
            {
                Text = "Snips are copied, saved, and listed here automatically.",
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0)
            });
            ((TextBlock)empty.Children[1]).SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            RecentHost.Children.Add(empty);
            return;
        }

        foreach (var entry in recent)
            RecentHost.Children.Add(BuildRecentRow(entry));
    }

    private Border BuildRecentRow(SnipEntry entry)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 2),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent
        };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "SurfaceHoverBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                _host.OpenEditorForEntry(entry);
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var thumbHost = new Border
        {
            Width = 104,
            Height = 64,
            CornerRadius = new CornerRadius(7),
            ClipToBounds = true,
            Background = Brushes.Transparent
        };
        thumbHost.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        try
        {
            if (!string.IsNullOrEmpty(entry.ThumbnailPath) && File.Exists(entry.ThumbnailPath))
                thumbHost.Child = new Image
                {
                    Source = BitmapUtils.LoadBitmapSource(entry.ThumbnailPath),
                    Stretch = Stretch.UniformToFill
                };
        }
        catch { }
        grid.Children.Add(thumbHost);

        var text = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = entry.FileName, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = new TextBlock { Text = $"{entry.ModeLabel} · {entry.Dimensions} · {entry.DisplayTime}", FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0) };
        meta.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        var path = new TextBlock { Text = entry.FilePath, FontSize = 10.5, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        path.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        text.Children.Add(name);
        text.Children.Add(meta);
        text.Children.Add(path);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var open = new Button
        {
            Content = "Edit",
            Style = (Style)FindResource("SecondaryButton"),
            Padding = new Thickness(12, 5, 12, 5),
            VerticalAlignment = VerticalAlignment.Center
        };
        open.Click += (_, _) => _host.OpenEditorForEntry(entry);
        Grid.SetColumn(open, 2);
        grid.Children.Add(open);

        row.Child = grid;
        return row;
    }

    private void ShowHint(string text)
    {
        HintText.Text = text;
        HintBar.Visibility = Visibility.Visible;
        HintBar.Opacity = 0;
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140));
        HintBar.BeginAnimation(OpacityProperty, fade);
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (e.ClickCount == 1 && e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (_host.Settings.MinimizeToTray)
        {
            HideToTray();
            return;
        }
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => _host.RequestExit();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_host.IsExiting && _host.Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            if (_host.Settings.ShowNotifications)
                ToastWindow.Show("Shutio is still running in the notification area.", "Exit", _host.RequestExit);
            return;
        }
        if (!_host.IsExiting)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(_host.RequestExit);
        }
        base.OnClosing(e);
    }
}
