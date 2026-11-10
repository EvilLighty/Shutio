using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnippingTool.Core;

public enum AppTheme { System, Light, Dark }

public enum CaptureMode
{
    Rectangle,
    Freeform,
    Window,
    Fullscreen,
    Monitor
}

public sealed class Settings
{
    public int Version { get; set; } = 1;
    public AppTheme Theme { get; set; } = AppTheme.System;
    public CaptureMode DefaultMode { get; set; } = CaptureMode.Rectangle;
    public int DelaySeconds { get; set; }
    public bool AutoCopy { get; set; } = true;
    public bool AutoSave { get; set; } = true;
    public bool OpenEditorAfterCapture { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public string SaveDirectory { get; set; } = AppPaths.Screenshots;
    public string FileNamePattern { get; set; } = "Snip_{yyyy}-{MM}-{dd}_{HH}-{mm}-{ss}";
    public bool ShowFloatingToolbar { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool CloseToTray { get; set; }
    public bool StartMinimizedInTray { get; set; }
    public bool IncludeCursorInRecording { get; set; } = true;
    public int RecordingFps { get; set; } = 15;
    public bool AudioEnabled { get; set; }
    public string Microphone { get; set; } = "";
    public string OcrLanguage { get; set; } = "";
    public Dictionary<string, string> Hotkeys { get; set; } = HotkeyDefaults.Create();

    // Glass / window appearance
    public bool TranslucentWindow { get; set; } = true;
    public double GlassOpacity { get; set; } = 0.28;   // fraction of transparency (0 = opaque, 0.9 = very see-through); lower = more frosted/opaque
    public int GlassBlur { get; set; } = 3;            // 0 = off; 1..4 = increasing frosted blur (acrylic-like, desktop sampled behind the window)

    // Recording output
    public int RecordingResolution { get; set; } = 0;  // 0 = native, otherwise target output height (480/540/720/1080/1440)
    public bool SeparateAudio { get; set; }            // record video-only MP4 + a lossless audio file for easy editing

    // Webcam picture-in-picture
    public bool WebcamEnabled { get; set; }
    public int WebcamSize { get; set; } = 1;           // 0 = S, 1 = M, 2 = L
    public string WebcamDevice { get; set; } = "";
    public bool WebcamMirror { get; set; } = true;

    public Settings Clone()
    {
        var json = JsonSerializer.Serialize(this, SettingsService.JsonOptions);
        return JsonSerializer.Deserialize<Settings>(json, SettingsService.JsonOptions) ?? new Settings();
    }

    public void Sanitize()
    {
        if (DelaySeconds is not (0 or 3 or 5 or 10)) DelaySeconds = 0;
        if (RecordingFps is not (10 or 15 or 24 or 30 or 60)) RecordingFps = 15;
        GlassOpacity = double.IsFinite(GlassOpacity) ? Math.Clamp(GlassOpacity, 0.05, 0.9) : 0.35;
        GlassBlur = Math.Clamp(GlassBlur, 0, 4);
        if (RecordingResolution is not (0 or 480 or 540 or 720 or 1080 or 1440)) RecordingResolution = 0;
        if (WebcamSize is not (0 or 1 or 2)) WebcamSize = 1;
        if (WebcamDevice == null) WebcamDevice = "";
        WebcamDevice = WebcamDevice.Trim();
        if (WebcamDevice.Length > 160) WebcamDevice = WebcamDevice[..160];
        if (Microphone == null) Microphone = "";
        Microphone = Microphone.Trim();
        if (Microphone.Length > 160) Microphone = Microphone[..160];
        if (string.IsNullOrWhiteSpace(SaveDirectory)) SaveDirectory = AppPaths.Screenshots;
        if (string.IsNullOrWhiteSpace(FileNamePattern)) FileNamePattern = "Snip_{yyyy}-{MM}-{dd}_{HH}-{mm}-{ss}";
        FileNamePattern = SanitizeFileNamePattern(FileNamePattern);
        if (FileNamePattern.Length > 120) FileNamePattern = FileNamePattern[..120];
        Hotkeys ??= HotkeyDefaults.Create();
        foreach (var pair in HotkeyDefaults.Create())
            if (!Hotkeys.ContainsKey(pair.Key)) Hotkeys[pair.Key] = pair.Value;
        foreach (var key in Hotkeys.Keys.ToList())
            if (!HotkeyDefaults.Create().ContainsKey(key)) Hotkeys.Remove(key);
        if (!Enum.IsDefined(DefaultMode)) DefaultMode = CaptureMode.Rectangle;
        if (!Enum.IsDefined(Theme)) Theme = AppTheme.System;
    }

    private static string SanitizeFileNamePattern(string pattern)
    {
        var builder = new System.Text.StringBuilder(pattern.Length);
        foreach (char c in pattern)
            builder.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        string value = builder.ToString().Trim();
        if (value.Length == 0) value = "Snip";
        // Windows does not allow a file or folder name to end with a dot or a space.
        value = value.TrimEnd('.', ' ');
        return value.Length == 0 ? "Snip" : value;
    }
}

public static class HotkeyDefaults
{
    public const string CaptureRect = "CaptureRect";
    public const string CaptureFreeform = "CaptureFreeform";
    public const string CaptureWindow = "CaptureWindow";
    public const string CaptureFullscreen = "CaptureFullscreen";
    public const string CaptureMonitor = "CaptureMonitor";
    public const string History = "History";
    public const string Editor = "Editor";
    public const string StopRecording = "StopRecording";

    public static Dictionary<string, string> Create() => new()
    {
        [CaptureRect] = "Ctrl+Shift+S",
        [CaptureFreeform] = "Ctrl+Shift+A",
        [CaptureWindow] = "Ctrl+Shift+W",
        [CaptureFullscreen] = "Ctrl+Shift+F",
        [CaptureMonitor] = "Ctrl+Shift+M",
        [History] = "Ctrl+Shift+O",
        [Editor] = "Ctrl+Shift+E",
        [StopRecording] = "Esc"
    };

    public static string Label(string key) => key switch
    {
        CaptureRect => "Rectangular snip",
        CaptureFreeform => "Freeform snip",
        CaptureWindow => "Window snip",
        CaptureFullscreen => "Full desktop snip",
        CaptureMonitor => "Single display snip",
        History => "Open snip history",
        Editor => "Open editor",
        StopRecording => "Stop recording",
        _ => key
    };

    /// <summary>
    /// True when this shortcut is allowed to be a single bare key (no modifier).
    /// Only the stop-recording key does this, and it is only armed while a recording
    /// is active — so a bare key never shadows normal typing in other apps.
    /// </summary>
    public static bool AllowsBareKey(string key) => key == StopRecording;
}

public static class SettingsService
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    private static readonly object Gate = new();

    public static Settings Load()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                {
                    var json = File.ReadAllText(AppPaths.SettingsFile);
                    var settings = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
                    if (settings != null)
                    {
                        settings.Sanitize();
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Could not load settings; using defaults", ex);
                TryBackupBrokenSettings();
            }
            var defaults = new Settings();
            defaults.Sanitize();
            return defaults;
        }
    }

    public static bool Save(Settings settings)
    {
        lock (Gate)
        {
            try
            {
                settings.Sanitize();
                Directory.CreateDirectory(AppPaths.Data);
                var tmp = AppPaths.SettingsFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
                File.Move(tmp, AppPaths.SettingsFile, true);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Could not save settings", ex);
                return false;
            }
        }
    }

    private static void TryBackupBrokenSettings()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                File.Copy(AppPaths.SettingsFile, AppPaths.SettingsFile + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}", true);
        }
        catch { }
    }
}
