using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SnippingTool.Core;

public sealed record HotkeyRegistration(string Action, string Gesture, bool Success, int ErrorCode, string? Error);

public sealed class HotkeyManager
{
    private readonly Dictionary<int, string> _ids = new();
    private readonly Dictionary<string, HotkeyGesture> _gestures = new();
    private IntPtr _hwnd;
    private Action<string>? _handler;
    private bool _registered;

    // The stop-recording shortcut is transient: it is armed only while a recording is in
    // progress so a bare key (e.g. Esc) never shadows typing in other apps.
    private int? _stopId;

    public IReadOnlyDictionary<string, HotkeyGesture> Gestures => _gestures;
    public IReadOnlyList<HotkeyRegistration> LastResults { get; private set; } = Array.Empty<HotkeyRegistration>();

    public void Attach(IntPtr hwnd, Action<string> handler)
    {
        _hwnd = hwnd;
        _handler = handler;
    }

    public void Detach()
    {
        UnregisterAll();
        UnregisterStopRecording();
        _hwnd = IntPtr.Zero;
        _handler = null;
    }

    public IReadOnlyList<HotkeyRegistration> RegisterAll(Dictionary<string, string> settings)
    {
        UnregisterAll();
        var results = new List<HotkeyRegistration>();
        if (_hwnd == IntPtr.Zero)
        {
            foreach (var pair in settings)
                results.Add(new HotkeyRegistration(pair.Key, pair.Value, false, -1, "Application window is not ready."));
            LastResults = results;
            return results;
        }

        int id = 0xA100;
        foreach (var pair in settings)
        {
            // Stop-recording is armed only while a recording is live (see RegisterStopRecording),
            // so it is intentionally excluded from the always-on set.
            if (pair.Key == HotkeyDefaults.StopRecording) continue;

            var gesture = HotkeyGesture.Parse(pair.Value);
            if (gesture == null)
            {
                results.Add(new HotkeyRegistration(pair.Key, pair.Value, false, -2, "Invalid shortcut."));
                continue;
            }
            if (_gestures.Values.Contains(gesture))
            {
                results.Add(new HotkeyRegistration(pair.Key, pair.Value, false, -3, "This shortcut is used by another action."));
                continue;
            }

            int currentId = id++;
            bool ok = NativeMethods.RegisterHotKey(_hwnd, currentId, gesture.NativeModifiers, gesture.VirtualKey);
            int error = ok ? 0 : Marshal.GetLastWin32Error();
            if (ok)
            {
                _ids[currentId] = pair.Key;
                _gestures[pair.Key] = gesture;
            }
            results.Add(new HotkeyRegistration(
                pair.Key,
                gesture.ToString(),
                ok,
                error,
                ok ? null : error == 1409 ? "Already in use by another application." : $"Registration failed (error {error})."));
        }

        _registered = true;
        LastResults = results;
        foreach (var r in results)
            Logger.Info($"Hotkey {r.Action}: {r.Gesture} -> {(r.Success ? "registered" : "FAILED: " + r.Error)}");
        return results;
    }

    public bool HandleHotkey(int id)
    {
        string? action = null;
        if (_stopId.HasValue && id == _stopId.Value) action = HotkeyDefaults.StopRecording;
        else if (_ids.TryGetValue(id, out var a)) action = a;
        if (action != null)
        {
            try { _handler?.Invoke(action); }
            catch (Exception ex) { Logger.Error($"Hotkey action {action} failed", ex); }
            return true;
        }
        return false;
    }

    public void UnregisterAll()
    {
        if (_hwnd != IntPtr.Zero)
        {
            foreach (var id in _ids.Keys)
            {
                try { NativeMethods.UnregisterHotKey(_hwnd, id); }
                catch { }
            }
        }
        _ids.Clear();
        _gestures.Clear();
        _registered = false;
    }

    /// <summary>
    /// Arms the stop-recording shortcut. It may be a single bare key (e.g. Esc) or a
    /// modifier+key combo. Returns false if the gesture is invalid, collides with another
    /// shortcut, or Windows rejects it — the indicator's stop button always remains a fallback.
    /// </summary>
    public bool RegisterStopRecording(string? gesture)
    {
        UnregisterStopRecording();
        if (_hwnd == IntPtr.Zero) return false;
        var parsed = HotkeyGesture.Parse(gesture, allowBare: true);
        if (parsed == null) { Logger.Warn("Stop-recording: not a valid shortcut: " + gesture); return false; }
        if (_gestures.Values.Contains(parsed)) { Logger.Warn("Stop-recording: collides with another shortcut"); return false; }

        int id = 0xB001;
        if (NativeMethods.RegisterHotKey(_hwnd, id, parsed.NativeModifiers, parsed.VirtualKey))
        {
            _stopId = id;
            Logger.Info($"Hotkey stop-recording: {parsed} -> registered");
            return true;
        }
        int error = Marshal.GetLastWin32Error();
        Logger.Warn($"Stop-recording: registration failed (error {error}); the stop button still works.");
        return false;
    }

    public void UnregisterStopRecording()
    {
        if (_stopId.HasValue && _hwnd != IntPtr.Zero)
        {
            try { NativeMethods.UnregisterHotKey(_hwnd, _stopId.Value); } catch { }
        }
        _stopId = null;
    }

    public bool AnyRegistered => _ids.Count > 0;
    public bool IsRegistered => _registered;
}
