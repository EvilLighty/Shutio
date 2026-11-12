using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SnippingTool.Core;
using SnippingTool.Imaging;
using Drawing = System.Drawing;

namespace SnippingTool.Recording;

/// <summary>
/// Small topmost, draggable window that shows the live webcam while a recording is in
/// progress — "see it on my actual screen". It is fed by the RECORDING ffmpeg process
/// (which also emits the cam feed as raw frames on stdout), so the camera is opened
/// exactly once and the preview mirrors exactly what is composited into the MP4
/// (same scale, same mirror). No second capture, no busy-device races.
/// </summary>
public partial class WebcamPreviewWindow : Window
{
    private readonly RecordingController _recorder;
    private readonly DispatcherTimer _timer;
    private bool _dragging;
    private Point _dragOffset;
    private bool _everHadFrame;

    public WebcamPreviewWindow(RecordingController recorder)
    {
        _recorder = recorder;
        InitializeComponent();
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1000 / 15) };
        _timer.Tick += (_, _) => PumpFrame();
        Loaded += (_, _) =>
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - ActualWidth - 16;
            Top = work.Bottom - ActualHeight - 16;
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
        CloseButton.Click += (_, _) => Close();
    }

    private void PumpFrame()
    {
        if (!_recorder.TryGetLatestPreviewFrame(out byte[] frame, out int w, out int h))
        {
            if (_everHadFrame && StatusPanel.Visibility != Visibility.Visible)
            {
                StatusText.Text = "Webcam disconnected — the recording continues.";
                StatusPanel.Visibility = Visibility.Visible;
            }
            return;
        }
        try
        {
            using var bmp = new Drawing.Bitmap(w, h, Drawing.Imaging.PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Drawing.Rectangle(0, 0, w, h),
                Drawing.Imaging.ImageLockMode.WriteOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(frame, 0, data.Scan0, w * h * 4);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            CamImage.Source = BitmapUtils.ToBitmapSource(bmp);
            if (StatusPanel.Visibility == Visibility.Visible)
                StatusPanel.Visibility = Visibility.Collapsed;
            _everHadFrame = true;
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not render the webcam preview frame", ex);
        }
    }

    // ===================== drag to move =====================

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase) return;   // don't drag from the close button
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            _dragging = true;
            _dragOffset = e.GetPosition(this);
            try { CaptureMouse(); } catch { }
        }
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(this);
        Left += p.X - _dragOffset.X;
        Top += p.Y - _dragOffset.Y;
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        try { ReleaseMouseCapture(); } catch { }
    }
}
