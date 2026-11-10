using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnippingTool.Core;
using SnippingTool.Editor;
using SnippingTool.Imaging;
using SnippingTool.Recording;
using SnippingTool.Storage;
using CaptureMode = SnippingTool.Core.CaptureMode;

namespace SnippingTool.UI;

public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private readonly EditorPane _editor;
    private readonly DispatcherTimer _hintTimer;
    private readonly DispatcherTimer _recTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _glassDebounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
    private HwndSource? _source;
    private bool _loading = true;
    private bool _glassBusy;
    private DateTime _lastGlassAt = DateTime.MinValue;
    // True while our own Hide->Show sampling is in flight, so the events that Show()
    // raises (Activated / SizeChanged / LocationChanged) can't schedule the next refresh
    // and turn one glass update into a Hide/Show feedback loop.
    private bool _glassSuppressed;

    // Recording tab state (kept in the window so it mirrors the controls live).
    private int _recSource;
    private int _recFps = 15;
    private int _recRes;
    private int _recCamSize = 1;
    private double _camXFrac = 0.76, _camYFrac = 0.62;
    private bool _pipDragging;

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
        // The footer reflects wherever THIS copy of the app actually lives, so it matches
        // the folder the user installed (or copied) the app to — not a hard-coded path.
        FooterText.Text = $"Portable · data stays in {AppPaths.Data}";

        // Inline editor: re-hosts the retired EditorWindow so screenshots are edited in place
        // on the Screenshot tab (no pop-up window). The pane overlays the landing content.
        _editor = new EditorPane(_host.Settings, _host.History, this);
        _editor.NewSnipRequested += (_, _) => _ = _host.CaptureAsync(_host.Settings.DefaultMode);
        ScreenshotTab.Children.Add(_editor);
        _editor.Visibility = Visibility.Collapsed;

        _hintTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
        _hintTimer.Tick += (_, _) => { _hintTimer.Stop(); HintBar.Visibility = Visibility.Collapsed; };
        _recTimer.Tick += (_, _) => { if (_host.IsRecording) UpdateRecordingUi(); };

        // Frosted-glass refresh: re-sample the desktop behind the window when it moves, resizes, or
        // comes back to the foreground. Debounced + throttled so it can't thrash or loop.
        _glassDebounce.Tick += (_, _) => { _glassDebounce.Stop(); if (_glassSuppressed) return; RefreshGlass(); };
        LocationChanged += (_, _) => ScheduleGlassRefresh();
        SizeChanged += (_, _) => ScheduleGlassRefresh();
        Activated += (_, _) => ScheduleGlassRefresh();

        ModeRect.Click += (_, _) => Launch(CaptureMode.Rectangle);
        ModeFreeform.Click += (_, _) => Launch(CaptureMode.Freeform);
        ModeWindow.Click += (_, _) => Launch(CaptureMode.Window);
        ModeFullscreen.Click += (_, _) => Launch(CaptureMode.Fullscreen);
        ModeMonitor.Click += (_, _) => Launch(CaptureMode.Monitor);
        NewSnipButton.Click += (_, _) => Launch(_host.Settings.DefaultMode);

        // Recording tab (inline controls, no pop-up setup window).
        RecSourceFull.Click += (_, _) => SelectRecSource(0);
        RecSourceMonitor.Click += (_, _) => SelectRecSource(1);
        RecSourceWindow.Click += (_, _) => SelectRecSource(2);
        RecSourceRegion.Click += (_, _) => SelectRecSource(3);
        RecFps15.Click += (_, _) => SelectRecFps(15);
        RecFps30.Click += (_, _) => SelectRecFps(30);
        RecFps60.Click += (_, _) => SelectRecFps(60);
        RecResNative.Click += (_, _) => SelectRecRes(0);
        RecRes1080.Click += (_, _) => SelectRecRes(1080);
        RecRes720.Click += (_, _) => SelectRecRes(720);
        RecRes540.Click += (_, _) => SelectRecRes(540);
        RecAudioToggle.Checked += (_, _) => RecMicPanel.Visibility = Visibility.Visible;
        RecAudioToggle.Unchecked += (_, _) => RecMicPanel.Visibility = Visibility.Collapsed;
        RecWebcamToggle.Checked += (_, _) => RecCamPanel.Visibility = Visibility.Visible;
        RecWebcamToggle.Unchecked += (_, _) => RecCamPanel.Visibility = Visibility.Collapsed;
        RecCamSizeS.Click += (_, _) => { SelectRecCamSize(0); UpdatePipLayout(); };
        RecCamSizeM.Click += (_, _) => { SelectRecCamSize(1); UpdatePipLayout(); };
        RecCamSizeL.Click += (_, _) => { SelectRecCamSize(2); UpdatePipLayout(); };
        RecPreview.SizeChanged += (_, _) => UpdatePipLayout();

        LoadMics(_host.Settings.Microphone);
        LoadCameras(_host.Settings.WebcamDevice);

        ViewHistoryButton.Click += (_, _) => _host.ShowHistory();
        HistoryButton.Click += (_, _) => _host.ShowHistory();
        // Settings now lives in the title bar as a gear icon (SettingsGear, wired in XAML).
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
            RefreshGlass();
            SelectTab(0);
            _host.History.Changed += OnHistoryChanged;
            _host.SettingsChanged += OnSettingsChanged;
            _host.RecordingState += OnRecordingState;
            _recTimer.Start();
            RefreshAll();
            RefreshRecording();
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
            _host.RecordingState -= OnRecordingState;
            _recTimer.Stop();
            _editor?.DisposeCanvas();
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

    private void SettingsGear_Click(object sender, RoutedEventArgs e) => _host.ShowSettings();

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
        RefreshGlass();
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
        RefreshScreenshotState();
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

    // ===================== Tabs (single window, no pop-ups) =====================

    private void SelectTab(int index)
    {
        if (TabScreenshot == null || ScreenshotTab == null) return;
        TabScreenshot.IsChecked = index == 0;
        TabVideo.IsChecked = index == 1;
        TabRecording.IsChecked = index == 2;
        ScreenshotTab.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        CutPaneHost.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelRecording.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (index == 0) RefreshScreenshotState();
        if (index == 2) UpdatePipLayout();
    }

    private void Tab_Checked(object sender, RoutedEventArgs e) =>
        SelectTab(sender == TabVideo ? 1 : sender == TabRecording ? 2 : 0);

    public void SwitchToRecordingTab() => SelectTab(2);
    public void SwitchToScreenshotTab() => SelectTab(0);
    public void SwitchToVideoTab() => SelectTab(1);

    // ===================== Inline editor (re-hosts the old EditorWindow) =====================

    public bool HasInlineImage => _editor.HasImage;

    /// <summary>Loads a snip into the inline editor on the Screenshot tab and reveals it.</summary>
    public bool LoadInlineEditor(System.Drawing.Bitmap image, CaptureMode mode, SnipEntry? entry)
    {
        try
        {
            SelectTab(0);
            _editor.LoadSnip(image, mode, entry);
            RefreshScreenshotState();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Could not open the inline editor", ex);
            MessageDialog.Error(this, "Could not open the editor", ex.Message);
            return false;
        }
    }

    /// <summary>Shows the inline editor when it has an image; otherwise the capture landing.</summary>
    private void RefreshScreenshotState()
    {
        if (ScreenshotTab == null) return;
        bool has = _editor.HasImage;
        _editor.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        PanelScreenshot.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
    }

    // ===================== Glass (frosted blur + tint) =====================

    private void ScheduleGlassRefresh()
    {
        if (_loading || _glassSuppressed) return;
        _glassDebounce.Start();
    }

    public void RefreshGlass(double? opacityOverride = null, int? blurOverride = null, bool? enabledOverride = null)
    {
        if (GlassRoot == null || _glassBusy) return;
        // Belt-and-braces throttle on top of the _glassSuppressed loop guard.
        if ((DateTime.UtcNow - _lastGlassAt).TotalMilliseconds < 250) return;
        _glassBusy = true;
        _lastGlassAt = DateTime.UtcNow;

        var s = _host.Settings;
        bool enabled = enabledOverride ?? s.TranslucentWindow;
        double opacity = opacityOverride ?? s.GlassOpacity;
        int blur = blurOverride ?? s.GlassBlur;
        try
        {
            if (!enabled)
            {
                // Solid (non-glass) surface — the "reversible" off state.
                GlassRoot.Background = TryFindResource("AppBackgroundBrush") as Brush
                    ?? ThemeManager.GlassBrush(0.0, ThemeManager.IsDark);
                return;
            }

            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            var rect = GlassBackdrop.GetWindowScreenRect(hwnd);
            if (rect.Width < 16 || rect.Height < 16) return;

            bool wasVisible = IsVisible;
            var wasMaximized = WindowState;
            _glassSuppressed = true;
            try
            {
                // Hide for the few ms it takes to sample so we capture the desktop BEHIND the window.
                if (wasVisible) Hide();
                using var bmp = GlassBackdrop.Build(rect, ThemeManager.SurfaceDrawColor(ThemeManager.IsDark), opacity, blur);
                var brush = new ImageBrush(BitmapUtils.ToBitmapSource(bmp)) { Stretch = Stretch.Fill, TileMode = TileMode.None };
                brush.Freeze();
                GlassRoot.Background = brush;
            }
            finally
            {
                if (wasVisible && !IsVisible)
                {
                    Show();
                    // WPF's Show() after Hide() drops a maximized window back to Normal, and
                    // that state change is exactly what fed the old Hide/Show feedback loop.
                    // Put the state back so the window ends where the user left it.
                    if (wasMaximized == WindowState.Maximized && WindowState != WindowState.Maximized)
                        WindowState = WindowState.Maximized;
                }
                // Keep the suppression active until the dispatcher has drained the events our
                // own Hide/Show just raised, then lift it so genuine user actions
                // (move / resize / foreground) still trigger a fresh sample.
                Dispatcher.BeginInvoke(() => _glassSuppressed = false, System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        }
        catch (Exception ex)
        {
            // Fall back to a flat translucent fill so the window still looks fine.
            Logger.Warn("Glass blur refresh failed: " + ex.Message);
            if (GlassRoot != null)
                GlassRoot.Background = ThemeManager.GlassBrush(opacity, ThemeManager.IsDark);
        }
        finally
        {
            _glassBusy = false;
        }
    }

    // ===================== Recording tab =====================

    private void SelectRecSource(int i)
    {
        _recSource = i;
        RecSourceFull.IsChecked = i == 0;
        RecSourceMonitor.IsChecked = i == 1;
        RecSourceWindow.IsChecked = i == 2;
        RecSourceRegion.IsChecked = i == 3;
    }

    private void SelectRecFps(int fps)
    {
        _recFps = fps;
        RecFps15.IsChecked = fps == 15;
        RecFps30.IsChecked = fps == 30;
        RecFps60.IsChecked = fps == 60;
    }

    private void SelectRecRes(int h)
    {
        _recRes = h;
        RecResNative.IsChecked = h == 0;
        RecRes1080.IsChecked = h == 1080;
        RecRes720.IsChecked = h == 720;
        RecRes540.IsChecked = h == 540;
    }

    private void SelectRecCamSize(int i)
    {
        _recCamSize = i;
        RecCamSizeS.IsChecked = i == 0;
        RecCamSizeM.IsChecked = i == 1;
        RecCamSizeL.IsChecked = i == 2;
    }

    private void RefreshRecording()
    {
        if (RecSourceFull == null) return;
        var s = _host.Settings;
        SelectRecSource(0);
        SelectRecFps(s.RecordingFps is 15 or 30 or 60 ? s.RecordingFps : 15);
        SelectRecRes(s.RecordingResolution);
        SelectRecCamSize(s.WebcamSize);
        RecAudioToggle.IsChecked = s.AudioEnabled;
        RecMicPanel.Visibility = s.AudioEnabled ? Visibility.Visible : Visibility.Collapsed;
        RecSeparateToggle.IsChecked = s.SeparateAudio;
        RecCursorToggle.IsChecked = s.IncludeCursorInRecording;
        RecWebcamToggle.IsChecked = s.WebcamEnabled;
        RecCamPanel.Visibility = s.WebcamEnabled ? Visibility.Visible : Visibility.Collapsed;
        RecCamMirror.IsChecked = s.WebcamMirror;
        _camXFrac = 0.76; _camYFrac = 0.62;
        UpdatePipLayout();

        if (RecordingController.IsAvailable)
        {
            RecEngineText.Text = "MP4 · H.264 encoder ready";
            RecEngineNotice.Background = (System.Windows.Media.Brush)TryFindResource("AccentSoftBrush");
            RecEngineNotice.BorderBrush = (System.Windows.Media.Brush)TryFindResource("AccentSoftBorderBrush");
            RecEngineIcon.Text = "\uE946";
            RecEngineIcon.Foreground = (System.Windows.Media.Brush)TryFindResource("AccentBrush");
        }
        else
        {
            RecEngineText.Text = "Recording engine not found. Place ffmpeg.exe in the Tools folder to enable MP4 recording.";
            RecEngineNotice.Background = (System.Windows.Media.Brush)TryFindResource("DangerSoftBrush");
            RecEngineNotice.BorderBrush = (System.Windows.Media.Brush)TryFindResource("DangerBrush");
            RecEngineIcon.Text = "\uE783";
            RecEngineIcon.Foreground = (System.Windows.Media.Brush)TryFindResource("DangerBrush");
        }
        UpdateRecordingUi();
    }

    private RecordingConfig BuildCurrentRecordingConfig()
    {
        var s = _host.Settings;
        return new RecordingConfig
        {
            Source = _recSource switch
            {
                1 => RecordingSource.Monitor,
                2 => RecordingSource.Window,
                3 => RecordingSource.Region,
                _ => RecordingSource.Fullscreen
            },
            Fps = _recFps,
            IncludeCursor = RecCursorToggle.IsChecked == true,
            IncludeAudio = RecAudioToggle.IsChecked == true,
            Microphone = RecMicCombo.SelectedItem as string ?? s.Microphone,
            TargetHeight = _recRes,
            SeparateAudio = RecSeparateToggle.IsChecked == true,
            WebcamEnabled = RecWebcamToggle.IsChecked == true,
            WebcamDevice = RecCamCombo.SelectedItem as string ?? s.WebcamDevice,
            WebcamSize = _recCamSize,
            WebcamX = _camXFrac,
            WebcamY = _camYFrac,
            WebcamMirror = RecCamMirror.IsChecked == true
        };
    }

    public void StartRecording()
    {
        if (_host.IsRecording)
        {
            _host.StopRecording();
            return;
        }
        if (!RecordingController.IsAvailable) return;

        var cfg = BuildCurrentRecordingConfig();
        var s = _host.Settings;
        s.RecordingFps = cfg.Fps;
        s.IncludeCursorInRecording = cfg.IncludeCursor;
        s.AudioEnabled = cfg.IncludeAudio;
        s.Microphone = cfg.IncludeAudio ? cfg.Microphone : "";
        s.RecordingResolution = cfg.TargetHeight;
        s.SeparateAudio = cfg.SeparateAudio;
        s.WebcamEnabled = cfg.WebcamEnabled;
        s.WebcamDevice = cfg.WebcamEnabled ? cfg.WebcamDevice : "";
        s.WebcamSize = cfg.WebcamSize;
        s.WebcamMirror = cfg.WebcamMirror;
        SettingsService.Save(s);

        UpdateRecordingUi();
        _ = _host.StartRecording(cfg);
    }

    private void RecRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_host.IsRecording)
        {
            _host.StopRecording();
            return;
        }
        StartRecording();
    }

    private void OnRecordingState(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdateRecordingUi);

    private void UpdateRecordingUi()
    {
        if (RecRecordButton == null) return;
        bool rec = _host.IsRecording;
        RecRecordLabel.Text = rec ? "Stop" : "Record";
        RecRecordButton.IsEnabled = rec || RecordingController.IsAvailable;
        RecStatusText.Text = rec
            ? $"Recording…  {_host.Recorder?.Elapsed:hh\\:mm\\:ss}"
            : "Ready";
    }

    private void LoadMics(string? saved)
    {
        RecMicCombo.Items.Clear();
        var mics = AudioDevices.Enumerate();
        if (mics.Count == 0)
        {
            RecMicCombo.Items.Add("No microphone found");
            RecMicCombo.IsEnabled = false;
            RecMicCombo.SelectedIndex = 0;
            return;
        }
        foreach (var m in mics) RecMicCombo.Items.Add(m);
        int idx = string.IsNullOrWhiteSpace(saved) ? -1 : mics.IndexOf(saved);
        RecMicCombo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void LoadCameras(string? saved)
    {
        RecCamCombo.Items.Clear();
        var cams = VideoDevices.Enumerate();
        if (cams.Count == 0)
        {
            RecCamCombo.Items.Add("No webcam found");
            RecCamCombo.IsEnabled = false;
            RecCamCombo.SelectedIndex = 0;
            return;
        }
        foreach (var c in cams) RecCamCombo.Items.Add(c);
        int idx = string.IsNullOrWhiteSpace(saved) ? -1 : cams.IndexOf(saved);
        RecCamCombo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void UpdatePipLayout()
    {
        if (RecPreview == null || RecPipBox == null) return;
        double pw = RecPreview.ActualWidth, ph = RecPreview.ActualHeight;
        if (pw < 4 || ph < 4) return;
        double factor = _recCamSize switch { 0 => 0.16, 2 => 0.30, _ => 0.22 };
        double w = Math.Clamp(pw * factor, 40, pw - 12);
        double h = w * 9.0 / 16.0;
        if (h > ph - 12) { h = ph - 12; w = h * 16.0 / 9.0; }
        double maxL = Math.Max(1, pw - w), maxT = Math.Max(1, ph - h);
        double left = Math.Clamp(_camXFrac * maxL, 0, maxL);
        double top = Math.Clamp(_camYFrac * maxT, 0, maxT);
        RecPipBox.Width = w;
        RecPipBox.Height = h;
        RecPipBox.Margin = new Thickness(left, top, 0, 0);
    }

    private void RecPipBox_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _pipDragging = true;
        try { RecPipBox.CaptureMouse(); } catch { }
        e.Handled = true;
    }

    private void RecPipBox_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_pipDragging) return;
        var p = e.GetPosition(RecPreview);
        double maxL = Math.Max(1, RecPreview.ActualWidth - RecPipBox.Width);
        double maxT = Math.Max(1, RecPreview.ActualHeight - RecPipBox.Height);
        _camXFrac = Math.Clamp((p.X - RecPipBox.Width / 2) / maxL, 0, 1);
        _camYFrac = Math.Clamp((p.Y - RecPipBox.Height / 2) / maxT, 0, 1);
        UpdatePipLayout();
    }

    private void RecPipBox_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pipDragging) return;
        _pipDragging = false;
        try { RecPipBox.ReleaseMouseCapture(); } catch { }
    }

    // ===================== Video editing tab (Shutio Cut timeline) =====================

    /// <summary>Loads a recording into the timeline editor and switches to the Video tab.</summary>
    public void OpenRecordingInEditor(string path)
    {
        SelectTab(1);
        CutPaneHost.LoadRecording(path);
    }

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
