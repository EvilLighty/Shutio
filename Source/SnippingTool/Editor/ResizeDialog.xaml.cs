using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SnippingTool.Core;
using SnippingTool.UI;

namespace SnippingTool.Editor;

public partial class ResizeDialog : Window
{
    private readonly int _originalWidth;
    private readonly int _originalHeight;
    private bool _updating;
    private bool _presetClick;

    public int ResultWidth { get; private set; }
    public int ResultHeight { get; private set; }

    public ResizeDialog(int width, int height)
    {
        InitializeComponent();
        _originalWidth = Math.Max(1, width);
        _originalHeight = Math.Max(1, height);
        OriginalText.Text = $"Original: {_originalWidth} × {_originalHeight} px";
        WidthBox.Text = _originalWidth.ToString();
        HeightBox.Text = _originalHeight.ToString();
        Loaded += (_, _) =>
        {
            ThemeManager.ApplyWindowChrome(this);
            WidthBox.Focus();
            WidthBox.SelectAll();
            Preset100.IsChecked = true;
            UpdateEstimate();
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
        Preset25.Click += (_, _) => ApplyPreset(0.25, Preset25);
        Preset50.Click += (_, _) => ApplyPreset(0.50, Preset50);
        Preset75.Click += (_, _) => ApplyPreset(0.75, Preset75);
        Preset100.Click += (_, _) => ApplyPreset(1.0, Preset100);
        Preset150.Click += (_, _) => ApplyPreset(1.5, Preset150);
        Preset200.Click += (_, _) => ApplyPreset(2.0, Preset200);
    }

    private void ApplyPreset(double factor, ToggleButton clicked)
    {
        _updating = true;
        WidthBox.Text = Math.Max(1, (int)Math.Round(_originalWidth * factor)).ToString();
        HeightBox.Text = Math.Max(1, (int)Math.Round(_originalHeight * factor)).ToString();
        _updating = false;
        foreach (var toggle in new[] { Preset25, Preset50, Preset75, Preset100, Preset150, Preset200 })
            if (toggle != clicked) toggle.IsChecked = false;
        clicked.IsChecked = true;
        UpdateEstimate();
    }

    private void Dimension_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || !IsLoaded) return;
        UpdateEstimate();
        if (AspectCheck.IsChecked != true) return;
        if (sender == WidthBox && int.TryParse(WidthBox.Text, out int width) && width > 0)
        {
            _updating = true;
            HeightBox.Text = Math.Max(1, (int)Math.Round((double)width * _originalHeight / _originalWidth)).ToString();
            _updating = false;
        }
        else if (sender == HeightBox && int.TryParse(HeightBox.Text, out int height) && height > 0)
        {
            _updating = true;
            WidthBox.Text = Math.Max(1, (int)Math.Round((double)height * _originalWidth / _originalHeight)).ToString();
            _updating = false;
        }
    }

    private void UpdateEstimate()
    {
        if (int.TryParse(WidthBox.Text, out int w) && int.TryParse(HeightBox.Text, out int h) && w > 0 && h > 0)
        {
            double mp = w * (double)h / 1_000_000.0;
            EstimateText.Text = $"{w} × {h} px  ·  {mp:0.00} MP";
            EstimateText.Foreground = (Brush)FindResource("TextTertiaryBrush");
        }
        else
        {
            EstimateText.Text = "Enter a valid size";
            EstimateText.Foreground = (Brush)FindResource("DangerBrush");
        }
    }

    private void Resize_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(WidthBox.Text, out int w) || !int.TryParse(HeightBox.Text, out int h) ||
            w < 1 || h < 1 || w > 16000 || h > 16000)
        {
            MessageDialog.Warning(this, "Invalid size", "Width and height must be between 1 and 16,000 pixels.");
            return;
        }
        if ((long)w * h > 120_000_000)
        {
            MessageDialog.Warning(this, "Image too large", "That image would require too much memory. Choose a smaller size.");
            return;
        }
        ResultWidth = w;
        ResultHeight = h;
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

