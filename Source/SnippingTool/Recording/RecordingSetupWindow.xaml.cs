using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SnippingTool.Core;

namespace SnippingTool.Recording;

public enum RecordingSource { Fullscreen, Monitor, Window, Region }

public partial class RecordingSetupWindow : Window
{
    public RecordingSource Source { get; private set; } = RecordingSource.Fullscreen;
    public int Fps { get; private set; } = 15;
    public bool IncludeCursor { get; private set; } = true;
    public bool IncludeAudio { get; private set; }
    public string Microphone { get; private set; } = "";
    public bool Confirmed { get; private set; }

    private readonly Settings _settings;

    public RecordingSetupWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        Fps = settings.RecordingFps is 10 or 15 or 24 or 30 or 60 ? settings.RecordingFps : 15;
        IncludeCursor = settings.IncludeCursorInRecording;
        IncludeAudio = settings.AudioEnabled;
        SelectFps(Fps);
        CursorCheck.IsChecked = IncludeCursor;
        AudioCheck.IsChecked = IncludeAudio;
        MicPickerPanel.Visibility = IncludeAudio ? Visibility.Visible : Visibility.Collapsed;
        AudioCheck.Checked += (_, _) => { IncludeAudio = true; MicPickerPanel.Visibility = Visibility.Visible; };
        AudioCheck.Unchecked += (_, _) => { IncludeAudio = false; MicPickerPanel.Visibility = Visibility.Collapsed; };
        LoadMicrophones(settings.Microphone);
        if (!RecordingController.IsAvailable)
        {
            EngineNotice.Background = (Brush)FindResource("DangerSoftBrush");
            EngineNotice.BorderBrush = (Brush)FindResource("DangerBrush");
            EngineIcon.Text = "\uE783";
            EngineIcon.Foreground = (Brush)FindResource("DangerBrush");
            EngineText.Text = "Recording engine not found. Place ffmpeg.exe in the Tools folder to enable MP4 recording.";
            StartButton.IsEnabled = false;
        }
        SourceFullscreen.Click += (_, _) => SelectSource(RecordingSource.Fullscreen, SourceFullscreen);
        SourceMonitor.Click += (_, _) => SelectSource(RecordingSource.Monitor, SourceMonitor);
        SourceWindow.Click += (_, _) => SelectSource(RecordingSource.Window, SourceWindow);
        SourceRegion.Click += (_, _) => SelectSource(RecordingSource.Region, SourceRegion);
        Fps10.Click += (_, _) => SelectFps(10);
        Fps15.Click += (_, _) => SelectFps(15);
        Fps24.Click += (_, _) => SelectFps(24);
        Fps30.Click += (_, _) => SelectFps(30);
        Fps60.Click += (_, _) => SelectFps(60);
        Loaded += (_, _) =>
        {
            ThemeManager.ApplyWindowChrome(this);
        };
        SourceInitialized += (_, _) =>
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int corners = 2;
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
            }
            catch { }
        };
    }

    private void SelectSource(RecordingSource source, ToggleButton selected)
    {
        Source = source;
        foreach (var toggle in new[] { SourceFullscreen, SourceMonitor, SourceWindow, SourceRegion })
            toggle.IsChecked = toggle == selected;
    }

    private void SelectFps(int fps)
    {
        Fps = fps;
        Fps10.IsChecked = fps == 10;
        Fps15.IsChecked = fps == 15;
        Fps24.IsChecked = fps == 24;
        Fps30.IsChecked = fps == 30;
        Fps60.IsChecked = fps == 60;
    }

    private void LoadMicrophones(string? saved)
    {
        MicCombo.Items.Clear();
        var mics = AudioDevices.Enumerate();
        if (mics.Count == 0)
        {
            MicCombo.Items.Add("No microphone found");
            MicCombo.IsEnabled = false;
            MicCombo.SelectedIndex = 0;
            return;
        }
        foreach (var m in mics) MicCombo.Items.Add(m);
        int idx = string.IsNullOrWhiteSpace(saved) ? -1 : mics.IndexOf(saved);
        MicCombo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        IncludeCursor = CursorCheck.IsChecked == true;
        IncludeAudio = AudioCheck.IsChecked == true;
        Microphone = MicCombo.SelectedItem as string ?? "";
        _settings.RecordingFps = Fps;
        _settings.IncludeCursorInRecording = IncludeCursor;
        _settings.AudioEnabled = IncludeAudio;
        _settings.Microphone = IncludeAudio ? Microphone : "";
        SettingsService.Save(_settings);
        Confirmed = true;
        try { DialogResult = true; } catch { }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try { DialogResult = false; } catch { }
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1 && e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }
}

