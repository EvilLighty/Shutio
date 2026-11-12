using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using SnippingTool.Core;
using SnippingTool.Recording;
using CaptureMode = SnippingTool.Core.CaptureMode;

namespace SnippingTool.UI;

public partial class SettingsWindow : Window
{
    private readonly AppHost _host;
    private readonly Dictionary<string, HotkeyBox> _hotkeyBoxes = new();
    private Settings _working;
    private bool _applied;
    private bool _initializing = true;

    public SettingsWindow(AppHost host)
    {
        _host = host;
        _working = host.Settings.Clone();
        InitializeComponent();
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));

        ThemeSystem.Click += (_, _) => PreviewTheme(AppTheme.System);
        ThemeLight.Click += (_, _) => PreviewTheme(AppTheme.Light);
        ThemeDark.Click += (_, _) => PreviewTheme(AppTheme.Dark);

        GlassEnabledToggle.Checked += (_, _) => { _working.TranslucentWindow = true; UpdateGlassLabels(); PreviewGlass(); };
        GlassEnabledToggle.Unchecked += (_, _) => { _working.TranslucentWindow = false; UpdateGlassLabels(); PreviewGlass(); };
        BlurSlider.ValueChanged += (_, _) =>
        {
            _working.GlassBlur = (int)Math.Round(BlurSlider.Value);
            UpdateGlassLabels();
            PreviewGlass();
        };
        TransparencySlider.ValueChanged += (_, _) =>
        {
            _working.GlassOpacity = Math.Round(TransparencySlider.Value * 100) / 100.0;
            UpdateGlassLabels();
            PreviewGlass();
        };

        ModeRectangle.Click += (_, _) => SelectMode(CaptureMode.Rectangle);
        ModeFreeform.Click += (_, _) => SelectMode(CaptureMode.Freeform);
        ModeWindow.Click += (_, _) => SelectMode(CaptureMode.Window);
        ModeFullscreen.Click += (_, _) => SelectMode(CaptureMode.Fullscreen);
        ModeMonitor.Click += (_, _) => SelectMode(CaptureMode.Monitor);

        Delay0.Click += (_, _) => SelectDelay(0);
        Delay3.Click += (_, _) => SelectDelay(3);
        Delay5.Click += (_, _) => SelectDelay(5);
        Delay10.Click += (_, _) => SelectDelay(10);

        Fps10.Click += (_, _) => SelectFps(10);
        Fps15.Click += (_, _) => SelectFps(15);
        Fps24.Click += (_, _) => SelectFps(24);
        Fps30.Click += (_, _) => SelectFps(30);
        Fps60.Click += (_, _) => SelectFps(60);

        AutoCopyToggle.Checked += (_, _) => _working.AutoCopy = true;
        AutoCopyToggle.Unchecked += (_, _) => _working.AutoCopy = false;
        AutoSaveToggle.Checked += (_, _) => _working.AutoSave = true;
        AutoSaveToggle.Unchecked += (_, _) => _working.AutoSave = false;
        OpenEditorToggle.Checked += (_, _) => _working.OpenEditorAfterCapture = true;
        OpenEditorToggle.Unchecked += (_, _) => _working.OpenEditorAfterCapture = false;
        NotificationsToggle.Checked += (_, _) => _working.ShowNotifications = true;
        NotificationsToggle.Unchecked += (_, _) => _working.ShowNotifications = false;
        FloatingToolbarToggle.Checked += (_, _) => _working.ShowFloatingToolbar = true;
        FloatingToolbarToggle.Unchecked += (_, _) => _working.ShowFloatingToolbar = false;
        CloseToTrayToggle.Checked += (_, _) => _working.CloseToTray = true;
        CloseToTrayToggle.Unchecked += (_, _) => _working.CloseToTray = false;
        StartTrayToggle.Checked += (_, _) => _working.StartMinimizedInTray = true;
        StartTrayToggle.Unchecked += (_, _) => _working.StartMinimizedInTray = false;
        MinimizeTrayToggle.Checked += (_, _) => _working.MinimizeToTray = true;
        MinimizeTrayToggle.Unchecked += (_, _) => _working.MinimizeToTray = false;
        CursorToggle.Checked += (_, _) => _working.IncludeCursorInRecording = true;
        CursorToggle.Unchecked += (_, _) => _working.IncludeCursorInRecording = false;
        AudioToggle.Checked += (_, _) => { _working.AudioEnabled = true; MicPickerRow.Visibility = Visibility.Visible; };
        AudioToggle.Unchecked += (_, _) => { _working.AudioEnabled = false; MicPickerRow.Visibility = Visibility.Collapsed; };
        MicBox.SelectionChanged += (_, _) => _working.Microphone = MicBox.SelectedItem as string ?? "";
        UpdateAutoToggle.Checked += (_, _) => _working.CheckForUpdatesAtStartup = true;
        UpdateAutoToggle.Unchecked += (_, _) => _working.CheckForUpdatesAtStartup = false;
        UpdateCheckButton.Click += (_, _) => _host.PromptForUpdate(this);

        PatternBox.TextChanged += (_, _) => UpdatePatternPreview();
        BrowseButton.Click += (_, _) => BrowseForFolder();
        OpenDataButton.Click += (_, _) => OpenFolder(AppPaths.Data);
        ResetAllButton.Click += (_, _) => ResetAll();
        ResetHotkeysButton.Click += (_, _) => ResetHotkeys();
        SaveButton.Click += (_, _) => Save();
        CancelButton.Click += (_, _) => Cancel();
        SourceInitialized += (_, _) => ThemeManager.ApplyWindowChrome(this);
        Loaded += (_, _) =>
        {
            ThemeManager.ApplyWindowChrome(this);
            _initializing = false;
        };
    }

    private void LoadControls()
    {
        _initializing = true;
        SelectThemeVisual(_working.Theme);
        GlassEnabledToggle.IsChecked = _working.TranslucentWindow;
        BlurSlider.Value = _working.GlassBlur;
        TransparencySlider.Value = _working.GlassOpacity;
        UpdateGlassLabels();
        SelectModeVisual(_working.DefaultMode);
        SelectDelayVisual(_working.DelaySeconds);
        SelectFpsVisual(_working.RecordingFps);
        AutoCopyToggle.IsChecked = _working.AutoCopy;
        AutoSaveToggle.IsChecked = _working.AutoSave;
        OpenEditorToggle.IsChecked = _working.OpenEditorAfterCapture;
        NotificationsToggle.IsChecked = _working.ShowNotifications;
        FloatingToolbarToggle.IsChecked = _working.ShowFloatingToolbar;
        CloseToTrayToggle.IsChecked = _working.CloseToTray;
        StartTrayToggle.IsChecked = _working.StartMinimizedInTray;
        MinimizeTrayToggle.IsChecked = _working.MinimizeToTray;
        CursorToggle.IsChecked = _working.IncludeCursorInRecording;
        AudioToggle.IsChecked = _working.AudioEnabled;
        MicPickerRow.Visibility = _working.AudioEnabled ? Visibility.Visible : Visibility.Collapsed;
        LoadMicBox(_working.Microphone);
        UpdateAutoToggle.IsChecked = _working.CheckForUpdatesAtStartup;
        TokenBox.Text = _working.GitHubToken;
        SaveDirectoryBox.Text = _working.SaveDirectory;
        PatternBox.Text = _working.FileNamePattern;
        DataPathText.Text = AppPaths.Data;
        RecordingEngineText.Text = RecordingController.IsAvailable
            ? "The MP4 encoder (ffmpeg) is available. Recordings are saved to Data\\Recordings."
            : "The MP4 encoder is not installed. Place ffmpeg.exe in the Tools folder to enable recording.";
        RecordingEngineText.Foreground = (Brush)FindResource(RecordingController.IsAvailable ? "TextSecondaryBrush" : "DangerBrush");
        BuildHotkeyRows();
        UpdatePatternPreview();
        ValidateHotkeys();
        _initializing = false;
    }

    private void LoadMicBox(string? saved)
    {
        MicBox.Items.Clear();
        var mics = AudioDevices.Enumerate();
        if (mics.Count == 0)
        {
            MicBox.Items.Add("No microphone found");
            MicBox.IsEnabled = false;
            MicBox.SelectedIndex = 0;
            return;
        }
        foreach (var m in mics) MicBox.Items.Add(m);
        int idx = string.IsNullOrWhiteSpace(saved) ? -1 : mics.IndexOf(saved);
        MicBox.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void BuildHotkeyRows()
    {
        HotkeyHost.Children.Clear();
        _hotkeyBoxes.Clear();

        var defaults = HotkeyDefaults.Create();
        foreach (var pair in defaults)
        {
            string action = pair.Key;
            string defaultGesture = pair.Value;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = HotkeyDefaults.Label(action),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 12, 0)
            };
            row.Children.Add(label);

            // The current key combination is shown as a compact chip right next to the
            // action label. Clicking it records a new combination (live), and the value
            // is always rendered as "Ctrl + Shift + S".
            var box = new HotkeyBox
            {
                Gesture = _working.Hotkeys.TryGetValue(action, out var gesture) ? gesture : defaultGesture,
                Width = 240,
                Height = 34,
                VerticalAlignment = VerticalAlignment.Center,
                AllowBareKeys = HotkeyDefaults.AllowsBareKey(action)
            };
            if (box.AllowBareKeys)
                box.ToolTip = "Click, then press a key — a single key like Esc is fine (it only works while a recording is active)";
            box.GestureChanged += (_, _) =>
            {
                ValidateHotkeys();
                UpdateShortcutStatus(action);
            };
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            _hotkeyBoxes[action] = box;

            var reset = new Button
            {
                Content = "Reset",
                Style = (Style)FindResource("LinkButton"),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            reset.Click += (_, _) => { box.Gesture = defaultGesture; UpdateShortcutStatus(action); };
            Grid.SetColumn(reset, 2);
            row.Children.Add(reset);

            HotkeyHost.Children.Add(row);
        }
    }

    private void UpdateShortcutStatus(string action)
    {
        string gesture = _hotkeyBoxes.TryGetValue(action, out var box) ? box.Gesture : "";
        if (string.IsNullOrWhiteSpace(gesture))
        {
            ShortcutStatusText.Text = $"{HotkeyDefaults.Label(action)}: cleared. Press a modifier key (Ctrl / Alt / Shift) plus a key to set it again.";
            return;
        }
        ShortcutStatusText.Text = $"Recorded {HotkeyBox.Display(gesture)} for {HotkeyDefaults.Label(action)} — press \u201CSave changes\u201D to apply.";
    }

    private void SelectThemeVisual(AppTheme theme)
    {
        ThemeSystem.IsChecked = theme == AppTheme.System;
        ThemeLight.IsChecked = theme == AppTheme.Light;
        ThemeDark.IsChecked = theme == AppTheme.Dark;
    }

    private void SelectModeVisual(CaptureMode mode)
    {
        ModeRectangle.IsChecked = mode == CaptureMode.Rectangle;
        ModeFreeform.IsChecked = mode == CaptureMode.Freeform;
        ModeWindow.IsChecked = mode == CaptureMode.Window;
        ModeFullscreen.IsChecked = mode == CaptureMode.Fullscreen;
        ModeMonitor.IsChecked = mode == CaptureMode.Monitor;
    }

    private void SelectDelayVisual(int delay)
    {
        Delay0.IsChecked = delay == 0;
        Delay3.IsChecked = delay == 3;
        Delay5.IsChecked = delay == 5;
        Delay10.IsChecked = delay == 10;
    }

    private void SelectFpsVisual(int fps)
    {
        Fps10.IsChecked = fps == 10;
        Fps15.IsChecked = fps == 15;
        Fps24.IsChecked = fps == 24;
        Fps30.IsChecked = fps == 30;
        Fps60.IsChecked = fps == 60;
    }

    private void PreviewTheme(AppTheme theme)
    {
        _working.Theme = theme;
        SelectThemeVisual(theme);
        ThemeManager.Apply(theme);
    }

    private void UpdateGlassLabels()
    {
        if (BlurValueText != null) BlurValueText.Text = ((int)Math.Round(BlurSlider.Value)).ToString();
        if (TransparencyValueText != null) TransparencyValueText.Text = ((int)Math.Round(TransparencySlider.Value * 100)) + "%";
    }

    /// <summary>
    /// Renders the main window's glass with the current draft values (without persisting them),
    /// so the user sees the frost/blur live while dragging. Re-asserts this window's foreground
    /// because the preview briefly hides/shows the main window behind it.
    /// </summary>
    private void PreviewGlass()
    {
        if (_initializing) return;
        try
        {
            _host.MainWindow?.RefreshGlass(_working.GlassOpacity, _working.GlassBlur, _working.TranslucentWindow);
        }
        catch { /* preview is best-effort; the value still applies on Save */ }
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { Activate(); }));
    }

    private void SelectMode(CaptureMode mode)
    {
        _working.DefaultMode = mode;
        SelectModeVisual(mode);
    }

    private void SelectDelay(int delay)
    {
        _working.DelaySeconds = delay;
        SelectDelayVisual(delay);
    }

    private void SelectFps(int fps)
    {
        _working.RecordingFps = fps;
        SelectFpsVisual(fps);
    }

    private bool ValidateHotkeys()
    {
        var errors = new List<string>();
        var seen = new Dictionary<string, string>();
        foreach (var pair in _hotkeyBoxes)
        {
            bool allowBare = HotkeyDefaults.AllowsBareKey(pair.Key);
            var gesture = HotkeyGesture.Parse(pair.Value.Gesture, allowBare);
            if (gesture == null)
            {
                errors.Add($"{HotkeyDefaults.Label(pair.Key)} has no valid shortcut.");
                continue;
            }
            if (seen.TryGetValue(gesture.ToString(), out var other))
                errors.Add($"{gesture} is assigned to both {HotkeyDefaults.Label(other)} and {HotkeyDefaults.Label(pair.Key)}.");
            else
                seen[gesture.ToString()] = pair.Key;
        }
        if (errors.Count > 0)
        {
            HotkeyConflictText.Text = string.Join(" ", errors);
            HotkeyConflictText.Visibility = Visibility.Visible;
            SaveButton.IsEnabled = false;
            return false;
        }
        HotkeyConflictText.Visibility = Visibility.Collapsed;
        SaveButton.IsEnabled = true;
        return true;
    }

    private void UpdatePatternPreview()
    {
        string pattern = PatternBox.Text;
        if (string.IsNullOrWhiteSpace(pattern)) pattern = "Snip_{yyyy}-{MM}-{dd}_{HH}-{mm}-{ss}";
        var now = DateTime.Now;
        string preview = pattern
            .Replace("{yyyy}", now.ToString("yyyy"))
            .Replace("{yy}", now.ToString("yy"))
            .Replace("{MM}", now.ToString("MM"))
            .Replace("{dd}", now.ToString("dd"))
            .Replace("{HH}", now.ToString("HH"))
            .Replace("{mm}", now.ToString("mm"))
            .Replace("{ss}", now.ToString("ss"))
            .Replace("{mode}", _working.DefaultMode.ToString());
        foreach (char c in Path.GetInvalidFileNameChars())
            preview = preview.Replace(c, '_');
        PatternPreview.Text = "Preview: " + preview.Trim().Trim('.') + ".png";
    }

    private void BrowseForFolder()
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Choose where snips are saved",
                InitialDirectory = Directory.Exists(SaveDirectoryBox.Text) ? SaveDirectoryBox.Text : AppPaths.Screenshots,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) == true)
            {
                SaveDirectoryBox.Text = dialog.FolderName;
                SaveHintText.Text = "Save folder updated.";
            }
        }
        catch (Exception ex)
        {
            MessageDialog.Error(this, "Could not choose a folder", ex.Message);
        }
    }

    private void Save()
    {
        if (!ValidateHotkeys()) return;

        string directory = Environment.ExpandEnvironmentVariables(SaveDirectoryBox.Text.Trim());
        try
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("Choose a folder.");
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            string probe = Path.Combine(directory, ".snippingtool-write-test");
            File.WriteAllText(probe, "test");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            MessageDialog.Warning(this, "Folder is not writable",
                $"The save folder could not be used:\n{ex.Message}\n\nChoose another folder and try again.");
            return;
        }

        if (string.IsNullOrWhiteSpace(PatternBox.Text))
        {
            MessageDialog.Warning(this, "File name pattern is empty", "Enter a pattern, for example Snip_{yyyy}-{MM}-{dd}_{HH}-{mm}-{ss}.");
            return;
        }

        _working.SaveDirectory = directory;
        _working.FileNamePattern = PatternBox.Text.Trim();
        _working.GitHubToken = (TokenBox.Text ?? "").Trim();
        foreach (var pair in _hotkeyBoxes)
            _working.Hotkeys[pair.Key] = pair.Value.Gesture;
        _working.Sanitize();

        var failures = _host.ApplySettings(_working);
        _applied = true;
        if (failures.Count > 0)
        {
            string details = string.Join("\n", failures.Select(f => $"• {HotkeyDefaults.Label(f.Action)}: {f.Error}"));
            int choice = MessageDialog.Show(this, "Some shortcuts are unavailable",
                $"Windows did not allow these global shortcuts:\n\n{details}\n\nYou can use the floating capture toolbar instead.",
                DialogKind.Question, new[] { "Show floating toolbar", "Keep settings" }, 1);
            if (choice == 0)
                _host.EnableFloatingToolbar(true);
        }
        Close();
    }

    private void Cancel()
    {
        if (!_applied)
            ThemeManager.Apply(_host.Settings.Theme);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancel();

    private void ResetAll()
    {
        int choice = MessageDialog.Show(this, "Reset settings", "Restore every setting to its default value? Your snips and history are not affected.",
            DialogKind.Question, new[] { "Reset", "Cancel" }, 1);
        if (choice != 0) return;
        _working = new Settings();
        LoadControls();
        PreviewTheme(_working.Theme);
    }

    private void ResetHotkeys()
    {
        var defaults = HotkeyDefaults.Create();
        foreach (var pair in _hotkeyBoxes)
            pair.Value.Gesture = defaults[pair.Key];
        ValidateHotkeys();
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageDialog.Error(this, "Could not open folder", ex.Message);
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        LoadControls();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_applied)
            ThemeManager.Apply(_host.Settings.Theme);
        base.OnClosing(e);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1 && e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }
}
