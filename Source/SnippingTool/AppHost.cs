using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using SnippingTool.Capture;
using SnippingTool.Core;
using SnippingTool.Editor;
using SnippingTool.Imaging;
using SnippingTool.Recording;
using SnippingTool.Storage;
using SnippingTool.UI;
using Point = System.Drawing.Point;

namespace SnippingTool;

/// <summary>
/// Owns the long-lived services and coordinates capture, editing, recording, and the windows.
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly List<EditorWindow> _editors = new();
    private List<Window> _recordingHiddenWindows = new();
    private bool _hotkeysAttached;
    private bool _capturing;
    private bool _disposed;

    public Settings Settings { get; private set; }
    public HistoryService History { get; } = new();
    public HotkeyManager Hotkeys { get; } = new();
    public MainWindow? MainWindow { get; private set; }
    public TrayIcon? Tray { get; private set; }
    public FloatingToolbarWindow? FloatingToolbar { get; private set; }
    public bool IsExiting { get; private set; }
    public string HotkeyStatus { get; private set; } = "Shortcuts not registered";

    public event EventHandler? SettingsChanged;

    public AppHost(Settings settings)
    {
        Settings = settings;
    }

    public void Start(bool showMain)
    {
        History.Load();
        var trayTooltip = "Shutio";
        try
        {
            Tray = new TrayIcon();
            Tray.CaptureRequested += (_, mode) => _ = CaptureAsync(mode);
            Tray.ShowRequested += (_, _) => ShowMain();
            Tray.HistoryRequested += (_, _) => ShowHistory();
            Tray.SettingsRequested += (_, _) => ShowSettings();
            Tray.RecordingRequested += (_, _) => BeginRecordingFromUi();
            Tray.ExitRequested += (_, _) => RequestExit();
            Tray.UpdateTooltip(trayTooltip);
        }
        catch (Exception ex)
        {
            Logger.Error("Tray icon could not be created", ex);
        }

        MainWindow = new MainWindow(this);
        try
        {
            // Create the handle without flashing a window so global hotkeys work immediately.
            new WindowInteropHelper(MainWindow).EnsureHandle();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not create the main window handle", ex);
        }

        ApplyFloatingToolbar(Settings.ShowFloatingToolbar);
        ApplyHotkeys(announce: true);
        UpdateTrayTooltip();

        if (showMain)
            ShowMain();
        Logger.Info($"App started. Main visible: {showMain}; hotkeys: {HotkeyStatus}; ffmpeg: {RecordingController.IsAvailable}.");
    }

    public void AttachHotkeys(Window window)
    {
        if (_hotkeysAttached) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        Hotkeys.Attach(hwnd, OnHotkeyAction);
        _hotkeysAttached = true;
        ApplyHotkeys(announce: false);
    }

    private void OnHotkeyAction(string action)
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke(() => OnHotkeyAction(action));
            return;
        }
        Logger.Info("Hotkey action: " + action);
        switch (action)
        {
            case HotkeyDefaults.CaptureRect:
                _ = CaptureAsync(CaptureMode.Rectangle);
                break;
            case HotkeyDefaults.CaptureFreeform:
                _ = CaptureAsync(CaptureMode.Freeform);
                break;
            case HotkeyDefaults.CaptureWindow:
                _ = CaptureAsync(CaptureMode.Window);
                break;
            case HotkeyDefaults.CaptureFullscreen:
                _ = CaptureAsync(CaptureMode.Fullscreen);
                break;
            case HotkeyDefaults.CaptureMonitor:
                _ = CaptureAsync(CaptureMode.Monitor);
                break;
            case HotkeyDefaults.History:
                ShowHistory();
                break;
            case HotkeyDefaults.Editor:
                OpenLatestEditor();
                break;
            case HotkeyDefaults.StopRecording:
                // Only armed while a recording is live; route it through the same path as the stop button.
                if (_recordingIndicator is { IsVisible: true } indicator) _ = indicator.StopAsync();
                break;
        }
    }

    private IReadOnlyList<HotkeyRegistration> ApplyHotkeys(bool announce)
    {
        var results = Hotkeys.RegisterAll(Settings.Hotkeys);
        int ok = results.Count(r => r.Success);
        HotkeyStatus = results.Count == 0
            ? "No shortcuts configured"
            : ok == results.Count
                ? "Shortcuts ready"
                : ok == 0
                    ? "Shortcuts blocked"
                    : $"{ok} of {results.Count} shortcuts active";
        UpdateTrayTooltip();

        if (announce)
        {
            if (results.Count > 0 && ok == 0)
            {
                if (!Settings.ShowFloatingToolbar)
                {
                    Settings.ShowFloatingToolbar = true;
                    SettingsService.Save(Settings);
                }
                ApplyFloatingToolbar(true);
                Notify("Windows did not allow the global shortcuts. Use the floating toolbar or the tray icon instead.",
                    "Settings", ShowSettings);
                Logger.Warn("All global hotkeys were blocked; floating toolbar enabled.");
            }
            else if (ok < results.Count)
            {
                Notify("Some global shortcuts are already in use. Everything else is ready.",
                    "Review", ShowSettings);
            }
        }
        RaiseSettingsChanged();
        return results;
    }

    /// <summary>
    /// Temporarily releases the global shortcuts so the Settings window can record
    /// combinations that are already assigned (RegisterHotKey would otherwise swallow them).
    /// </summary>
    public void SuspendHotkeys()
    {
        if (IsExiting) return;
        try { Hotkeys.UnregisterAll(); } catch { }
        HotkeyStatus = "Shortcuts paused while Settings is open";
        UpdateTrayTooltip();
        RaiseSettingsChanged();
    }

    public void ResumeHotkeys()
    {
        if (IsExiting) return;
        ApplyHotkeys(announce: false);
        RaiseSettingsChanged();
    }

    public IReadOnlyList<HotkeyRegistration> ApplySettings(Settings settings)
    {
        settings.Sanitize();
        Settings = settings;
        SettingsService.Save(Settings);
        ThemeManager.Apply(Settings.Theme);
        ApplyFloatingToolbar(Settings.ShowFloatingToolbar);
        var results = ApplyHotkeys(announce: false);
        UpdateTrayTooltip();
        RaiseSettingsChanged();
        return results.Where(r => !r.Success).ToList();
    }

    public void EnableFloatingToolbar(bool show)
    {
        Settings.ShowFloatingToolbar = show;
        SettingsService.Save(Settings);
        ApplyFloatingToolbar(show);
        RaiseSettingsChanged();
    }

    public void RaiseSettingsChanged() => SettingsChanged?.Invoke(this, EventArgs.Empty);

    public void Notify(string message, string? actionLabel = null, Action? action = null)
    {
        if (!Settings.ShowNotifications) return;
        ToastWindow.Show(message, actionLabel, action);
    }

    private void UpdateTrayTooltip()
    {
        if (Tray == null) return;
        string tip = "Shutio";
        if (Settings.Hotkeys.TryGetValue(HotkeyDefaults.CaptureRect, out var gesture) && !string.IsNullOrWhiteSpace(gesture))
            tip += $" — {gesture}";
        Tray.UpdateTooltip(tip);
    }

    // ------------------------------------------------------------------ windows

    public void ShowMain()
    {
        if (MainWindow == null) return;
        try
        {
            if (!MainWindow.IsVisible) MainWindow.Show();
            if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
            var hwnd = new WindowInteropHelper(MainWindow).Handle;
            if (hwnd != IntPtr.Zero) NativeMethods.SetForegroundWindow(hwnd);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not show the main window", ex);
        }
    }

    public void ShowHistory()
    {
        try
        {
            if (HistoryWindow is { IsLoaded: true } existing)
            {
                existing.Activate();
                return;
            }
            var window = new HistoryWindow(this);
            HistoryWindow = window;
            if (MainWindow?.IsVisible == true)
            {
                window.Owner = MainWindow;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            window.Closed += (_, _) => { if (ReferenceEquals(HistoryWindow, window)) HistoryWindow = null; };
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not open history", ex);
            MessageDialog.Error(MainWindow?.IsVisible == true ? MainWindow : null, "Could not open history", ex.Message);
        }
    }

    public HistoryWindow? HistoryWindow { get; private set; }

    public void ShowSettings()
    {
        try
        {
            if (SettingsWindow is { IsLoaded: true } existing)
            {
                existing.Activate();
                return;
            }
            var window = new SettingsWindow(this);
            SettingsWindow = window;
            if (MainWindow?.IsVisible == true)
            {
                window.Owner = MainWindow;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(SettingsWindow, window)) SettingsWindow = null;
                ResumeHotkeys();
            };
            SuspendHotkeys();
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not open settings", ex);
            MessageDialog.Error(MainWindow?.IsVisible == true ? MainWindow : null, "Could not open settings", ex.Message);
        }
    }

    public SettingsWindow? SettingsWindow { get; private set; }

    // ------------------------------------------------------------------ capture

    public async Task CaptureAsync(CaptureMode mode)
    {
        if (_capturing)
        {
            Notify("A capture is already in progress.");
            return;
        }
        _capturing = true;
        var hidden = HideAppWindows();
        try
        {
            await Task.Delay(170);
            if (Settings.DelaySeconds > 0)
            {
                var countdown = new CountdownWindow(Settings.DelaySeconds);
                countdown.Show();
                bool completed = await countdown.Completion;
                if (!completed)
                {
                    Notify("Capture cancelled.");
                    return;
                }
                await Task.Delay(130);
            }

            Rectangle virtualScreen;
            Bitmap frozen;
            try
            {
                virtualScreen = CaptureService.VirtualScreen;
                frozen = CaptureService.CaptureVirtualScreen();
            }
            catch (Exception ex)
            {
                Logger.Error("Screen capture failed", ex);
                MessageDialog.Error(VisibleOwner(), "Could not capture the screen",
                    $"{ex.Message}\n\nThis session may not allow desktop capture.");
                return;
            }

            using (frozen)
            {
                var overlay = new CaptureOverlayWindow(frozen, virtualScreen, mode);
                overlay.ShowDialog();
                var result = overlay.SelectedResult;
                if (result == null)
                {
                    Logger.Info("Capture cancelled by the user.");
                    return;
                }

                using var snip = BuildSnip(result, frozen);
                await ProcessSnipAsync(snip, result.Mode);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Capture pipeline failed", ex);
            MessageDialog.Error(VisibleOwner(), "Capture failed", ex.Message);
        }
        finally
        {
            _capturing = false;
            RestoreWindows(hidden);
        }
    }

    private static Bitmap BuildSnip(CaptureResult result, Bitmap frozen)
    {
        if (result.Mode == CaptureMode.Window && result.Window != null)
        {
            try
            {
                return CaptureService.CaptureWindow(result.Window.Handle, frozen);
            }
            catch (Exception ex)
            {
                Logger.Warn("Window capture failed; falling back to the frozen desktop", ex);
            }
        }

        var virtualScreen = CaptureService.VirtualScreen;
        var crop = new Rectangle(
            result.Region.Left - virtualScreen.Left,
            result.Region.Top - virtualScreen.Top,
            result.Region.Width,
            result.Region.Height);

        if (result.Mode == CaptureMode.Freeform && result.FreeformPoints is { Count: >= 3 })
        {
            var points = result.FreeformPoints
                .Select(p => new Point((int)Math.Round(p.X) - virtualScreen.Left, (int)Math.Round(p.Y) - virtualScreen.Top))
                .ToList();
            using var masked = CaptureService.CreateFreeformSnip(frozen, points);
            return BitmapUtils.Crop(masked, crop);
        }

        return BitmapUtils.Crop(frozen, crop);
    }

    private Task ProcessSnipAsync(Bitmap snip, CaptureMode mode)
    {
        bool copied = false;
        if (Settings.AutoCopy)
            copied = ClipboardService.SetImage(snip);

        SnipEntry? entry = null;
        if (Settings.AutoSave)
            entry = CaptureStorage.SaveNew(snip, mode, Settings, History);

        bool editorOpen = false;
        if (Settings.OpenEditorAfterCapture)
            editorOpen = OpenEditor(snip, mode, entry);

        if (Settings.ShowNotifications)
        {
            string dimensions = $"{snip.Width} × {snip.Height}";
            string message;
            if (entry != null && copied)
                message = $"Snip saved and copied · {dimensions}";
            else if (entry != null)
                message = $"Snip saved · {dimensions}";
            else if (copied)
                message = $"Snip copied to the clipboard · {dimensions}";
            else
                message = $"Snip captured · {dimensions}";

            if (editorOpen)
            {
                ToastWindow.Show(message, "View", ShowMain, snip);
            }
            else
            {
                var editCopy = BitmapUtils.Clone(snip);
                ToastWindow.Show(message, "Edit", () =>
                {
                    using var image = editCopy;
                    OpenEditor(image, mode, entry);
                }, snip);
            }
        }
        return Task.CompletedTask;
    }

    private static void ActivateWindow(Window window)
    {
        try
        {
            if (!window.IsVisible) window.Show();
            window.Activate();
        }
        catch { }
    }

    public bool OpenEditor(Bitmap image, CaptureMode mode, SnipEntry? entry)
    {
        try
        {
            if (MainWindow == null) return false;
            if (!MainWindow.LoadInlineEditor(image, mode, entry)) return false;
            ShowMain();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Could not open the editor", ex);
            MessageDialog.Error(VisibleOwner(), "Could not open the editor", ex.Message);
            return false;
        }
    }

    public void OpenEditorForEntry(SnipEntry entry)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(entry.FilePath) || !File.Exists(entry.FilePath))
            {
                MessageDialog.Warning(VisibleOwner(), "Snip file is missing",
                    $"The file could not be found:\n{entry.FilePath}");
                return;
            }
            using var bitmap = BitmapUtils.Load(entry.FilePath);
            var mode = Enum.TryParse<CaptureMode>(entry.Mode, true, out var parsed) ? parsed : CaptureMode.Rectangle;
            OpenEditor(bitmap, mode, entry);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not open snip from history", ex);
            MessageDialog.Error(VisibleOwner(), "Could not open snip", ex.Message);
        }
    }

    public void OpenLatestEditor()
    {
        if (MainWindow != null && MainWindow.HasInlineImage)
        {
            MainWindow.SwitchToScreenshotTab();
            ShowMain();
            return;
        }
        var recent = History.Recent(1).FirstOrDefault();
        if (recent != null)
        {
            OpenEditorForEntry(recent);
            return;
        }
        ShowMain();
        Notify("Take a snip first, then the editor shortcut will reopen it.");
    }

    private Window? VisibleOwner()
    {
        if (MainWindow?.IsVisible == true) return MainWindow;
        if (SettingsWindow?.IsVisible == true) return SettingsWindow;
        if (HistoryWindow?.IsVisible == true) return HistoryWindow;
        return _editors.LastOrDefault(w => w.IsVisible);
    }

    private List<Window> HideAppWindows()
    {
        var hidden = new List<Window>();
        try
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (!window.IsVisible) continue;
                if (window is CaptureOverlayWindow or CountdownWindow) continue;
                if (window is ToastWindow)
                {
                    window.Hide();
                    continue;
                }
                try
                {
                    window.Hide();
                    hidden.Add(window);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not hide app windows for capture", ex);
        }
        return hidden;
    }

    private void RestoreWindows(List<Window> hidden)
    {
        foreach (var window in hidden)
        {
            try
            {
                if (!window.IsVisible) window.Show();
            }
            catch { }
        }
        try
        {
            var editor = _editors.LastOrDefault(w => w.IsVisible);
            if (editor != null)
            {
                editor.Activate();
                return;
            }
            if (SettingsWindow?.IsVisible == true)
            {
                SettingsWindow.Activate();
                return;
            }
            if (HistoryWindow?.IsVisible == true)
            {
                HistoryWindow.Activate();
                return;
            }
            if (hidden.Contains(MainWindow) && MainWindow?.IsVisible == true)
                MainWindow.Activate();
        }
        catch { }
    }

    // ------------------------------------------------------------------ floating toolbar

    private void ApplyFloatingToolbar(bool show)
    {
        try
        {
            if (show)
            {
                if (FloatingToolbar == null)
                {
                    FloatingToolbar = new FloatingToolbarWindow();
                    FloatingToolbar.CaptureRequested += (_, mode) => _ = CaptureAsync(mode);
                    FloatingToolbar.HistoryRequested += (_, _) => ShowHistory();
                    FloatingToolbar.HiddenRequested += (_, _) => EnableFloatingToolbar(false);
                }
                if (!FloatingToolbar.IsVisible) FloatingToolbar.Show();
            }
            else
            {
                FloatingToolbar?.Hide();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Could not show the floating toolbar", ex);
        }
    }

    // ------------------------------------------------------------------ recording

    public void BeginRecordingFromUi()
    {
        if (_recorder is { IsRecording: true })
        {
            ShowMain();
            MainWindow?.SwitchToRecordingTab();
            Notify("A recording is already in progress. Use the Stop button to finish it.");
            return;
        }
        if (!RecordingController.IsAvailable)
        {
            MessageDialog.Warning(VisibleOwner(), "Recording is unavailable",
                $"The portable MP4 encoder was not found.\n\nPlace ffmpeg.exe in:\n{Path.Combine(AppPaths.Tools, "ffmpeg.exe")}\n\nScreenshots and the editor work without it.");
            return;
        }
        if (_capturing)
        {
            Notify("Finish the current capture before starting a recording.");
            return;
        }

        // No more pop-up setup window: bring the Recording tab into view and start from its controls.
        ShowMain();
        MainWindow?.SwitchToRecordingTab();
        MainWindow?.StartRecording();
    }

    public async Task StartRecording(RecordingConfig cfg)
    {
        var hidden = HideAppWindows();
        try
        {
            await Task.Delay(170);
            Rectangle region = CaptureService.VirtualScreen;

            if (cfg.Source == RecordingSource.Monitor)
            {
                // "Single display" = the primary monitor.
                region = CaptureService.MonitorAt(new System.Drawing.Point(0, 0)).Bounds;
            }
            else if (cfg.Source != RecordingSource.Fullscreen)
            {
                var mode = cfg.Source == RecordingSource.Window ? CaptureMode.Window : CaptureMode.Rectangle;
                using var frozen = CaptureService.CaptureVirtualScreen();
                var overlay = new CaptureOverlayWindow(frozen, CaptureService.VirtualScreen, mode, regionOnly: true);
                overlay.ShowDialog();
                var result = overlay.SelectedResult;
                if (result == null)
                {
                    RestoreWindows(hidden);
                    Notify("Recording cancelled.");
                    return;
                }
                region = result.Region;
            }

            await Task.Delay(120);
            string output = Path.Combine(AppPaths.Recordings,
                $"Recording_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4");
            _recorder?.Dispose();
            _recorder = new RecordingController();
            _recorder.Start(new RecordingOptions
            {
                Region = region,
                Fps = cfg.Fps,
                IncludeCursor = cfg.IncludeCursor,
                IncludeAudio = cfg.IncludeAudio,
                Microphone = cfg.Microphone,
                OutputPath = output,
                TargetHeight = cfg.TargetHeight,
                SeparateAudio = cfg.SeparateAudio,
                WebcamEnabled = cfg.WebcamEnabled,
                WebcamDevice = cfg.WebcamDevice,
                WebcamSize = cfg.WebcamSize,
                WebcamX = cfg.WebcamX,
                WebcamY = cfg.WebcamY,
                WebcamMirror = cfg.WebcamMirror
            });
            _recordingHiddenWindows = hidden;

            var indicator = new RecordingIndicatorWindow(_recorder);
            _recordingIndicator = indicator;
            indicator.Finished += OnRecordingFinished;
            indicator.Closed += (_, _) =>
            {
                if (ReferenceEquals(_recordingIndicator, indicator)) _recordingIndicator = null;
            };
            indicator.Show();
            RecordingState?.Invoke(this, EventArgs.Empty);
            string stopGesture = Settings.Hotkeys.TryGetValue(HotkeyDefaults.StopRecording, out var g) ? g : "";
            bool stopHotkeyArmed = Hotkeys.RegisterStopRecording(stopGesture);
            Notify(stopHotkeyArmed
                ? "Recording started. Use the stop button or your stop shortcut to finish."
                : "Recording started. Use the stop button in the recording indicator.");
            Logger.Info($"Recording started at {region} -> {output} (stop shortcut {stopGesture}: {(stopHotkeyArmed ? "armed" : "not armed — stop button works")})");
        }
        catch (Exception ex)
        {
            Logger.Error("Could not start recording", ex);
            RestoreWindows(hidden);
            MessageDialog.Error(VisibleOwner(), "Recording could not start", ex.Message);
            _recorder?.Dispose();
            _recorder = null;
        }
    }

    private void OnRecordingFinished(object? sender, RecordingStoppedEventArgs result)
    {
        try
        {
            if (result.Success)
            {
                var duration = result.Duration;
                string label = $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}";
                string message = $"Recording saved · {label} · {Path.GetFileName(result.OutputPath)}";
                if (!string.IsNullOrEmpty(result.AudioOutputPath) && File.Exists(result.AudioOutputPath))
                    message += $" · + {Path.GetFileName(result.AudioOutputPath)}";
                ToastWindow.Show(message, "Open folder", OpenRecordingsFolder);
                Logger.Info($"Recording finished: {result.OutputPath} ({label}, {result.Frames} frames){(result.AudioOutputPath.Length > 0 ? "; audio " + result.AudioOutputPath : "")}");
            }
            else
            {
                // Separate-audio mode can leave a partial .wav behind when the video fails — remove it.
                try
                {
                    if (!string.IsNullOrEmpty(result.AudioOutputPath) && File.Exists(result.AudioOutputPath))
                        File.Delete(result.AudioOutputPath);
                }
                catch { }
                MessageDialog.Error(VisibleOwner(), "Recording failed",
                    result.Error ?? "The encoder did not produce a usable video file.");
            }
        }
        finally
        {
            Hotkeys.UnregisterStopRecording();
            _recorder?.Dispose();
            _recorder = null;
            _recordingIndicator = null;
            var hidden = _recordingHiddenWindows;
            _recordingHiddenWindows = new List<Window>();
            RestoreWindows(hidden);
            RecordingState?.Invoke(this, EventArgs.Empty);
        }
    }

    private static void OpenRecordingsFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Recordings);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Recordings}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not open recordings folder", ex);
        }
    }

    private RecordingController? _recorder;
    private RecordingIndicatorWindow? _recordingIndicator;

    /// <summary>The live recorder, if any (lets the Recording tab mirror the state and drive Stop).</summary>
    public RecordingController? Recorder => _recorder;
    public bool IsRecording => _recorder is { IsRecording: true };
    public event EventHandler? RecordingState;

    /// <summary>Stops the active recording (driven from the Recording tab's Stop button).</summary>
    public void StopRecording()
    {
        if (_recordingIndicator is { IsVisible: true } ind)
        {
            _ = ind.StopAsync();
            return;
        }
        if (_recorder is { IsRecording: true } r)
        {
            _ = r.StopAsync().ContinueWith(t =>
                Application.Current?.Dispatcher.BeginInvoke(() => OnRecordingFinished(r, t.Result)));
        }
    }

    // ------------------------------------------------------------------ lifecycle

    public void RequestExit()
    {
        if (IsExiting) return;
        IsExiting = true;
        try
        {
            SettingsService.Save(Settings);
            foreach (var editor in _editors.ToList())
                editor.Close();
            if (_editors.Any(w => w.IsVisible))
            {
                IsExiting = false;
                return;
            }
            HistoryWindow?.Close();
            SettingsWindow?.Close();
            FloatingToolbar?.Close();
            MainWindow?.Close();
            _recorder?.Dispose();
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Logger.Error("Exit failed", ex);
            Application.Current.Shutdown();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Hotkeys.Detach(); } catch { }
        try { Tray?.Dispose(); } catch { }
        try { _recorder?.Dispose(); } catch { }
    }
}
