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
        if (_ids.TryGetValue(id, out var action))
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

    public bool AnyRegistered => _ids.Count > 0;
    public bool IsRegistered => _registered;
}
