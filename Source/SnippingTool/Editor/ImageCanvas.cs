using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnippingTool.Imaging;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Windows.Size;

namespace SnippingTool.Editor;

public enum EditorTool
{
    Pen,
    Highlighter,
    Eraser,
    Line,
    Crop
}

public sealed class InkStroke
{
    public EditorTool Tool { get; init; }
    public Color Color { get; init; }
    public double Thickness { get; init; }
    public List<Point> Points { get; } = new();
    public bool IsStraight => Tool == EditorTool.Line;
    public double RenderedThickness => Tool == EditorTool.Highlighter ? Thickness * 3.2 : Thickness;
    public Color RenderedColor => Tool == EditorTool.Highlighter
        ? Color.FromArgb(88, Color.R, Color.G, Color.B)
        : Color;

    public bool HitTest(Point point, double radius)
    {
        double limit = radius + RenderedThickness / 2;
        if (Points.Count == 0) return false;
        if (Points.Count == 1) return (point - Points[0]).Length <= limit;
        for (int i = 1; i < Points.Count; i++)
            if (DistanceToSegment(point, Points[i - 1], Points[i]) <= limit) return true;
        return false;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double lengthSq = ab.LengthSquared;
        if (lengthSq < 0.0001) return (p - a).Length;
        double t = Math.Clamp(((p - a) * ab) / lengthSq, 0, 1);
        var projection = a + ab * t;
        return (p - projection).Length;
    }

    public Rect Bounds
    {
        get
        {
            if (Points.Count == 0) return Rect.Empty;
            double minX = Points.Min(p => p.X) - RenderedThickness;
            double minY = Points.Min(p => p.Y) - RenderedThickness;
            double maxX = Points.Max(p => p.X) + RenderedThickness;
            double maxY = Points.Max(p => p.Y) + RenderedThickness;
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }

    public static StreamGeometry BuildGeometry(IReadOnlyList<Point> points, bool close = false)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            if (points.Count == 0) return geometry;
            ctx.BeginFigure(points[0], false, close);
            if (points.Count == 1)
            {
                ctx.LineTo(points[0], true, false);
            }
            else if (points.Count == 2)
            {
                ctx.LineTo(points[1], true, false);
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
}

internal sealed class EditorSnapshot
{
    public Bitmap Base { get; }
    public List<InkStroke> Strokes { get; }
    public EditorSnapshot(Bitmap baseBitmap, List<InkStroke> strokes)
    {
        Base = baseBitmap;
        Strokes = strokes;
    }
}

public sealed class ImageCanvas : FrameworkElement
{
    private Bitmap? _base;
    private BitmapSource? _baseSource;
    private List<InkStroke> _strokes = new();
    private readonly List<EditorSnapshot> _undo = new();
    private readonly List<EditorSnapshot> _redo = new();
    private const int MaxHistory = 20;

    private EditorTool _tool = EditorTool.Pen;
    private Color _penColor = Color.FromRgb(255, 59, 48);
    private double _penThickness = 4;

    private double? _explicitZoom;
    private Vector _pan;
    private Point _lastLocal;
    private bool _mouseInside;

    private InkStroke? _activeStroke;
    private bool _erasing;
    private bool _eraserChanged;
    private Point _erasePoint;

    private bool _cropping;
    private Point _cropStart;
    private Point _cropCurrent;
    private bool _panning;
    private Point _panStartMouse;
    private Vector _panStartOffset;




    public event EventHandler? HistoryChanged;
    public event EventHandler? ZoomChanged;
    public event EventHandler? ToolChanged;
    public event EventHandler? ImageChanged;

    public ImageCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        UpdateCursor();
    }

    public Bitmap? BaseBitmap => _base;
    public bool HasImage => _base != null;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int StrokeCount => _strokes.Count;
    public Size ImageSize => _base == null ? Size.Empty : new Size(_base.Width, _base.Height);

    public EditorTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            _tool = value;
            if (value == EditorTool.Crop)
            {
                _cropping = false;
            }
            UpdateCursor();
            InvalidateVisual();
            ToolChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Color PenColor
    {
        get => _penColor;
        set { _penColor = value; InvalidateVisual(); }
    }

    public double PenThickness
    {
        get => _penThickness;
        set { _penThickness = Math.Clamp(value, 1, 40); InvalidateVisual(); }
    }

    public double EffectiveScale
    {
        get
        {
            if (_base == null) return 1;
            return _explicitZoom ?? FitScale;
        }
    }

    private double FitScale
    {
        get
        {
            if (_base == null || ActualWidth < 2 || ActualHeight < 2) return 1;
            double sx = Math.Max(1, ActualWidth - 56) / _base.Width;
            double sy = Math.Max(1, ActualHeight - 56) / _base.Height;
            return Math.Clamp(Math.Min(sx, sy), 0.02, 8.0);
        }
    }

    public double ZoomPercent => EffectiveScale * 100;

    public Rect ImageRect
    {
        get
        {
            if (_base == null) return Rect.Empty;
            double scale = EffectiveScale;
            double w = _base.Width * scale;
            double h = _base.Height * scale;
            double x = (ActualWidth - w) / 2 + _pan.X;
            double y = (ActualHeight - h) / 2 + _pan.Y;
            return new Rect(x, y, w, h);
        }
    }

    public void LoadBitmap(Bitmap bitmap)
    {
        DisposeAllBitmaps(keepCurrent: false);
        _base = bitmap;
        _baseSource = BitmapUtils.ToBitmapSource(bitmap);
        _strokes = new List<InkStroke>();
        _explicitZoom = null;
        _pan = new Vector();
        _activeStroke = null;
        _cropping = false;
        InvalidateVisual();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        ImageChanged?.Invoke(this, EventArgs.Empty);
    }

    public Bitmap Flatten()
    {
        if (_base == null) throw new InvalidOperationException("No image is loaded.");
        var result = new Bitmap(_base.Width, _base.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImageUnscaled(_base, 0, 0);
        g.CompositingMode = CompositingMode.SourceOver;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        foreach (var stroke in _strokes)
            DrawStrokeGdi(g, stroke);
        return result;
    }

    private static void DrawStrokeGdi(Graphics g, InkStroke stroke)
    {
        if (stroke.Points.Count == 0) return;
        var color = System.Drawing.Color.FromArgb(stroke.RenderedColor.A, stroke.RenderedColor.R,
            stroke.RenderedColor.G, stroke.RenderedColor.B);
        using var pen = new System.Drawing.Pen(color, (float)stroke.RenderedThickness)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
            Alignment = System.Drawing.Drawing2D.PenAlignment.Center
        };
        if (stroke.Points.Count == 1)
        {
            float r = (float)(stroke.RenderedThickness / 2);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, (float)stroke.Points[0].X - r, (float)stroke.Points[0].Y - r, r * 2, r * 2);
            return;
        }
        if (stroke.IsStraight)
        {
            var p0 = stroke.Points[0];
            var p1 = stroke.Points[^1];
            g.DrawLine(pen, (float)p0.X, (float)p0.Y, (float)p1.X, (float)p1.Y);
            return;
        }
        var pts = stroke.Points.Select(p => new PointF((float)p.X, (float)p.Y)).ToArray();
        g.DrawLines(pen, pts);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_base == null || _baseSource == null) return;
        var imageRect = ImageRect;

        // Soft, layered shadow behind the paper.
        if (imageRect.Width > 0 && imageRect.Height > 0)
        {
            for (int i = 5; i >= 1; i--)
            {
                var alpha = (byte)(8 + i * 3);
                var shadow = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
                dc.DrawRoundedRectangle(shadow, null,
                    new Rect(imageRect.X - i * 1.5, imageRect.Y + i * 2, imageRect.Width + i * 3, imageRect.Height + i * 3), 4, 4);
            }
        }

        if (TryFindResource("CheckerBrush") is Brush checker)
            dc.DrawRectangle(checker, null, imageRect);
        else
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(60, 60, 60)), null, imageRect);

        dc.PushTransform(new TranslateTransform(imageRect.X, imageRect.Y));
        dc.PushTransform(new ScaleTransform(EffectiveScale, EffectiveScale));
        dc.DrawImage(_baseSource, new Rect(0, 0, _base.Width, _base.Height));
        foreach (var stroke in _strokes)
            DrawStrokeWpf(dc, stroke);
        if (_activeStroke != null && !_strokes.Contains(_activeStroke))
            DrawStrokeWpf(dc, _activeStroke);
        dc.Pop();
        dc.Pop();

        if (_cropping || _tool == EditorTool.Crop)
            DrawCropOverlay(dc, imageRect);

        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), 1), imageRect);

        if (_mouseInside && _tool == EditorTool.Eraser)
            DrawEraserCursor(dc, imageRect);
    }

    private static void DrawStrokeWpf(DrawingContext dc, InkStroke stroke)
    {
        if (stroke.Points.Count == 0) return;
        var brush = new SolidColorBrush(stroke.RenderedColor);
        if (stroke.Points.Count == 1)
        {
            double r = stroke.RenderedThickness / 2;
            dc.DrawEllipse(brush, null, stroke.Points[0], r, r);
            return;
        }
        var geometry = InkStroke.BuildGeometry(stroke.Points, false);
        var pen = new Pen(brush, stroke.RenderedThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        dc.DrawGeometry(null, pen, geometry);
    }

    private void DrawCropOverlay(DrawingContext dc, Rect imageRect)
    {
        if (!_cropping)
        {
            // A subtle instruction rail is drawn by the editor window; nothing extra here.
            return;
        }
        var crop = NormalizeCrop(_cropStart, _cropCurrent);
        if (crop.Width < 1 || crop.Height < 1) return;
        double scale = EffectiveScale;
        var local = new Rect(imageRect.X + crop.X * scale, imageRect.Y + crop.Y * scale,
            crop.Width * scale, crop.Height * scale);

        var full = new RectangleGeometry(imageRect);
        var cut = new RectangleGeometry(local);
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude, full, cut);
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, outside);
        dc.DrawRectangle(null, new Pen(Brushes.White, 1), Rect.Inflate(local, 1, 1));
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0, 120, 212)), 2), local);

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = new FormattedText($"{crop.Width} × {crop.Height}", CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI Variable Text"), 12, Brushes.White, dpi);
        double w = text.Width + 16, h = text.Height + 8;
        double x = Math.Clamp(local.Left, 4, Math.Max(4, ActualWidth - w - 4));
        double y = local.Top - h - 8;
        if (y < 4) y = local.Top + 8;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(235, 28, 28, 30)), null, new Rect(x, y, w, h), 5, 5);
        dc.DrawText(text, new Point(x + 8, y + 4));
    }

    private void DrawEraserCursor(DrawingContext dc, Rect imageRect)
    {
        if (!imageRect.Contains(_lastLocal)) return;
        double radius = Math.Max(6, 12 * EffectiveScale);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 1.2), _lastLocal, radius, radius);
    }

    private void UpdateCursor()
    {
        Cursor = _tool switch
        {
            EditorTool.Eraser => Cursors.None,
            EditorTool.Crop => Cursors.Cross,
            _ => Cursors.Cross
        };
    }

    private Point LocalToImage(Point local)
    {
        var rect = ImageRect;
        double scale = EffectiveScale;
        if (scale <= 0) return new Point();
        return new Point((local.X - rect.X) / scale, (local.Y - rect.Y) / scale);
    }

    private bool IsInsideImage(Point image)
    {
        return _base != null && image.X >= 0 && image.Y >= 0 && image.X <= _base.Width && image.Y <= _base.Height;
    }

    private Rectangle NormalizeCrop(Point a, Point b)
    {
        if (_base == null) return Rectangle.Empty;
        int left = (int)Math.Round(Math.Min(a.X, b.X));
        int top = (int)Math.Round(Math.Min(a.Y, b.Y));
        int right = (int)Math.Round(Math.Max(a.X, b.X));
        int bottom = (int)Math.Round(Math.Max(a.Y, b.Y));
        left = Math.Clamp(left, 0, _base.Width);
        top = Math.Clamp(top, 0, _base.Height);
        right = Math.Clamp(right, 0, _base.Width);
        bottom = Math.Clamp(bottom, 0, _base.Height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_base == null) return;

        if (e.ChangedButton == MouseButton.Middle)
        {
            _panning = true;
            _panStartMouse = e.GetPosition(this);
            _panStartOffset = _pan;
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;
        var local = e.GetPosition(this);
        _lastLocal = local;
        _mouseInside = true;
        var image = LocalToImage(local);
        if (!IsInsideImage(image) && _tool != EditorTool.Eraser)
        {
            // Crops may start slightly outside the paper and are clamped.
            if (_tool != EditorTool.Crop) return;
        }

        if (_tool == EditorTool.Crop)
        {
            _cropping = true;
            _cropStart = image;
            _cropCurrent = image;
            CaptureMouse();
            InvalidateVisual();
            return;
        }

        if (_tool == EditorTool.Eraser)
        {
            PushUndo();
            _erasing = true;
            _eraserChanged = false;
            _erasePoint = image;
            EraseAt(image);
            CaptureMouse();
            InvalidateVisual();
            return;
        }

        PushUndo();
        _activeStroke = new InkStroke
        {
            Tool = _tool,
            Color = _penColor,
            Thickness = _penThickness
        };
        image = new Point(Math.Clamp(image.X, 0, _base.Width), Math.Clamp(image.Y, 0, _base.Height));
        _activeStroke.Points.Add(image);
        _strokes.Add(_activeStroke);
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var local = e.GetPosition(this);
        _lastLocal = local;
        _mouseInside = true;

        if (_panning)
        {
            _pan = _panStartOffset + (local - _panStartMouse);
            InvalidateVisual();
            return;
        }

        if (_base == null) return;
        var image = LocalToImage(local);
        image = new Point(Math.Clamp(image.X, 0, _base.Width), Math.Clamp(image.Y, 0, _base.Height));

        if (_cropping)
        {
            _cropCurrent = image;
            InvalidateVisual();
            return;
        }

        if (_erasing)
        {
            _erasePoint = image;
            EraseAt(image);
            InvalidateVisual();
            return;
        }

        if (_activeStroke != null)
        {
            if (_activeStroke.IsStraight)
            {
                if (_activeStroke.Points.Count == 1) _activeStroke.Points.Add(image);
                else _activeStroke.Points[^1] = image;
            }
            else
            {
                var last = _activeStroke.Points[^1];
                if ((image - last).Length >= 1.2)
                    _activeStroke.Points.Add(image);
            }
            InvalidateVisual();
        }
        else if (_tool == EditorTool.Eraser)
        {
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_panning && e.ChangedButton == MouseButton.Middle)
        {
            _panning = false;
            ReleaseMouseCapture();
            UpdateCursor();
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        if (_cropping)
        {
            _cropping = false;
            ReleaseMouseCapture();
            var crop = NormalizeCrop(_cropStart, _cropCurrent);
            if (crop.Width >= 4 && crop.Height >= 4)
                ApplyCrop(crop);
            else
                InvalidateVisual();
            return;
        }

        if (_erasing)
        {
            _erasing = false;
            ReleaseMouseCapture();
            if (!_eraserChanged)
                RemoveLastUndoIfNoop();
            else
                ImageChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return;
        }

        if (_activeStroke != null)
        {
            _activeStroke = null;
            ReleaseMouseCapture();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            ImageChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _mouseInside = false;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        ZoomAt(e.GetPosition(this), factor);
        e.Handled = true;
    }

    private void EraseAt(Point image)
    {
        double radius = 12;
        int removed = _strokes.RemoveAll(s => s.HitTest(image, radius + s.RenderedThickness / 2));
        if (removed > 0)
        {
            _eraserChanged = true;
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ApplyCrop(Rectangle crop)
    {
        if (_base == null || crop.Width < 1 || crop.Height < 1) return;
        PushUndo();
        using var flattened = Flatten();
        var cropped = BitmapUtils.Crop(flattened, crop);
        _base = cropped;
        _baseSource = BitmapUtils.ToBitmapSource(cropped);
        _strokes.Clear();
        _activeStroke = null;
        _explicitZoom = null;
        _pan = new Vector();
        _tool = EditorTool.Pen;
        UpdateCursor();
        InvalidateVisual();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        ToolChanged?.Invoke(this, EventArgs.Empty);
        ImageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Rotate(bool clockwise)
    {
        if (_base == null) return;
        PushUndo();
        using var flattened = Flatten();
        var rotated = BitmapUtils.Rotate(flattened, clockwise);
        _base = rotated;
        _baseSource = BitmapUtils.ToBitmapSource(rotated);
        _strokes.Clear();
        _activeStroke = null;
        _explicitZoom = null;
        _pan = new Vector();
        InvalidateVisual();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        ImageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResizeImage(int width, int height)
    {
        if (_base == null) return;
        if (width == _base.Width && height == _base.Height) return;
        PushUndo();
        using var flattened = Flatten();
        var resized = BitmapUtils.Resize(flattened, width, height);
        _base = resized;
        _baseSource = BitmapUtils.ToBitmapSource(resized);
        _strokes.Clear();
        _activeStroke = null;
        _explicitZoom = null;
        _pan = new Vector();
        InvalidateVisual();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        ImageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_base == null || _undo.Count == 0) return;
        _redo.Add(new EditorSnapshot(_base, new List<InkStroke>(_strokes)));
        var snapshot = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _base = snapshot.Base;
        _baseSource = BitmapUtils.ToBitmapSource(_base);
        _strokes = new List<InkStroke>(snapshot.Strokes);
        _activeStroke = null;
        InvalidateVisual();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        ImageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_base == null || _redo.Count == 0) return;
        _undo.Add(new EditorSnapshot(_base, new List<InkStroke>(_strokes)));
        var snapshot = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _base = snapshot.Base;
        _baseSource = BitmapUtils.ToBitmapSource(_base);
        _strokes = new List<InkStroke>(snapshot.Strokes);
        _activeStroke = null;
        InvalidateVisual();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        ImageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PushUndo()
    {
        if (_base == null) return;
        _undo.Add(new EditorSnapshot(_base, new List<InkStroke>(_strokes)));
        if (_undo.Count > MaxHistory)
            _undo.RemoveAt(0);
        _redo.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveLastUndoIfNoop()
    {
        if (_undo.Count > 0)
        {
            var last = _undo[^1];
            if (ReferenceEquals(last.Base, _base) && last.Strokes.Count == _strokes.Count)
                _undo.RemoveAt(_undo.Count - 1);
        }
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void FitToWindow()
    {
        _explicitZoom = null;
        _pan = new Vector();
        InvalidateVisual();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomIn() => ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), 1.25);
    public void ZoomOut() => ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), 1 / 1.25);

    public void SetZoomPercent(double percent)
    {
        if (_base == null) return;
        double target = Math.Clamp(percent / 100.0, 0.02, 16);
        double factor = target / Math.Max(0.0001, EffectiveScale);
        ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), factor);
    }

    public void SetZoomFactor(double factor)
    {
        if (_base == null) return;
        double target = Math.Clamp(factor * FitScale, 0.02, 16);
        ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), target / Math.Max(0.0001, EffectiveScale));
    }

    private void ZoomAt(Point local, double factor)
    {
        if (_base == null) return;
        double oldScale = EffectiveScale;
        var imagePoint = LocalToImage(local);
        double newScale = Math.Clamp(oldScale * factor, 0.02, 16);
        if (Math.Abs(newScale - oldScale) < 0.00001) return;
        _explicitZoom = newScale;
        var baseOrigin = new Point((ActualWidth - _base.Width * newScale) / 2,
            (ActualHeight - _base.Height * newScale) / 2);
        var desiredOrigin = new Point(local.X - imagePoint.X * newScale, local.Y - imagePoint.Y * newScale);
        _pan = new Vector(desiredOrigin.X - baseOrigin.X, desiredOrigin.Y - baseOrigin.Y);
        InvalidateVisual();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DisposeHistory()
    {
        DisposeAllBitmaps(keepCurrent: true);
    }

    public void DisposeCanvas()
    {
        DisposeAllBitmaps(keepCurrent: false);
        _base = null;
        _baseSource = null;
        _strokes.Clear();
    }

    private void DisposeAllBitmaps(bool keepCurrent)
    {
        var seen = new HashSet<Bitmap>();
        foreach (var snapshot in _undo)
        {
            if (keepCurrent && ReferenceEquals(snapshot.Base, _base)) continue;
            if (seen.Add(snapshot.Base)) snapshot.Base.Dispose();
        }
        foreach (var snapshot in _redo)
        {
            if (keepCurrent && ReferenceEquals(snapshot.Base, _base)) continue;
            if (seen.Add(snapshot.Base)) snapshot.Base.Dispose();
        }
        if (!keepCurrent && _base != null && seen.Add(_base)) _base.Dispose();
        if (keepCurrent && _base != null && !seen.Contains(_base)) { /* current owned by canvas */ }
        _undo.Clear();
        _redo.Clear();
    }
}

