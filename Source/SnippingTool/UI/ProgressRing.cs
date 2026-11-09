using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SnippingTool.UI;

public sealed class ProgressRing : FrameworkElement
{
    private readonly DispatcherTimer _timer;
    private double _angle;

    public ProgressRing()
    {
        Width = 22;
        Height = 22;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) =>
        {
            _angle = (_angle + 9) % 360;
            InvalidateVisual();
        };
        IsVisibleChanged += (_, _) => UpdateTimer();
    }

    private void UpdateTimer()
    {
        if (IsVisible) _timer.Start();
        else _timer.Stop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 2) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = size / 2 - 2;
        var track = new Pen(new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), 2);
        dc.DrawEllipse(null, track, center, radius, radius);
        var brush = TryFindResource("AccentBrush") as Brush ?? Brushes.DeepSkyBlue;
        var pen = new Pen(brush, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        double start = _angle * Math.PI / 180;
        var p1 = new Point(center.X + radius * Math.Cos(start), center.Y + radius * Math.Sin(start));
        var p2 = new Point(center.X + radius * Math.Cos(start + Math.PI * 1.45), center.Y + radius * Math.Sin(start + Math.PI * 1.45));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(p1, false, false);
            ctx.ArcTo(p2, new Size(radius, radius), 0, true, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
