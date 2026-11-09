using System.Drawing;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnippingTool.Capture;
using SnippingTool.Core;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using CaptureMode = SnippingTool.Core.CaptureMode;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace SnippingTool.Capture;

public sealed class CaptureResult
{
    public CaptureMode Mode { get; init; }
    public Rectangle Region { get; init; }
    public IReadOnlyList<Point>? FreeformPoints { get; init; }
    public WindowInfo? Window { get; init; }
    public bool RegionOnly { get; init; }
}

public sealed class SelectionSurface : FrameworkElement
{
    private Bitmap? _frozen;
    private BitmapSource? _frozenSource;
    private Rectangle _virtualScreen;
    private double _scale = 1.0;
    private bool _dragging;
    private Point _dragStartScreen;
    private Point _currentScreen;
    private readonly List<Point> _freePoints = new();
    private WindowInfo? _hoverWindow;
    private (Rectangle Bounds, string Name)? _hoverMonitor;
    private List<WindowInfo> _windows = new();
    private CaptureMode _mode = CaptureMode.Rectangle;

    private static readonly Brush DimBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(150, 0, 0, 0));
    private static readonly Brush SoftDimBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 0, 0, 0));
    private static readonly Brush AccentBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212));
    private static readonly Brush BadgeBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(235, 28, 28, 30));
    private static readonly Brush WhiteBrush = Brushes.White;
    private static readonly Typeface BadgeTypeface = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public CaptureMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            _dragging = false;
            _freePoints.Clear();
            UpdateCursor();
            InvalidateVisual();
        }
    }

    public bool RegionOnly { get; set; }

    public event EventHandler<CaptureResult>? Committed;
    public event EventHandler? Cancelled;
    public event EventHandler<(Point ScreenPoint, bool Show)>? CursorMoved;

    public SelectionSurface()
    {
        Focusable = true;
        ClipToBounds = true;
        UpdateCursor();
        if (DimBrush.CanFreeze) DimBrush.Freeze();
        if (SoftDimBrush.CanFreeze) SoftDimBrush.Freeze();
        if (AccentBrush.CanFreeze) AccentBrush.Freeze();
        if (BadgeBrush.CanFreeze) BadgeBrush.Freeze();
    }

    public void SetCapturedDesktop(Bitmap frozen, Rectangle virtualScreen, double dpiScale, CaptureMode mode, List<WindowInfo> windows)
    {
        _frozen = frozen;
        _frozenSource = Imaging.BitmapUtils.ToBitmapSource(frozen);
        _virtualScreen = virtualScreen;
        _scale = dpiScale <= 0 ? 1.0 : dpiScale;
        _mode = mode;
        _windows = windows;
        UpdateCursor();
        InvalidateVisual();
    }

    public BitmapSource? FrozenSource => _frozenSource;

    private void UpdateCursor()
    {
        Cursor = _mode switch
        {
            CaptureMode.Window or CaptureMode.Monitor or CaptureMode.Fullscreen => Cursors.Hand,
            _ => Cursors.Cross
        };
    }

    private Point ScreenToLocal(Point screen) =>
        new((screen.X - _virtualScreen.Left) / _scale, (screen.Y - _virtualScreen.Top) / _scale);

    private Point LocalToScreen(Point local) =>
        new(_virtualScreen.Left + local.X * _scale, _virtualScreen.Top + local.Y * _scale);

    private Point ClampScreen(Point point) => new(
        Math.Clamp(point.X, _virtualScreen.Left, _virtualScreen.Right - 1),
        Math.Clamp(point.Y, _virtualScreen.Top, _virtualScreen.Bottom - 1));

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_frozen == null || _frozenSource == null) return;

        dc.DrawImage(_frozenSource, new Rect(0, 0, ActualWidth, ActualHeight));

        var selection = GetCurrentSelectionGeometry(out var physicalRect, out var isFreeform, out var freePointsLocal);
        if (selection == null)
        {
            dc.DrawRectangle(SoftDimBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
            DrawHoverInfo(dc);
            return;
        }

        var full = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        Geometry outside;
        try { outside = new CombinedGeometry(GeometryCombineMode.Exclude, full, selection); }
        catch { outside = full; }
        dc.DrawGeometry(DimBrush, null, outside);

        // White keyline behind the accent border keeps the selection visible on bright content.
        var whitePen = new Pen(WhiteBrush, 1.2);
        var accentPen = new Pen(AccentBrush, 2.0);
        if (isFreeform)
        {
            dc.DrawGeometry(null, whitePen, selection);
            dc.DrawGeometry(null, accentPen, selection);
        }
        else
        {
            var rect = physicalRect;
            var local = new Rect(
                (rect.Left - _virtualScreen.Left) / _scale,
                (rect.Top - _virtualScreen.Top) / _scale,
                rect.Width / _scale,
                rect.Height / _scale);
            dc.DrawRectangle(null, whitePen, Rect.Inflate(local, 1, 1));
            dc.DrawRectangle(null, accentPen, local);
            DrawHandles(dc, local);
            DrawSizeBadge(dc, rect, local);
        }

        if (isFreeform && freePointsLocal != null)
            DrawFreeformBadge(dc, physicalRect);

        DrawHoverInfo(dc);
    }

    private void DrawHoverInfo(DrawingContext dc)
    {
        if (_mode == CaptureMode.Window && _hoverWindow != null)
        {
            var local = new Rect(
                (_hoverWindow.Bounds.Left - _virtualScreen.Left) / _scale,
                (_hoverWindow.Bounds.Top - _virtualScreen.Top) / _scale,
                _hoverWindow.Bounds.Width / _scale,
                _hoverWindow.Bounds.Height / _scale);
            dc.DrawRectangle(null, new Pen(WhiteBrush, 1.2), Rect.Inflate(local, 1, 1));
            dc.DrawRectangle(null, new Pen(AccentBrush, 2.0), local);
            DrawLabel(dc, _hoverWindow.Title, new Point(local.Left, Math.Max(6, local.Top - 34)), local.Width);
        }
        else if (_mode == CaptureMode.Monitor && _hoverMonitor is { } monitor)
        {
            var local = new Rect(
                (monitor.Bounds.Left - _virtualScreen.Left) / _scale,
                (monitor.Bounds.Top - _virtualScreen.Top) / _scale,
                monitor.Bounds.Width / _scale,
                monitor.Bounds.Height / _scale);
            dc.DrawRectangle(null, new Pen(WhiteBrush, 1.2), Rect.Inflate(local, 1, 1));
            dc.DrawRectangle(null, new Pen(AccentBrush, 2.0), local);
            DrawLabel(dc, $"{monitor.Name}  ·  {monitor.Bounds.Width} × {monitor.Bounds.Height}",
                new Point(local.Left + 20, local.Top + 20), local.Width - 40);
        }
    }

    private void DrawHandles(DrawingContext dc, Rect rect)
    {
        const double size = 7;
        var points = new[]
        {
            new Point(rect.Left, rect.Top), new Point(rect.Left + rect.Width / 2, rect.Top),
            new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Top + rect.Height / 2),
            new Point(rect.Right, rect.Bottom), new Point(rect.Left + rect.Width / 2, rect.Bottom),
            new Point(rect.Left, rect.Bottom), new Point(rect.Left, rect.Top + rect.Height / 2)
        };
        var fill = Brushes.White;
        var pen = new Pen(AccentBrush, 1);
        foreach (var p in points)
            dc.DrawRectangle(fill, pen, new Rect(p.X - size / 2, p.Y - size / 2, size, size));
    }

    private void DrawSizeBadge(DrawingContext dc, Rectangle physicalRect, Rect local)
    {
        string text = $"{physicalRect.Width} × {physicalRect.Height}";
        DrawBadgeAt(dc, text, local);
    }

    private void DrawFreeformBadge(DrawingContext dc, Rectangle physicalRect)
    {
        var local = new Rect(
            (physicalRect.Left - _virtualScreen.Left) / _scale,
            (physicalRect.Top - _virtualScreen.Top) / _scale,
            physicalRect.Width / _scale,
            physicalRect.Height / _scale);
        DrawBadgeAt(dc, $"{physicalRect.Width} × {physicalRect.Height}", local);
    }

    private void DrawBadgeAt(DrawingContext dc, string text, Rect local)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            BadgeTypeface, 12, Brushes.White, dpi);
        double w = formatted.Width + 16;
        double h = formatted.Height + 8;
        double x = Math.Clamp(local.Left, 4, Math.Max(4, ActualWidth - w - 4));
        double y = local.Top - h - 10;
        if (y < 4) y = Math.Min(ActualHeight - h - 4, local.Top + 10);
        y = Math.Max(4, y);
        var badge = new Rect(x, y, w, h);
        dc.DrawRoundedRectangle(new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 0, 0, 0)), null,
            new Rect(x + 1, y + 2, w, h), 5, 5);
        dc.DrawRoundedRectangle(BadgeBrush, new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)), 1), badge, 5, 5);
        dc.DrawText(formatted, new Point(x + 8, y + 4));
    }

    private void DrawLabel(DrawingContext dc, string text, Point at, double maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (text.Length > 70) text = text[..67] + "…";
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            BadgeTypeface, 12, Brushes.White, dpi) { MaxTextWidth = Math.Max(80, maxWidth) };
        double w = Math.Min(formatted.Width + 18, Math.Max(80, maxWidth));
        double h = formatted.Height + 9;
        double x = Math.Clamp(at.X, 4, Math.Max(4, ActualWidth - w - 4));
        double y = Math.Max(4, at.Y);
        dc.DrawRoundedRectangle(BadgeBrush, new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(45, 255, 255, 255)), 1),
            new Rect(x, y, w, h), 5, 5);
        dc.DrawText(formatted, new Point(x + 9, y + 4));
    }

    private Geometry? GetCurrentSelectionGeometry(out Rectangle physicalRect, out bool isFreeform, out List<Point>? freePointsLocal)
    {
        physicalRect = Rectangle.Empty;
        isFreeform = false;
        freePointsLocal = null;

        if (_mode == CaptureMode.Window && !_dragging)
        {
            if (_hoverWindow == null) return null;
            physicalRect = _hoverWindow.Bounds;
            return new RectangleGeometry(ToLocalRect(physicalRect));
        }
        if (_mode == CaptureMode.Monitor && !_dragging)
        {
            if (_hoverMonitor == null) return null;
            physicalRect = _hoverMonitor.Value.Bounds;
            return new RectangleGeometry(ToLocalRect(physicalRect));
        }
        if (_mode == CaptureMode.Fullscreen && !_dragging)
        {
            physicalRect = _virtualScreen;
            return new RectangleGeometry(ToLocalRect(physicalRect));
        }

        if (!_dragging)
        {
            if (_mode == CaptureMode.Freeform) return null;
            return null;
        }

        if (_mode == CaptureMode.Freeform)
        {
            if (_freePoints.Count < 2) return null;
            isFreeform = true;
            freePointsLocal = _freePoints.Select(ScreenToLocal).ToList();
            physicalRect = GetBounds(_freePoints);
            var geometry = BuildSmoothGeometry(freePointsLocal, true, out _);
            return geometry;
        }
        else
        {
            var rect = Normalize(_dragStartScreen, _currentScreen);
            if (rect.Width < 1 || rect.Height < 1) return null;
            physicalRect = rect;
            return new RectangleGeometry(ToLocalRect(rect));
        }
    }

    private Rect ToLocalRect(Rectangle physical) => new(
        (physical.Left - _virtualScreen.Left) / _scale,
        (physical.Top - _virtualScreen.Top) / _scale,
        physical.Width / _scale,
        physical.Height / _scale);

    private static Rectangle Normalize(Point a, Point b)
    {
        int left = (int)Math.Round(Math.Min(a.X, b.X));
        int top = (int)Math.Round(Math.Min(a.Y, b.Y));
        int right = (int)Math.Round(Math.Max(a.X, b.X));
        int bottom = (int)Math.Round(Math.Max(a.Y, b.Y));
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static Rectangle GetBounds(IReadOnlyList<Point> points)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }
        return Rectangle.FromLTRB((int)Math.Floor(minX), (int)Math.Floor(minY),
            (int)Math.Ceiling(maxX) + 1, (int)Math.Ceiling(maxY) + 1);
    }

    internal static StreamGeometry BuildSmoothGeometry(IReadOnlyList<Point> points, bool closed, out Point last)
    {
        last = points.Count > 0 ? points[^1] : new Point();
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            if (points.Count == 0) return geometry;
            ctx.BeginFigure(points[0], false, closed);
            if (points.Count == 1)
            {
                ctx.LineTo(points[0], true, false);
            }
            else
            {
                for (int i = 1; i < points.Count - 1; i++)
                {
                    var mid = new Point((points[i].X + points[i + 1].X) / 2, (points[i].Y + points[i + 1].Y) / 2);
                    ctx.QuadraticBezierTo(points[i], mid, true, false);
                }
                ctx.LineTo(points[^1], true, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var screen = ClampScreen(LocalToScreen(e.GetPosition(this)));

        if (_mode == CaptureMode.Window)
        {
            _hoverWindow = CaptureService.WindowAt(new System.Drawing.Point((int)Math.Round(screen.X), (int)Math.Round(screen.Y)), _windows);
            if (_hoverWindow != null) CommitWindow(_hoverWindow);
            return;
        }
        if (_mode == CaptureMode.Monitor)
        {
            var monitor = CaptureService.MonitorAt(new System.Drawing.Point((int)Math.Round(screen.X), (int)Math.Round(screen.Y)));
            CommitRegion(monitor.Bounds, CaptureMode.Monitor);
            return;
        }
        if (_mode == CaptureMode.Fullscreen)
        {
            CommitRegion(_virtualScreen, CaptureMode.Fullscreen);
            return;
        }

        _dragging = true;
        _dragStartScreen = screen;
        _currentScreen = screen;
        _freePoints.Clear();
        _freePoints.Add(screen);
        CaptureMouse();
        InvalidateVisual();
        CursorMoved?.Invoke(this, (screen, false));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var screen = ClampScreen(LocalToScreen(e.GetPosition(this)));
        _currentScreen = screen;

        if (_mode == CaptureMode.Window)
        {
            var hit = CaptureService.WindowAt(new System.Drawing.Point((int)Math.Round(screen.X), (int)Math.Round(screen.Y)), _windows);
            if (!ReferenceEquals(hit, _hoverWindow))
            {
                _hoverWindow = hit;
                InvalidateVisual();
            }
        }
        else if (_mode == CaptureMode.Monitor)
        {
            var monitor = CaptureService.MonitorAt(new System.Drawing.Point((int)Math.Round(screen.X), (int)Math.Round(screen.Y)));
            if (_hoverMonitor == null || _hoverMonitor.Value.Bounds != monitor.Bounds)
            {
                _hoverMonitor = monitor;
                InvalidateVisual();
            }
        }
        else if (_mode == CaptureMode.Fullscreen)
        {
            _hoverMonitor = CaptureService.MonitorAt(new System.Drawing.Point((int)Math.Round(screen.X), (int)Math.Round(screen.Y)));
            InvalidateVisual();
        }

        if (_dragging)
        {
            if (_mode == CaptureMode.Freeform)
            {
                var last = _freePoints.Count > 0 ? _freePoints[^1] : screen;
                if ((screen - last).Length >= 2)
                    _freePoints.Add(screen);
            }
            CursorMoved?.Invoke(this, (screen, true));
            InvalidateVisual();
        }
        else
        {
            CursorMoved?.Invoke(this, (screen, false));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        var screen = ClampScreen(LocalToScreen(e.GetPosition(this)));
        _currentScreen = screen;

        if (_mode == CaptureMode.Freeform)
        {
            if (_freePoints.Count > 1) _freePoints.Add(screen);
            if (_freePoints.Count >= 4 && GetBounds(_freePoints).Width >= 2 && GetBounds(_freePoints).Height >= 2)
            {
                var points = _freePoints.Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToList();
                var bounds = GetBounds(points);
                Committed?.Invoke(this, new CaptureResult
                {
                    Mode = CaptureMode.Freeform,
                    Region = bounds,
                    FreeformPoints = points,
                    RegionOnly = RegionOnly
                });
                return;
            }
        }
        else
        {
            var rect = Normalize(_dragStartScreen, screen);
            if (rect.Width >= 2 && rect.Height >= 2)
            {
                Committed?.Invoke(this, new CaptureResult
                {
                    Mode = CaptureMode.Rectangle,
                    Region = rect,
                    RegionOnly = RegionOnly
                });
                return;
            }
        }
        _freePoints.Clear();
        InvalidateVisual();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        Cancelled?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void CommitWindow(WindowInfo window)
    {
        Committed?.Invoke(this, new CaptureResult
        {
            Mode = CaptureMode.Window,
            Region = window.Bounds,
            Window = window,
            RegionOnly = RegionOnly
        });
    }

    private void CommitRegion(Rectangle rect, CaptureMode mode)
    {
        Committed?.Invoke(this, new CaptureResult
        {
            Mode = mode,
            Region = rect,
            RegionOnly = RegionOnly
        });
    }

    public void CommitFullDesktop()
    {
        CommitRegion(_virtualScreen, CaptureMode.Fullscreen);
    }

    public Rectangle VirtualScreen => _virtualScreen;
}

