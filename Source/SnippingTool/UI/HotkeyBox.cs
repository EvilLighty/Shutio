using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SnippingTool.Core;

namespace SnippingTool.UI;

/// <summary>
/// A click-to-record keyboard shortcut field.
///
/// The current combination is rendered as plain <see cref="TextBlock"/> text (not a
/// <see cref="TextBox"/>) so it always displays, sitting right next to the action label.
/// Clicking the field enters "Press a shortcut…" mode; pressing a valid modifier+key
/// combination records it. Escape or losing focus cancels the (uncommitted) capture.
///
/// This is a <see cref="ContentControl"/> on purpose: a <see cref="TextBox"/> whose
/// template uses a <c>ScrollViewer</c> content host has a WPF quirk where text set on a
/// runtime-created control can render blank. A TextBlock has no content host, so it
/// renders reliably and can never be blank.
/// </summary>
public sealed class HotkeyBox : ContentControl
{
    private string _gesture = "";
    private bool _recording;
    private readonly Border _root;
    private readonly TextBlock _display;

    public event EventHandler? GestureChanged;

    public bool IsValid => HotkeyGesture.Parse(_gesture) != null;

    /// <summary>
    /// The stored gesture, as recorded ("Ctrl + Shift + S") or as read from defaults
    /// ("Ctrl+Shift+S"). Setting it updates the visible chip and raises <see cref="GestureChanged"/>.
    /// </summary>
    public string Gesture
    {
        get => _gesture;
        set
        {
            _gesture = value ?? "";
            RefreshDisplay();
            GestureChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Renders the stored gesture the way a human expects it (always "Ctrl + Shift + S"),
    /// regardless of whether it was recorded live or read from the defaults ("Ctrl+Shift+S").
    /// </summary>
    public static string Display(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return "";
        var parsed = HotkeyGesture.Parse(gesture);
        return parsed?.ToString() ?? gesture;
    }

    public HotkeyBox()
    {
        Focusable = true;
        IsTabStop = true;
        FocusVisualStyle = null; // the accent border (below) doubles as the focus indicator
        Cursor = Cursors.Hand;
        ToolTip = "Click, then press a shortcut such as Ctrl + Shift + S";
        MinHeight = 34;

        _display = new TextBlock
        {
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextTrimming = TextTrimming.None,
            Margin = new Thickness(11, 8, 11, 8)
        };
        var font = Application.Current?.TryFindResource("AppFont") as FontFamily;
        if (font != null) _display.FontFamily = font;

        _root = new Border
        {
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            Child = _display
        };
        // Theme-safe: these resolve dynamically so a live theme switch keeps working.
        _root.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        _root.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        Content = _root;
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        if (_recording)
        {
            _display.Text = "Press a shortcut…";
            _display.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            _root.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        }
        else if (string.IsNullOrWhiteSpace(_gesture))
        {
            _display.Text = "Not set";
            _display.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            _root.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        }
        else
        {
            _display.Text = Display(_gesture);
            _display.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            _root.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (!_recording)
        {
            _recording = true;
            RefreshDisplay();
        }
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (_recording)
        {
            _recording = false;
            RefreshDisplay();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!_recording) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Bare modifier presses just hold the modifiers down while we wait for the real key.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.System or Key.None)
        {
            e.Handled = true;
            return;
        }

        if (key == Key.Escape)
        {
            _recording = false;
            RefreshDisplay();
            e.Handled = true;
            return;
        }

        if (key == Key.Tab)
        {
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt);
        var gesture = new HotkeyGesture(modifiers, key);
        if (gesture.IsValid)
        {
            _recording = false;
            Gesture = gesture.ToString();
        }
        e.Handled = true;
    }
}
