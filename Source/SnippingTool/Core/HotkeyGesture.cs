using System.Windows.Input;

namespace SnippingTool.Core;

public sealed class HotkeyGesture : IEquatable<HotkeyGesture>
{
    public ModifierKeys Modifiers { get; }
    public Key Key { get; }

    public HotkeyGesture(ModifierKeys modifiers, Key key)
    {
        Modifiers = modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt);
        Key = key;
    }

    public bool HasKey =>
        Key != Key.None &&
        Key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.System);

    public bool HasModifiers => Modifiers != ModifierKeys.None;

    /// <summary>A valid, always‑on shortcut must carry a modifier so it can't shadow a normal key.</summary>
    public bool IsValid => HasKey && HasModifiers;

    public uint NativeModifiers
    {
        get
        {
            uint mods = NativeMethods.MOD_NOREPEAT;
            if (Modifiers.HasFlag(ModifierKeys.Alt)) mods |= NativeMethods.MOD_ALT;
            if (Modifiers.HasFlag(ModifierKeys.Control)) mods |= NativeMethods.MOD_CONTROL;
            if (Modifiers.HasFlag(ModifierKeys.Shift)) mods |= NativeMethods.MOD_SHIFT;
            return mods;
        }
    }

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    // Friendly names that users type but that are not exact Key enum names
    // (Enum.TryParse is exact + case-insensitive, so "Esc" alone would not resolve to Escape).
    private static readonly Dictionary<string, Key> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["esc"] = Key.Escape,
        ["space"] = Key.Space,
        ["tab"] = Key.Tab,
        ["backspace"] = Key.Back,
        ["delete"] = Key.Delete,
        ["return"] = Key.Return,
        ["up"] = Key.Up,
        ["down"] = Key.Down,
        ["left"] = Key.Left,
        ["right"] = Key.Right,
        ["home"] = Key.Home,
        ["end"] = Key.End,
    };

    private static bool TryResolveKey(string part, out Key key)
    {
        key = Key.None;
        return KeyAliases.TryGetValue(part.Trim(), out key);
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    public static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (key - Key.NumPad0),
        Key.Escape => "Esc",
        Key.Space => "Space",
        Key.Tab => "Tab",
        Key.Oem3 => "`",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemOpenBrackets => "[",
        Key.Oem6 => "]",
        Key.Oem5 => "\\",
        Key.Oem1 => ";",
        Key.OemQuotes => "'",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.Snapshot => "Print Screen",
        Key.Next => "Page Down",
        Key.Prior => "Page Up",
        _ => key.ToString()
    };

    public static HotkeyGesture? Parse(string? text, bool allowBare = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var modifiers = ModifierKeys.None;
        Key key = Key.None;
        foreach (var rawPart in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = rawPart.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "win":
                case "windows":
                    break;
                default:
                    if (part.Length == 1 && char.IsDigit(part[0]))
                        key = (Key)((int)Key.D0 + (part[0] - '0'));
                    else if (part.Length == 1 && char.IsLetter(part[0]))
                        key = (Key)((int)Key.A + (char.ToUpperInvariant(part[0]) - 'A'));
                    else if (Enum.TryParse<Key>(part.Replace(" ", ""), true, out var parsed))
                        key = parsed;
                    else if (TryResolveKey(part, out var aliased))
                        key = aliased;
                    break;
            }
        }
        var gesture = new HotkeyGesture(modifiers, key);
        if (!gesture.HasKey) return null;
        if (!allowBare && !gesture.HasModifiers) return null;
        return gesture;
    }

    public bool Equals(HotkeyGesture? other) => other != null && other.Modifiers == Modifiers && other.Key == Key;
    public override bool Equals(object? obj) => obj is HotkeyGesture g && Equals(g);
    public override int GetHashCode() => HashCode.Combine(Modifiers, Key);
}
