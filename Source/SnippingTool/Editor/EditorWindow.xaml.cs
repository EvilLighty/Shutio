using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SnippingTool.Core;
using SnippingTool.Imaging;
using SnippingTool.Storage;
using SnippingTool.UI;
using CaptureMode = SnippingTool.Core.CaptureMode;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Color = System.Windows.Media.Color;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WinForms = System.Windows.Forms;

namespace SnippingTool.Editor;

public partial class EditorWindow : Window
{
    private readonly Settings _settings;
    private readonly HistoryService _history;
    private readonly CaptureMode _mode;
    private SnipEntry? _entry;
    private bool _dirty;
    private bool _initializing = true;
    private bool _closed;
    private bool _allowClose;
    private bool _ocrBusy;
    private string _ocrLanguage;
    private Color _penColor = Color.FromRgb(255, 59, 48);
    private double _penThickness = 4;

    private static readonly Color[] Palette =
    {
        Color.FromRgb(255, 59, 48), Color.FromRgb(255, 149, 0), Color.FromRgb(255, 204, 0),
        Color.FromRgb(52, 199, 89), Color.FromRgb(0, 199, 190), Color.FromRgb(0, 122, 255),
        Color.FromRgb(88, 86, 214), Color.FromRgb(175, 82, 222), Color.FromRgb(255, 45, 85),
        Color.FromRgb(255, 255, 255), Color.FromRgb(0, 0, 0), Color.FromRgb(142, 142, 147)
    };

    public EditorWindow(System.Drawing.Bitmap image, CaptureMode mode, SnipEntry? entry, Settings settings, HistoryService history)
    {
        InitializeComponent();
        _settings = settings;
        _history = history;
        _mode = mode;
        _entry = entry;
        _ocrLanguage = settings.OcrLanguage;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));

        Canvas.HistoryChanged += (_, _) => UpdateHistoryButtons();
        Canvas.ZoomChanged += (_, _) => UpdateZoom();
        Canvas.ToolChanged += (_, _) => UpdateToolState();
        Canvas.ImageChanged += (_, _) =>
        {
            UpdateDimensions();
            if (!_initializing) MarkDirty();
        };

        BuildColorSwatches();
        BuildThicknessOptions();
        BuildZoomOptions();
        Canvas.PenColor = _penColor;
        Canvas.PenThickness = _penThickness;
        Canvas.LoadBitmap(Imaging.BitmapUtils.Clone(image));
        _initializing = false;
        _dirty = false;
        UpdateFileName();
        UpdateDimensions();
        UpdateZoom();
        UpdateHistoryButtons();
        SetTool(EditorTool.Pen);
        UpdateSwatch();
        UpdateThicknessDot();
        OcrLanguageText.Text = OcrService.IsAvailable ? "Windows OCR is ready" : "Windows OCR is not available";
        OcrLanguageButton.Content = "Language";
        StatusText.Text = "Ready";

        Loaded += (_, _) =>
        {
            ThemeManager.ApplyWindowChrome(this);
            UpdateZoom();
            Keyboard.Focus(Canvas);
        };
        StateChanged += (_, _) =>
        {
            MaximizeButton.Style = WindowState == WindowState.Maximized
                ? (System.Windows.Style)TryFindResource("TitleBarRestoreButton")
                : (System.Windows.Style)TryFindResource("TitleBarMaxButton");
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
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void BuildColorSwatches()
    {
        foreach (var color in Palette)
        {
            var button = new Button
            {
                Style = (Style)FindResource("ColorSwatchButton"),
                Background = new SolidColorBrush(color),
                Tag = color,
                ToolTip = $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            };
            button.Click += (_, _) =>
            {
                _penColor = color;
                Canvas.PenColor = color;
                UpdateSwatch();
                ColorPopup.IsOpen = false;
                UpdateToolStatus();
            };
            ColorSwatches.Children.Add(button);
        }
        var custom = new Button
        {
            Content = "Custom…",
            Style = (Style)FindResource("SecondaryButton"),
            Height = 30,
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = 11,
            Margin = new Thickness(2, 5, 2, 0)
        };
        custom.Click += (_, _) => PickCustomColor();
        ColorSwatches.Children.Add(custom);
    }

    private void PickCustomColor()
    {
        using var dialog = new WinForms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(_penColor.R, _penColor.G, _penColor.B)
        };
        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            _penColor = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
            Canvas.PenColor = _penColor;
            UpdateSwatch();
            ColorPopup.IsOpen = false;
            UpdateToolStatus();
        }
    }

    private void BuildThicknessOptions()
    {
        foreach (var thickness in new double[] { 2, 4, 7, 11, 16, 22 })
        {
            double size = Math.Clamp(4 + thickness * 0.55, 5, 19);
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = size,
                Height = size,
                Fill = (WpfBrush)FindResource("TextPrimaryBrush")
            };
            var button = new Button
            {
                Content = dot,
                Style = (Style)FindResource("ColorSwatchButton"),
                Background = WpfBrushes.Transparent,
                Tag = thickness,
                ToolTip = $"{thickness:0} px"
            };
            button.Click += (_, _) =>
            {
                _penThickness = thickness;
                Canvas.PenThickness = thickness;
                UpdateThicknessDot();
                UpdateThicknessSelection();
                ColorPopup.IsOpen = false;
                UpdateToolStatus();
            };
            ThicknessOptions.Children.Add(button);
        }
        UpdateThicknessSelection();
    }

    private void UpdateThicknessSelection()
    {
        foreach (var child in ThicknessOptions.Children.OfType<Button>())
        {
            bool selected = child.Tag is double d && Math.Abs(d - _penThickness) < 0.1;
            child.BorderBrush = selected ? (WpfBrush)FindResource("AccentBrush") : WpfBrushes.Transparent;
            child.BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    private void UpdateSwatch()
    {
        ColorSwatch.Fill = new SolidColorBrush(_penColor);
        ColorSwatch.Stroke = _penColor == Color.FromRgb(255, 255, 255)
            ? new SolidColorBrush(Color.FromArgb(120, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(128, 255, 255, 255));
        foreach (var child in ColorSwatches.Children.OfType<Button>())
        {
            if (child.Tag is not Color color) continue;
            bool selected = color == _penColor;
            child.BorderBrush = selected ? (WpfBrush)FindResource("AccentBrush") : WpfBrushes.Transparent;
            child.BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    private void UpdateThicknessDot()
    {
        double size = Math.Clamp(4 + _penThickness * 0.5, 4, 11);
        ThicknessDot.Width = size;
        ThicknessDot.Height = size;
    }

    private void BuildZoomOptions()
    {
        foreach (var percent in new[] { 25, 50, 100, 150, 200, 400 })
        {
            var item = new Button
            {
                Content = percent + "%",
                Style = (Style)FindResource("GhostButton"),
                MinWidth = 120,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(11, 7, 11, 7)
            };
            double value = percent;
            item.Click += (_, _) =>
            {
                Canvas.SetZoomPercent(value);
                ZoomPopup.IsOpen = false;
            };
            ZoomOptions.Children.Add(item);
        }
        var fit = new Button
        {
            Content = "Fit to window",
            Style = (Style)FindResource("GhostButton"),
            MinWidth = 120,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(11, 7, 11, 7)
        };
        fit.Click += (_, _) =>
        {
            Canvas.FitToWindow();
            ZoomPopup.IsOpen = false;
        };
        ZoomOptions.Children.Add(new Border { Height = 1, Background = (WpfBrush)FindResource("BorderBrush"), Margin = new Thickness(5, 4, 5, 4) });
        ZoomOptions.Children.Add(fit);
    }

    private void SetTool(EditorTool tool)
    {
        Canvas.Tool = tool;
        UpdateToolState();
    }

    private void UpdateToolState()
    {
        var tool = Canvas.Tool;
        PenTool.IsChecked = tool == EditorTool.Pen;
        HighlighterTool.IsChecked = tool == EditorTool.Highlighter;
        EraserTool.IsChecked = tool == EditorTool.Eraser;
        LineTool.IsChecked = tool == EditorTool.Line;
        CropTool.IsChecked = tool == EditorTool.Crop;
        CropHint.Visibility = tool == EditorTool.Crop ? Visibility.Visible : Visibility.Collapsed;
        UpdateToolStatus();
    }

    private void UpdateToolStatus()
    {
        ToolStatusText.Text = Canvas.Tool switch
        {
            EditorTool.Pen => $"Pen · {_penThickness:0} px",
            EditorTool.Highlighter => $"Highlighter · {_penThickness:0} px",
            EditorTool.Eraser => "Eraser",
            EditorTool.Line => $"Line · {_penThickness:0} px",
            EditorTool.Crop => "Crop",
            _ => "Tool"
        };
    }

    private void UpdateHistoryButtons()
    {
        UndoButton.IsEnabled = Canvas.CanUndo;
        RedoButton.IsEnabled = Canvas.CanRedo;
    }

    private void UpdateZoom()
    {
        string text = $"{Canvas.ZoomPercent:0}%";
        ZoomText.Text = text;
        ZoomStatusText.Text = text;
    }

    private void UpdateDimensions()
    {
        var size = Canvas.ImageSize;
        DimensionsText.Text = size.Width > 0 ? $"{size.Width:0} × {size.Height:0} px" : "";
    }

    private void UpdateFileName()
    {
        string name = _entry != null && !string.IsNullOrEmpty(_entry.FilePath)
            ? Path.GetFileName(_entry.FilePath)
            : "Untitled snip";
        FileNameText.Text = name;
        Title = "Shutio — " + name;
    }

    private void MarkDirty()
    {
        if (_closed) return;
        _dirty = true;
        DirtyDot.Visibility = Visibility.Visible;
        StatusText.Text = "Edited";
    }

    private void MarkClean(string status)
    {
        _dirty = false;
        DirtyDot.Visibility = Visibility.Collapsed;
        StatusText.Text = status;
    }

    private void PenTool_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Pen);
    private void HighlighterTool_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Highlighter);
    private void EraserTool_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Eraser);
    private void LineTool_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Line);
    private void CropTool_Click(object sender, RoutedEventArgs e) => SetTool(EditorTool.Crop);

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        ColorPopup.IsOpen = true;
    }

    private void ThicknessButton_Click(object sender, RoutedEventArgs e)
    {
        ColorPopup.IsOpen = true;
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        Canvas.Undo();
        UpdateHistoryButtons();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        Canvas.Redo();
        UpdateHistoryButtons();
    }

    private void RotateLeft_Click(object sender, RoutedEventArgs e) => Canvas.Rotate(false);
    private void RotateRight_Click(object sender, RoutedEventArgs e) => Canvas.Rotate(true);

    private void Resize_Click(object sender, RoutedEventArgs e)
    {
        var size = Canvas.ImageSize;
        if (size.Width <= 0) return;
        var dialog = new ResizeDialog((int)size.Width, (int)size.Height) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            Canvas.ResizeImage(dialog.ResultWidth, dialog.ResultHeight);
            MarkDirty();
        }
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Canvas.ZoomOut();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Canvas.ZoomIn();
    private void Fit_Click(object sender, RoutedEventArgs e) => Canvas.FitToWindow();
    private void ZoomButton_Click(object sender, RoutedEventArgs e) => ZoomPopup.IsOpen = true;

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyImage();

    private void CopyImage()
    {
        try
        {
            using var flattened = Canvas.Flatten();
            if (ClipboardService.SetImage(flattened))
                StatusText.Text = "Copied to clipboard";
            else
                MessageDialog.Warning(this, "Clipboard unavailable", "Windows did not allow access to the clipboard. Try again in a moment.");
        }
        catch (Exception ex)
        {
            Logger.Error("Copy failed", ex);
            MessageDialog.Error(this, "Could not copy", ex.Message);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) => Save(false);

    private void SaveAsButton_Click(object sender, RoutedEventArgs e) => SaveMenu.IsOpen = true;

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        SaveMenu.IsOpen = false;
        Save(true);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        SaveMenu.IsOpen = false;
        string? folder = _entry != null ? Path.GetDirectoryName(_entry.FilePath) : _settings.SaveDirectory;
        if (!string.IsNullOrEmpty(folder))
        {
            try
            {
                if (_entry != null && File.Exists(_entry.FilePath))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_entry.FilePath}\"") { UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageDialog.Error(this, "Could not open folder", ex.Message);
            }
        }
    }

    private bool Save(bool forceSaveAs)
    {
        try
        {
            System.Drawing.Bitmap flattened;
            try { flattened = Canvas.Flatten(); }
            catch (Exception ex)
            {
                MessageDialog.Error(this, "Could not render the image", ex.Message);
                return false;
            }
            using (flattened)
            {
                string? path = !forceSaveAs && _entry != null && !string.IsNullOrEmpty(_entry.FilePath) &&
                               File.Exists(_entry.FilePath)
                    ? _entry.FilePath
                    : null;

                if (path == null)
                {
                    var proposed = _entry != null && !string.IsNullOrEmpty(_entry.FilePath)
                        ? Path.GetFileName(_entry.FilePath)
                        : Path.GetFileName(CaptureStorage.GenerateFilePath(_settings, _mode));
                    var dialog = new SaveFileDialog
                    {
                        Title = "Save snip",
                        Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg;*.jpeg)|*.jpg;*.jpeg|Bitmap image (*.bmp)|*.bmp|All files (*.*)|*.*",
                        DefaultExt = "png",
                        AddExtension = true,
                        FileName = proposed,
                        InitialDirectory = Directory.Exists(_settings.SaveDirectory) ? _settings.SaveDirectory : AppPaths.Screenshots,
                        OverwritePrompt = true
                    };
                    if (dialog.ShowDialog(this) != true)
                        return false;
                    path = dialog.FileName;
                }

                BitmapUtils.Save(flattened, path);
                if (_entry != null)
                {
                    _entry.FilePath = path;
                    _history.Update(_entry, flattened, true);
                }
                else
                {
                    _entry = CaptureStorage.RegisterSavedFile(path, _mode, flattened, _history);
                }
                MarkClean("Saved");
                UpdateFileName();
                Logger.Info($"Editor saved {path} ({flattened.Width}x{flattened.Height}).");
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Save failed", ex);
            MessageDialog.Error(this, "Could not save image", ex.Message);
            return false;
        }
    }

    private async void OcrButton_Click(object sender, RoutedEventArgs e)
    {
        if (_ocrBusy) return;
        if (!OcrService.IsAvailable)
        {
            MessageDialog.Warning(this, "OCR unavailable", "No Windows OCR language is installed for this account.");
            return;
        }
        _ocrBusy = true;
        OcrPanel.Visibility = Visibility.Visible;
        OcrProgress.Visibility = Visibility.Visible;
        OcrButton.IsEnabled = false;
        OcrCopyButton.IsEnabled = false;
        StatusText.Text = "Reading text…";
        try
        {
            using var flattened = Canvas.Flatten();
            var result = await OcrService.RecognizeAsync(flattened, _ocrLanguage);
            if (_closed) return;
            OcrTextBox.Text = string.IsNullOrWhiteSpace(result.Text) ? "No text was found in this snip." : result.Text;
            OcrCountText.Text = $"{result.Text.Length} characters";
            OcrLanguageText.Text = $"Windows OCR · {result.Language}";
            OcrCopyButton.IsEnabled = result.Text.Length > 0;
            StatusText.Text = "Text extracted";
        }
        catch (Exception ex)
        {
            Logger.Error("OCR failed", ex);
            if (!_closed)
            {
                OcrTextBox.Text = "Text could not be extracted.";
                OcrCountText.Text = "";
                OcrLanguageText.Text = "Windows OCR";
                MessageDialog.Error(this, "OCR failed", ex.Message);
            }
        }
        finally
        {
            if (!_closed)
            {
                OcrProgress.Visibility = Visibility.Collapsed;
                OcrButton.IsEnabled = true;
                _ocrBusy = false;
            }
        }
    }

    private void OcrClose_Click(object sender, RoutedEventArgs e) => OcrPanel.Visibility = Visibility.Collapsed;

    private void OcrCopy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(OcrTextBox.Text) || OcrTextBox.Text == "No text was found in this snip.") return;
        if (ClipboardService.SetText(OcrTextBox.Text))
            StatusText.Text = "OCR text copied";
    }

    private void OcrLanguage_Click(object sender, RoutedEventArgs e)
    {
        var languages = OcrService.GetLanguages();
        if (languages.Count == 0)
        {
            MessageDialog.Warning(this, "OCR unavailable", "No OCR languages are available.");
            return;
        }
        var menu = new ContextMenu { PlacementTarget = OcrLanguageButton };
        foreach (var language in languages)
        {
            var item = new MenuItem { Header = language.DisplayName, IsChecked = language.Tag == _ocrLanguage, Tag = language.Tag };
            item.Click += (_, _) =>
            {
                _ocrLanguage = language.Tag;
                _settings.OcrLanguage = language.Tag;
                SettingsService.Save(_settings);
                OcrLanguageText.Text = $"Windows OCR · {language.DisplayName}";
                StatusText.Text = "OCR language set to " + language.DisplayName;
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (control && e.Key == Key.S)
        {
            Save(shift);
            e.Handled = true;
        }
        else if (control && e.Key == Key.C && !(Keyboard.FocusedElement is TextBoxBase))
        {
            CopyImage();
            e.Handled = true;
        }
        else if (control && e.Key == Key.Z && !(Keyboard.FocusedElement is TextBoxBase))
        {
            Canvas.Undo();
            UpdateHistoryButtons();
            e.Handled = true;
        }
        else if (control && (e.Key == Key.Y || (shift && e.Key == Key.Z)))
        {
            Canvas.Redo();
            UpdateHistoryButtons();
            e.Handled = true;
        }
        else if (control && e.Key == Key.D0)
        {
            Canvas.FitToWindow();
            e.Handled = true;
        }
        else if (control && (e.Key == Key.OemPlus || e.Key == Key.Add))
        {
            Canvas.ZoomIn();
            e.Handled = true;
        }
        else if (control && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
        {
            Canvas.ZoomOut();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (OcrPanel.Visibility == Visibility.Visible)
            {
                OcrPanel.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
            else if (Canvas.Tool == EditorTool.Crop)
            {
                SetTool(EditorTool.Pen);
                e.Handled = true;
            }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Maximize_Click(sender, e);
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            base.OnClosing(e);
            return;
        }

        if (!_dirty)
        {
            _allowClose = true;
            base.OnClosing(e);
            return;
        }

        if (_entry != null && File.Exists(_entry.FilePath))
        {
            // Match Shutio's auto-save behaviour when the snip already has a file.
            if (Save(false))
            {
                _allowClose = true;
                base.OnClosing(e);
                return;
            }
        }

        int choice = MessageDialog.Show(this, "Save changes?", "This snip has unsaved edits. Save it before closing?",
            DialogKind.Question, new[] { "Save", "Discard", "Cancel" }, 2);
        if (choice == 0)
        {
            if (Save(false))
            {
                _allowClose = true;
                base.OnClosing(e);
                return;
            }
        }
        else if (choice == 1)
        {
            _allowClose = true;
            base.OnClosing(e);
            return;
        }
        e.Cancel = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        Canvas.DisposeCanvas();
        base.OnClosed(e);
    }
}
