using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SnippingTool.Core;

namespace SnippingTool.UI;

public enum DialogKind { Info, Warning, Error, Question }

public partial class MessageDialog : Window
{
    public int Result { get; private set; } = -1;
    private readonly int _cancelIndex;
    private readonly bool _completed;

    public MessageDialog(string heading, string message, DialogKind kind, IReadOnlyList<string> buttonLabels, int cancelIndex = -1)
    {
        InitializeComponent();
        HeadingText.Text = heading;
        MessageText.Text = message;
        _cancelIndex = cancelIndex;
        TitleLabel.Text = heading;
        IconGlyph.Text = kind switch
        {
            DialogKind.Error => "\uE783",
            DialogKind.Warning => "\uE7BA",
            DialogKind.Question => "\uE897",
            _ => "\uE946"
        };
        if (kind == DialogKind.Error)
        {
            IconBubble.Background = (Brush)FindResource("DangerSoftBrush");
            IconGlyph.Foreground = (Brush)FindResource("DangerBrush");
        }
        BuildButtons(buttonLabels);
        Loaded += (_, _) => { ThemeManager.ApplyWindowChrome(this); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CloseWith(_cancelIndex >= 0 ? _cancelIndex : buttonLabels.Count - 1);
                e.Handled = true;
            }
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

    private void BuildButtons(IReadOnlyList<string> labels)
    {
        for (int i = 0; i < labels.Count; i++)
        {
            var style = (Style)Application.Current.FindResource(i == 0 ? "PrimaryButton" : i == labels.Count - 1 ? "GhostButton" : "SecondaryButton");
            var button = new Button
            {
                Content = labels[i],
                Style = style,
                MinWidth = 88,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = i == 0,
                IsCancel = i == _cancelIndex
            };
            int index = i;
            button.Click += (_, _) => CloseWith(index);
            ButtonsHost.Children.Add(button);
        }
    }

    private void CloseWith(int index)
    {
        Result = index;
        try { DialogResult = index >= 0; } catch { }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => CloseWith(_cancelIndex >= 0 ? _cancelIndex : -1);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1 && e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    public static void Info(Window? owner, string heading, string message) =>
        Show(owner, heading, message, DialogKind.Info, new[] { "OK" }, 0);

    public static void Error(Window? owner, string heading, string message) =>
        Show(owner, heading, message, DialogKind.Error, new[] { "OK" }, 0);

    public static void Warning(Window? owner, string heading, string message) =>
        Show(owner, heading, message, DialogKind.Warning, new[] { "OK" }, 0);

    public static bool Confirm(Window? owner, string heading, string message, string confirmLabel = "Continue",
        string cancelLabel = "Cancel")
    {
        var result = Show(owner, heading, message, DialogKind.Question, new[] { confirmLabel, cancelLabel }, 1);
        return result == 0;
    }

    public static int Show(Window? owner, string heading, string message, DialogKind kind,
        IReadOnlyList<string> buttons, int cancelIndex = -1)
    {
        var dialog = new MessageDialog(heading, message, kind, buttons, cancelIndex);
        if (owner != null && owner.IsVisible)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Topmost = true;
        }
        dialog.ShowDialog();
        return dialog.Result;
    }
}
