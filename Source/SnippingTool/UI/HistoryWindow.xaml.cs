using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnippingTool.Core;
using SnippingTool.Imaging;
using SnippingTool.Storage;
using CaptureMode = SnippingTool.Core.CaptureMode;

namespace SnippingTool.UI;

public partial class HistoryWindow : Window
{
    private readonly AppHost _host;
    private readonly Dictionary<string, BitmapSource> _thumbCache = new();
    private readonly Dictionary<ToggleButton, CaptureMode?> _filters = new();
    private List<SnipEntry> _all = new();
    private CaptureMode? _filterMode;
    private bool _bulk;

    public HistoryWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));

        _filters[FilterAll] = null;
        _filters[FilterRectangle] = CaptureMode.Rectangle;
        _filters[FilterFreeform] = CaptureMode.Freeform;
        _filters[FilterWindow] = CaptureMode.Window;
        _filters[FilterFullscreen] = CaptureMode.Fullscreen;
        _filters[FilterMonitor] = CaptureMode.Monitor;
        foreach (var pair in _filters)
        {
            var toggle = pair.Key;
            toggle.Click += (_, _) => SetFilter(pair.Value);
        }

        SearchBox.TextChanged += (_, _) => Reload();
        OpenFolderButton.Click += (_, _) => OpenFolder();
        DoneButton.Click += (_, _) => Close();
        ClearButton.Click += (_, _) => ClearAll();
        Loaded += (_, _) =>
        {
            ThemeManager.ApplyWindowChrome(this);
            FilterAll.IsChecked = true;
            _host.History.Changed += OnHistoryChanged;
            Reload();
        };
        Closed += (_, _) => _host.History.Changed -= OnHistoryChanged;
        SourceInitialized += (_, _) => ThemeManager.ApplyWindowChrome(this);
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        if (_bulk) return;
        Dispatcher.BeginInvoke(Reload);
    }

    private void SetFilter(CaptureMode? mode)
    {
        _filterMode = mode;
        foreach (var pair in _filters)
            pair.Key.IsChecked = pair.Value == mode;
        Reload();
    }

    private void Reload()
    {
        _all = _host.History.All.ToList();
        CardsHost.Children.Clear();

        string query = SearchBox.Text.Trim();
        var filtered = _all.Where(entry =>
        {
            if (_filterMode != null && !string.Equals(entry.Mode, _filterMode.Value.ToString(), StringComparison.OrdinalIgnoreCase))
                return false;
            if (query.Length == 0) return true;
            return entry.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   entry.ModeLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   entry.CreatedLocal.ToString("g").Contains(query, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        CountText.Text = _all.Count == 1 ? "1 snip" : $"{_all.Count} snips";
        StatusText.Text = _all.Count == 0
            ? $"Snips are saved to {_host.Settings.SaveDirectory}"
            : $"Showing {filtered.Count} of {_all.Count} · {_host.Settings.SaveDirectory}";

        bool anything = _all.Count > 0;
        EmptyState.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (filtered.Count == 0)
        {
            EmptyTitle.Text = anything ? "No matching snips" : "No snips yet";
            EmptyBody.Text = anything
                ? "Try a different search or filter."
                : "Take a snip and it will appear here.";
        }

        foreach (var entry in filtered)
            CardsHost.Children.Add(BuildCard(entry));
    }

    private Border BuildCard(SnipEntry entry)
    {
        var card = new Border
        {
            Width = 310,
            Height = 234,
            Margin = new Thickness(0, 0, 12, 12),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            ClipToBounds = true
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(158) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var imageHost = new Border
        {
            CornerRadius = new CornerRadius(11, 11, 0, 0),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromRgb(24, 25, 28))
        };
        var image = new Image { Stretch = Stretch.UniformToFill };
        try
        {
            if (!string.IsNullOrEmpty(entry.ThumbnailPath) && File.Exists(entry.ThumbnailPath))
            {
                if (!_thumbCache.TryGetValue(entry.ThumbnailPath, out var source))
                {
                    source = BitmapUtils.LoadBitmapSource(entry.ThumbnailPath);
                    _thumbCache[entry.ThumbnailPath] = source;
                }
                image.Source = source;
            }
        }
        catch { }
        imageHost.Child = image;
        imageHost.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) OpenEntry(entry);
        };
        grid.Children.Add(imageHost);

        var pill = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(9),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(8, 3, 8, 3),
            Background = new SolidColorBrush(Color.FromArgb(200, 20, 21, 24))
        };
        pill.Child = new TextBlock { Text = entry.ModeLabel, FontSize = 10.5, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold };
        grid.Children.Add(pill);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 7, 7, 0)
        };
        actions.Children.Add(SmallAction("Open in editor", "\uE70F", () => OpenEntry(entry)));
        actions.Children.Add(SmallAction("Copy image", "\uE8C8", () => CopyEntry(entry)));
        actions.Children.Add(SmallAction("Delete", "\uE74D", () => DeleteEntry(entry)));
        grid.Children.Add(actions);

        var info = new Grid { Margin = new Thickness(11, 8, 11, 8) };
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var name = new TextBlock
        {
            Text = entry.FileName,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = entry.FilePath
        };
        info.Children.Add(name);

        var meta = new TextBlock
        {
            Text = $"{entry.Dimensions} · {entry.DisplayTime}",
            FontSize = 10.5,
            Margin = new Thickness(0, 3, 0, 0)
        };
        meta.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        Grid.SetRow(meta, 1);
        info.Children.Add(meta);

        var open = new Button
        {
            Content = "Edit",
            Style = (Style)FindResource("LinkButton"),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 2, 6, 2)
        };
        open.Click += (_, _) => OpenEntry(entry);
        Grid.SetColumn(open, 1);
        Grid.SetRowSpan(open, 2);
        info.Children.Add(open);

        Grid.SetRow(info, 1);
        grid.Children.Add(info);
        card.Child = grid;
        return card;
    }

    private static Button SmallAction(string tip, string glyph, Action action)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe Fluent Icons"), FontSize = 12 },
            Width = 29,
            Height = 29,
            Margin = new Thickness(4, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(190, 20, 21, 24)),
            Foreground = Brushes.White,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Focusable = false
        };
        button.Template = BuildRoundTemplate();
        button.Click += (_, _) => action();
        return button;
    }

    private static ControlTemplate BuildRoundTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private void OpenEntry(SnipEntry entry)
    {
        if (string.IsNullOrEmpty(entry.FilePath) || !File.Exists(entry.FilePath))
        {
            MessageDialog.Warning(this, "File is missing", $"This snip no longer exists at:\n{entry.FilePath}");
            return;
        }
        _host.OpenEditorForEntry(entry);
    }

    private void CopyEntry(SnipEntry entry)
    {
        try
        {
            if (!File.Exists(entry.FilePath))
            {
                MessageDialog.Warning(this, "File is missing", "The saved image could not be found.");
                return;
            }
            using var bitmap = BitmapUtils.Load(entry.FilePath);
            if (ClipboardService.SetImage(bitmap))
            {
                StatusText.Text = $"Copied {entry.FileName} to the clipboard.";
                if (_host.Settings.ShowNotifications)
                    ToastWindow.Show("Snip copied to the clipboard.", thumbnail: bitmap);
            }
            else
            {
                MessageDialog.Warning(this, "Clipboard unavailable", "Windows did not allow access to the clipboard.");
            }
        }
        catch (Exception ex)
        {
            MessageDialog.Error(this, "Could not copy snip", ex.Message);
        }
    }

    private void DeleteEntry(SnipEntry entry)
    {
        int choice = MessageDialog.Show(this, "Delete snip?",
            $"Remove “{entry.FileName}” from history. You can also delete the image file from disk.",
            DialogKind.Question, new[] { "Delete file", "Remove from history", "Cancel" }, 2);
        if (choice < 0 || choice > 1) return;
        _host.History.Remove(entry, deleteFiles: choice == 0);
        Reload();
    }

    private void ClearAll()
    {
        if (_all.Count == 0) return;
        int choice = MessageDialog.Show(this, "Clear snip history?",
            $"Remove all {_all.Count} entries. Choose whether the PNG/JPG/BMP files should be deleted from disk as well.",
            DialogKind.Warning, new[] { "Delete everything", "Clear list only", "Cancel" }, 2);
        if (choice < 0 || choice > 1) return;

        _bulk = true;
        try
        {
            foreach (var entry in _all.ToList())
                _host.History.Remove(entry, deleteFiles: choice == 0);
        }
        finally
        {
            _bulk = false;
        }
        _thumbCache.Clear();
        Reload();
        StatusText.Text = choice == 0 ? "History and image files deleted." : "History cleared. Image files were kept.";
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_host.Settings.SaveDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_host.Settings.SaveDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageDialog.Error(this, "Could not open folder", ex.Message);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1 && e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
