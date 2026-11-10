using System.Globalization;
using System.IO;
using Path = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using SnippingTool.Core;
using SnippingTool.Imaging;
using SnippingTool.UI;
using Color = System.Windows.Media.Color;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace SnippingTool.Editing;

/// <summary>
/// The inline <c>Shutio Cut</c> timeline editor (the Video editing tab). A single-line toolbar
/// over a live preview and a draggable timeline of up to <see cref="TimelineConstants.MaxTracks"/>
/// tracks. Everything renders with the bundled ffmpeg (no pop-up windows, no extra dependencies).
/// </summary>
public partial class CutPane : UserControl
{
    private const double RulerH = 24;
    private const double TrackH = 46;
    private const double SnapPx = 8;          // snap threshold in screen pixels

    private readonly TimelineProject _project = new();
    private Window? _owner;
    private DispatcherTimer? _playTimer;
    private bool _playing;
    private bool _previewBusy;
    private bool _scrubUpdating;
    private bool _exporting;

    private double _pps = 60;                  // pixels per second (timeline zoom)
    private double _playTime;
    private bool _snapping = true;
    private string? _currentSource;
    private double _currentDur = 3.0;
    private TimelineTrack? _selectedTrack;
    private Line? _playheadLine;
    private PreviewDecoder? _decoder;
    private TimelineClip? _decoderClip;

    // Clip manipulation state
    private enum DragMode { Move, TrimLeft, TrimRight }
    private DragMode _mode;
    private Border? _dragBorder;
    private TimelineClip? _dragClip;
    private double _dragStartX;
    private double _origStart, _origDur, _origIn;

    // Brushes resolved from the active theme.
    private WpfBrush _textSecondary, _textTertiary, _accent, _altSurface, _border;
    private readonly WpfBrush _videoFill, _videoBorder, _audioFill, _audioBorder, _clipText, _selectedFill, _selectedBorder, _playhead;

    public CutPane()
    {
        _textSecondary = Res("TextSecondaryBrush", Brushes.Gray);
        _textTertiary = Res("TextTertiaryBrush", Brushes.DimGray);
        _accent = Res("AccentBrush", Brushes.DodgerBlue);
        _altSurface = Res("SurfaceAltBrush", Brushes.Gainsboro);
        _border = Res("BorderBrush", Brushes.LightGray);

        _videoFill = new SolidColorBrush(Color.FromArgb(120, 0, 103, 192));
        _videoBorder = new SolidColorBrush(Color.FromArgb(255, 0, 122, 215));
        _audioFill = new SolidColorBrush(Color.FromArgb(120, 46, 125, 50));
        _audioBorder = new SolidColorBrush(Color.FromArgb(255, 46, 160, 67));
        _clipText = new SolidColorBrush(Color.FromArgb(255, 240, 240, 245));
        _selectedFill = new SolidColorBrush(Color.FromArgb(160, 0, 103, 192));
        _selectedBorder = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
        _playhead = new SolidColorBrush(Color.FromArgb(255, 255, 71, 87));
        _videoFill.Freeze(); _videoBorder.Freeze(); _audioFill.Freeze(); _audioBorder.Freeze();
        _clipText.Freeze(); _selectedFill.Freeze(); _selectedBorder.Freeze(); _playhead.Freeze();

        InitializeComponent();
        _selectedTrack = _project.Tracks.FirstOrDefault();
        UpdateEngineText();
        UpdateTrackCount();
        RebuildTimeline();
        UpdateTransport();

        PreviewKeyDown += OnPreviewKeyDown;
        SizeChanged += (_, _) => { if (_project.AllClips().Any()) RebuildTimeline(); };
        Unloaded += (_, _) => StopPlay();
        _playTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _playTimer.Tick += PlayTick;
    }

    private static WpfBrush Res(string key, WpfBrush fallback) =>
        Application.Current?.TryFindResource(key) as WpfBrush ?? fallback;

    private Window GetOwner()
    {
        if (_owner != null) return _owner;
        _owner = Window.GetWindow(this) ?? Application.Current?.MainWindow;
        return _owner ?? new Window();
    }

    // ==================================================================== public API

    /// <summary>Loads a recording (or audio) file as the first clip and selects it.</summary>
    public void LoadRecording(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        OpenFile(path);
    }

    // ==================================================================== open / add

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open a recording or audio file",
            Filter = "Media files (*.mp4;*.mov;*.avi;*.mkv;*.webm;*.mp3;*.wav;*.aac;*.m4a;*.flac;*.wma;*.ogg)|*.mp4;*.mov;*.avi;*.mkv;*.webm;*.mp3;*.wav;*.aac;*.m4a;*.flac;*.wma;*.ogg|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog(GetOwner()) != true) return;
        OpenFile(dlg.FileName);
    }

    private async void OpenFile(string path)
    {
        SetStatus("Reading file…");
        try
        {
            ProbeInfo probe;
            try { probe = await FfmpegRunner.ProbeAsync(path); }
            catch { probe = new ProbeInfo(path, 0, 0, 0, false, false, 30); }

            _currentSource = path;
            SourceNameText.Text = Path.GetFileName(path);
            PreviewEmpty.Visibility = Visibility.Collapsed;

            bool video = probe.HasVideo;
            var track = _project.Tracks.FirstOrDefault(t => t.Kind == (video ? TrackKind.Video : TrackKind.Audio) && t.Clips.Count == 0)
                        ?? _project.Tracks.FirstOrDefault(t => t.Kind == (video ? TrackKind.Video : TrackKind.Audio))
                        ?? _project.Tracks[0];
            double dur = probe.Duration > 0.5 ? probe.Duration : 2.0;
            _currentDur = dur;
            AppendClip(track, path, 0, dur, track.End);
            _selectedTrack = track;
            _project.Selected = track.Clips.Last();
            _playTime = 0;
            RebuildTimeline();
            UpdateTransport();
            SetStatus($"Opened · {dur:0.##}s, {probe.Width}×{probe.Height}");
            ShowStaticFrame(0);
        }
        catch (Exception ex)
        {
            Logger.Error("Open failed", ex);
            MessageDialog.Error(GetOwner(), "Could not open file", ex.Message);
        }
    }

    private void AddClip_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSource == null)
        {
            SetStatus("Open a file first, then add clips.");
            Open_Click(sender, e);
            return;
        }
        var track = _selectedTrack ?? _project.Tracks[0];
        double dur = ClipDuration();
        AppendClip(track, _currentSource, 0, dur, track.End);
        _project.Selected = track.Clips.Last();
        RebuildTimeline();
        UpdateTransport();
        SetStatus("Clip added");
    }

    // ==================================================================== edit operations

    private void Split_Click(object sender, RoutedEventArgs e) => SplitAtPlayhead();

    private void SplitAtPlayhead()
    {
        var clip = _project.Selected
                   ?? FindClipAt(_playTime);
        if (clip == null) { SetStatus("Nothing to split — select a clip and move the playhead inside it."); return; }
        double t = _playTime;
        if (t <= clip.TimelineStart + 0.03 || t >= clip.End - 0.03)
        { SetStatus("Move the playhead into the clip to split it."); return; }

        var track = _project.TrackOf(clip)!;
        track.Clips.Remove(clip);
        double leftDur = t - clip.TimelineStart;
        var left = new TimelineClip { SourcePath = clip.SourcePath, SourceIn = clip.SourceIn, Duration = leftDur, TimelineStart = clip.TimelineStart };
        var right = new TimelineClip { SourcePath = clip.SourcePath, SourceIn = clip.SourceIn + leftDur, Duration = clip.End - t, TimelineStart = t };
        int insertAt = track.Clips.FindIndex(x => x.TimelineStart >= t);
        if (insertAt < 0) insertAt = track.Clips.Count;
        track.Clips.Insert(insertAt, left);
        track.Clips.Insert(insertAt + 1, right);
        _project.Selected = right;
        RebuildTimeline();
        UpdateTransport();
        SetStatus("Split at playhead");
    }

    private void Cut_Click(object sender, RoutedEventArgs e) => CutSelected();

    private void CutSelected()
    {
        var clip = _project.Selected;
        if (clip == null) { SetStatus("Select a clip to cut it."); return; }
        var track = _project.TrackOf(clip);
        if (track != null) track.Clips.Remove(clip);
        _project.Selected = null;
        RebuildTimeline();
        UpdateTransport();
        SetStatus("Cut");
    }

    private void Snap_Changed(object sender, RoutedEventArgs e)
    {
        _snapping = SnapButton.IsChecked == true;
        SetStatus(_snapping ? "Snap on" : "Snap off");
    }

    private void AddVideo_Click(object sender, RoutedEventArgs e) => AddTrack(TrackKind.Video);
    private void AddAudio_Click(object sender, RoutedEventArgs e) => AddTrack(TrackKind.Audio);

    private void AddTrack(TrackKind kind)
    {
        if (!_project.CanAdd(kind))
        { SetStatus($"Max {TimelineConstants.MaxTracks} tracks reached."); return; }
        var track = _project.AddTrack(kind)!;
        _selectedTrack = track;
        RebuildTimeline();
        UpdateTrackCount();
        SetStatus($"Added {kind} track ({_project.Tracks.Count}/{TimelineConstants.MaxTracks})");
    }

    private void RemoveTrack(int id)
    {
        var t = _project.Find(id);
        if (t == null) return;
        if (MessageDialog.Confirm(GetOwner(), "Remove track",
            $"Remove “{t.Label}” and its {t.Clips.Count} clip(s)?", "Remove") != true) return;
        _project.RemoveTrack(id);
        if (_selectedTrack == null || _project.Tracks.All(x => x.Id != _selectedTrack.Id))
            _selectedTrack = _project.Tracks.FirstOrDefault();
        RebuildTimeline();
        UpdateTrackCount();
        SetStatus("Track removed");
    }

    // ==================================================================== zoom / transport

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_pps * 1.4);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_pps / 1.4);

    private void ZoomButton_Click(object sender, RoutedEventArgs e)
    {
        int percent = (int)Math.Round(_pps / 60 * 100);
        int next = percent switch
        {
            100 => 150,
            150 => 200,
            200 => 100,
            _ => 100
        };
        SetZoom(60 * next / 100.0);
    }

    private void SetZoom(double pps)
    {
        _pps = Math.Clamp(pps, 20, 400);
        ZoomButton.Content = $"{(int)Math.Round(_pps / 60 * 100)}%";
        RebuildTimeline();
    }

    private void Play_Click(object sender, RoutedEventArgs e) => TogglePlay();

    private void TogglePlay()
    {
        if (_project.AllClips().Count() == 0) { SetStatus("Add a clip to preview."); return; }
        if (_playing) { StopPlay(); return; }
        if (_playTime >= _project.Duration - 0.02) _playTime = 0;
        _playing = true;
        _decoderClip = null;                        // force a fresh decoder for the clip under the playhead
        if (PlayGlyph != null) PlayGlyph.Text = "\uE769";
        if (PlayButton != null) PlayButton.ToolTip = "Pause (Space)";
        _playTimer?.Start();
        SetStatus("Playing…");
    }

    private void PlayTick(object? sender, EventArgs e)
    {
        if (!_playing) return;
        _playTime += 0.033;
        double d = Math.Max(_project.Duration, 0.5);
        if (_playTime >= d)
        {
            _playTime = d;
            UpdatePlayheadVisual(d);
            StopPlay();
            return;
        }
        UpdatePlayheadVisual(_playTime);
        ShowPlaybackFrame(_playTime);
    }

    private void StopPlay()
    {
        _playing = false;
        _playTimer?.Stop();
        _decoder?.Stop();
        _decoder = null;
        _decoderClip = null;
        if (PlayGlyph != null) PlayGlyph.Text = "\uE768";
        if (PlayButton != null) PlayButton.ToolTip = "Play (Space)";
    }

    private void Scrub_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_scrubUpdating || _project.AllClips().Count() == 0) return;
        MovePlayhead(e.NewValue);
    }

    // ==================================================================== preview

    private void MovePlayhead(double t)
    {
        double d = Math.Max(_project.Duration, 0.5);
        t = Math.Clamp(t, 0, d);
        _playTime = t;
        UpdatePlayheadVisual(t);
        if (!_playing) ShowStaticFrame(t);
    }

    private void UpdatePlayheadVisual(double t)
    {
        double d = Math.Max(_project.Duration, 0.5);
        t = Math.Clamp(t, 0, d);
        if (_playheadLine != null) { _playheadLine.X1 = t * _pps; _playheadLine.X2 = t * _pps; }
        if (TimeText != null) TimeText.Text = $"{t:0.0} / {d:0.0} s";
        if (Scrub != null)
        {
            Scrub.Maximum = d;
            if (Math.Abs(Scrub.Value - t) > 1e-6)
            {
                _scrubUpdating = true;
                Scrub.Value = t;
                _scrubUpdating = false;
            }
        }
    }

    private TimelineClip? PreviewClipAt(double t)
    {
        var full = _project.Tracks.FirstOrDefault(x => x.Kind == TrackKind.Video && x.Placement == VideoPlacement.Full && !x.Hidden);
        var anyV = _project.Tracks.FirstOrDefault(x => x.Kind == TrackKind.Video && !x.Hidden);
        foreach (var tr in new[] { full, anyV }.Where(x => x != null).Distinct())
        {
            var clip = tr!.Clips.FirstOrDefault(c => t >= c.TimelineStart - 0.001 && t <= c.End + 0.001);
            if (clip != null) return clip;
        }
        return null;
    }

    // One-shot frame decode (paused scrubbing + the initial preview).
    private async void ShowStaticFrame(double t)
    {
        var clip = PreviewClipAt(t);
        if (clip == null || !File.Exists(clip.SourcePath))
        {
            if (PreviewImage != null) PreviewImage.Source = null;
            return;
        }
        if (_previewBusy) return;
        _previewBusy = true;
        try
        {
            double local = clip.SourceIn + (t - clip.TimelineStart);
            var bmp = await FramePreview.GetFrameAsync(clip.SourcePath, local, 512, 288);
            if (bmp != null)
            {
                using (bmp) PreviewImage.Source = BitmapUtils.ToBitmapSource(bmp);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("Preview frame decode failed: " + ex.Message);
        }
        finally
        {
            _previewBusy = false;
        }
    }

    // Smooth playback: show the frame for the playhead from the continuous decoder buffer.
    private void ShowPlaybackFrame(double t)
    {
        var clip = PreviewClipAt(t);
        if (clip == null || !File.Exists(clip.SourcePath)) { if (PreviewImage != null) PreviewImage.Source = null; return; }
        double srcTime = clip.SourceIn + (t - clip.TimelineStart);
        if (!ReferenceEquals(_decoderClip, clip))
        {
            _decoderClip = clip;
            _decoder?.Stop();
            _decoder = new PreviewDecoder();
            _decoder.Start(clip.SourcePath, clip.SourceIn, clip.Duration, 30, 512, 288);
        }
        var jpg = _decoder.FrameAt(srcTime);
        if (jpg == null) return;
        var src = DecodeJpeg(jpg);
        if (src != null) PreviewImage.Source = src;
    }

    private static BitmapSource? DecodeJpeg(byte[] jpg)
    {
        try
        {
            using var ms = new MemoryStream(jpg);
            using var gdi = System.Drawing.Image.FromStream(ms) as System.Drawing.Bitmap;
            if (gdi == null) return null;
            var src = BitmapUtils.ToBitmapSource(gdi);
            src.Freeze();
            return src;
        }
        catch { return null; }
    }

    // ==================================================================== export

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_exporting) return;
        if (!_project.AllClips().Any())
        {
            MessageDialog.Warning(GetOwner(), "Nothing to export", "Add a clip to a track first.");
            return;
        }
        var dlg = new SaveFileDialog
        {
            Title = "Export MP4",
            Filter = "MP4 video (*.mp4)|*.mp4",
            DefaultExt = "mp4",
            AddExtension = true,
            FileName = "Shutio Cut export.mp4",
            InitialDirectory = Directory.Exists(AppPaths.Exports) ? AppPaths.Exports : AppPaths.Data
        };
        if (dlg.ShowDialog(GetOwner()) != true) return;
        string outPath = dlg.FileName;

        _exporting = true;
        SetBusy(true, "Preparing export…");
        var progress = new Progress<string>(msg => { if (PreviewBusy != null) BusyText.Text = msg; SetStatus(msg); });
        try
        {
            var (ok, error, path) = await TimelineExport.ExportAsync(_project, outPath, progress);
            if (ok)
            {
                SetStatus($"Exported to {outPath}");
                MessageDialog.Info(GetOwner(), "Export complete", $"Your MP4 is ready.\n\n{path}");
            }
            else
            {
                SetStatus("Export failed");
                MessageDialog.Error(GetOwner(), "Export failed", error);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Export failed", ex);
            SetStatus("Export failed");
            MessageDialog.Error(GetOwner(), "Export failed", ex.Message);
        }
        finally
        {
            SetBusy(false, "");
            _exporting = false;
        }
    }

    // ==================================================================== timeline rendering

    private void RebuildTimeline()
    {
        Gutter.Children.Clear();
        Lanes.Children.Clear();

        var tracks = _project.Tracks;
        int n = tracks.Count;
        void AddRows(RowDefinitionCollection col)
        {
            col.Clear();
            col.Add(new RowDefinition { Height = new GridLength(RulerH) });
            for (int i = 0; i < n; i++) col.Add(new RowDefinition { Height = new GridLength(TrackH) });
        }

        double dur = Math.Max(_project.Duration, 2.0);
        double contentW = ComputeContentWidth(dur);
        double totalH = RulerH + n * TrackH;

        // ---- gutter (labels) ----
        AddRows(Gutter.RowDefinitions);
        var header = new TextBlock
        {
            Text = "Tracks",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = _textTertiary,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(header, 0);
        Gutter.Children.Add(header);
        for (int i = 0; i < n; i++)
        {
            var label = BuildTrackLabel(tracks[i]);
            Grid.SetRow(label, i + 1);
            Gutter.Children.Add(label);
        }

        // ---- lanes ----
        Lanes.Width = contentW;
        AddRows(Lanes.RowDefinitions);

        var ruler = BuildRuler(contentW, dur);
        Grid.SetRow(ruler, 0);
        Lanes.Children.Add(ruler);

        for (int i = 0; i < n; i++)
        {
            var track = tracks[i];
            var canvas = new Canvas
            {
                Width = contentW,
                Height = TrackH,
                Background = i % 2 == 0 ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(18, 255, 255, 255))
            };
            var trackRef = track;
            canvas.MouseLeftButtonDown += (_, me) =>
            {
                MovePlayhead(me.GetPosition(canvas).X / _pps);
                _project.Selected = null;
                RebuildTimeline();
            };
            Grid.SetRow(canvas, i + 1);
            Lanes.Children.Add(canvas);
            foreach (var clip in track.Clips.OrderBy(c => c.TimelineStart))
                canvas.Children.Add(BuildClip(trackRef, clip));
        }

        // ---- playhead overlay (spans ruler + all tracks) ----
        var overlay = new Canvas { Width = contentW, IsHitTestVisible = false, Background = Brushes.Transparent };
        Grid.SetRow(overlay, 0);
        Grid.SetRowSpan(overlay, n + 1);
        var ph = new Line { X1 = _playTime * _pps, X2 = _playTime * _pps, Y1 = 0, Y2 = totalH, Stroke = _playhead, StrokeThickness = 2 };
        overlay.Children.Add(ph);
        Lanes.Children.Add(overlay);
        _playheadLine = ph;

        if (TimelineEmpty != null)
            TimelineEmpty.Visibility = _project.AllClips().Any() ? Visibility.Collapsed : Visibility.Visible;
    }

    private double ComputeContentWidth(double dur)
    {
        double right = LanesScroller?.ActualWidth > 0 ? LanesScroller.ActualWidth - 40 : 820;
        return Math.Max(320, Math.Max(right, dur * _pps + 80));
    }

    private UIElement BuildRuler(double contentW, double dur)
    {
        var c = new Canvas
        {
            Width = contentW,
            Height = RulerH,
            Background = _altSurface
        };
        c.MouseLeftButtonDown += (_, me) => MovePlayhead(me.GetPosition(c).X / _pps);

        double step = _pps >= 40 ? 1.0 : (_pps >= 20 ? 2.0 : 5.0);
        int maxTicks = 400;
        double t = 0;
        int count = 0;
        for (; t <= dur + 0.001 && count < maxTicks; t += step, count++)
        {
            double x = t * _pps;
            c.Children.Add(new Line { X1 = x, X2 = x, Y1 = RulerH - 8, Y2 = RulerH - 2, Stroke = _border, StrokeThickness = 1 });
            var label = new TextBlock
            {
                Text = $"{(t == Math.Floor(t) ? ((int)t).ToString() : t.ToString("0.0"))}s",
                FontSize = 9,
                Foreground = _textSecondary,
                Margin = new Thickness(2, 0, 0, 0)
            };
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, 1);
            c.Children.Add(label);
        }
        return c;
    }

    private UIElement BuildTrackLabel(TimelineTrack track)
    {
        bool video = track.Kind == TrackKind.Video;
        bool selected = _selectedTrack == track;
        var grid = new Grid { Margin = new Thickness(0, 3, 6, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        left.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width = 10,
            Height = 10,
            RadiusX = 2,
            RadiusY = 2,
            Fill = video ? _videoFill : _audioFill,
            Stroke = video ? _videoBorder : _audioBorder,
            StrokeThickness = 1,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        var label = new TextBlock
        {
            Text = track.Label + (video ? "  " + ShortPlacement(track.Placement) : ""),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = _textSecondary,
            VerticalAlignment = VerticalAlignment.Center
        };
        left.Children.Add(label);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        // Mute (audio) / Hide (video) toggle.
        var toggle = new ToggleButton
        {
            FontFamily = FontFamilyOfIcon(),
            FontSize = 12,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Padding = new Thickness(2),
            Cursor = Cursors.Hand,
            ToolTip = video ? "Show / hide this track" : "Mute / unmute this track",
            Content = video ? "\uE890" : "\uE767",
            IsChecked = video ? !track.Hidden : !track.Muted
        };
        toggle.Click += (_, _) =>
        {
            if (video) track.Hidden = toggle.IsChecked != true;
            else track.Muted = toggle.IsChecked != true;
            RebuildTimeline();
            UpdateTransport();
        };
        right.Children.Add(toggle);

        if (_project.CanRemove())
        {
            var del = new Button
            {
                FontFamily = FontFamilyOfIcon(),
                FontSize = 12,
                Content = "\uE73D",
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Padding = new Thickness(3, 2, 0, 2),
                Cursor = Cursors.Hand,
                ToolTip = "Remove this track"
            };
            del.Click += (_, _) => RemoveTrack(track.Id);
            right.Children.Add(del);
        }
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        grid.Background = selected
            ? new SolidColorBrush(Color.FromArgb(28, 0, 103, 192))
            : Brushes.Transparent;
        grid.MouseLeftButtonDown += (_, _) =>
        {
            _selectedTrack = track;
            RebuildTimeline();
        };
        return grid;
    }

    private static string ShortPlacement(VideoPlacement p) => p switch
    {
        VideoPlacement.Full => "Full",
        VideoPlacement.TopLeft => "Top-left PiP",
        VideoPlacement.TopRight => "Top-right PiP",
        VideoPlacement.BottomLeft => "Bottom-left PiP",
        VideoPlacement.BottomRight => "Bottom-right PiP",
        _ => ""
    };

    private Border BuildClip(TimelineTrack track, TimelineClip clip)
    {
        bool video = track.Kind == TrackKind.Video;
        bool sel = ReferenceEquals(_project.Selected, clip);
        var b = new Border
        {
            Background = sel ? _selectedFill : (video ? _videoFill : _audioFill),
            BorderBrush = sel ? _selectedBorder : (video ? _videoBorder : _audioBorder),
            BorderThickness = new Thickness(sel ? 2 : 1),
            CornerRadius = new CornerRadius(5),
            Width = Math.Max(12, clip.Duration * _pps),
            Height = TrackH - 10,
            Margin = new Thickness(1, 0, 1, 0),
            Cursor = Cursors.SizeWE
        };
        b.Tag = clip;
        Canvas.SetLeft(b, Math.Max(0, clip.TimelineStart * _pps));
        Canvas.SetTop(b, 5);
        b.ToolTip = $"{Path.GetFileName(clip.SourcePath)}\n{clip.Duration:0.##}s  ·  at {clip.TimelineStart:0.##}s";
        var text = new TextBlock
        {
            Text = Path.GetFileName(clip.SourcePath),
            FontSize = 11,
            Foreground = _clipText,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        b.Child = text;
        b.MouseLeftButtonDown += Clip_MouseDown;
        return b;
    }

    // ==================================================================== clip drag / trim / snap

    private void Clip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b) return;
        var clip = (TimelineClip)((object?)b.Tag ?? null) ?? FindClipByBorder(b);
        if (clip == null) return;

        var pos = e.GetPosition(b);
        double w = b.Width;
        _mode = pos.X < 9 ? DragMode.TrimLeft : (pos.X > w - 9 ? DragMode.TrimRight : DragMode.Move);

        _project.Selected = clip;
        _selectedTrack = _project.TrackOf(clip) ?? _selectedTrack;

        _dragBorder = b;
        _dragClip = clip;
        _dragStartX = e.GetPosition(this).X;
        _origStart = clip.TimelineStart;
        _origDur = clip.Duration;
        _origIn = clip.SourceIn;
        b.MouseMove += Drag_MouseMove;
        b.MouseUp += Drag_MouseUp;
        b.CaptureMouse();
    }

    private TimelineClip? FindClipByBorder(Border b)
    {
        foreach (var t in _project.Tracks)
            foreach (var c in t.Clips)
                if (Math.Abs(Math.Max(0, c.TimelineStart * _pps) - (Canvas.GetLeft(b) + b.Margin.Left)) < 0.5 &&
                    Math.Abs(c.Duration * _pps - b.Width) < 1.5)
                    return c;
        // Fallback: match by the tooltip / child text.
        string? name = b.Child as TextBlock is TextBlock tb ? tb.Text : null;
        if (name == null) return null;
        foreach (var t in _project.Tracks)
            foreach (var c in t.Clips)
                if (Path.GetFileName(c.SourcePath) == name) return c;
        return null;
    }

    private void Drag_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragBorder == null || _dragClip == null) return;
        var track = _project.TrackOf(_dragClip);
        if (track == null) return;

        double dx = (e.GetPosition(this).X - _dragStartX) / _pps;
        double newStart = _origStart, newDur = _origDur, newIn = _origIn;

        switch (_mode)
        {
            case DragMode.Move:
                newStart = _origStart + dx;
                if (newStart < 0) newStart = 0;
                if (_snapping) newStart = SnapTime(newStart, _dragClip, anchor: 0);
                break;
            case DragMode.TrimLeft:
                newStart = _origStart + dx;
                newIn = _origIn + dx;
                newDur = _origDur - dx;
                if (newDur < 0.2) { newDur = 0.2; newStart = _origStart + (_origDur - 0.2); newIn = _origIn + (_origDur - 0.2); }
                if (newIn < 0) { newIn = 0; newStart = _origStart; }
                if (newStart < 0) newStart = 0;
                if (_snapping) newStart = Math.Max(0, SnapTime(newStart, _dragClip, anchor: 0));
                break;
            case DragMode.TrimRight:
                newDur = _origDur + dx;
                if (newDur < 0.2) newDur = 0.2;
                if (_snapping)
                {
                    double edge = SnapTime(_origStart + newDur, _dragClip, anchor: 1);
                    if (edge > _origStart + 0.2) newDur = edge - _origStart;
                }
                break;
        }

        _dragClip.TimelineStart = newStart;
        _dragClip.Duration = newDur;
        _dragClip.SourceIn = newIn;

        // Live-move the border without a full rebuild (cheap).
        Canvas.SetLeft(_dragBorder, Math.Max(0, newStart * _pps));
        _dragBorder.Width = Math.Max(12, newDur * _pps);
        _dragBorder.ToolTip = $"{Path.GetFileName(_dragClip.SourcePath)}\n{newDur:0.##}s  ·  at {newStart:0.##}s";
    }

    private void Drag_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragBorder == null) return;
        try
        {
            _dragBorder.MouseMove -= Drag_MouseMove;
            _dragBorder.MouseUp -= Drag_MouseUp;
            _dragBorder.ReleaseMouseCapture();
        }
        catch { }
        _dragBorder = null;
        _dragClip = null;
        // Full refresh so the playhead, selection and labels stay in sync.
        RebuildTimeline();
        UpdateTransport();
    }

    private double SnapTime(double value, TimelineClip? exclude, int anchor)
    {
        double thr = SnapPx / _pps;
        var candidates = new List<double> { 0, _playTime };
        foreach (var t in _project.Tracks)
            foreach (var c in t.Clips)
            {
                if (ReferenceEquals(c, exclude)) continue;
                candidates.Add(c.TimelineStart);
                candidates.Add(c.End);
            }
        double best = value;
        double bestDist = thr;
        foreach (var cand in candidates)
        {
            double d = Math.Abs(cand - value);
            if (d <= bestDist) { bestDist = d; best = cand; }
        }
        return best;
    }

    // ==================================================================== helpers

    private TimelineClip? FindClipAt(double t)
    {
        var list = _project.Tracks.Where(x => x.Kind == TrackKind.Video && !x.Hidden).ToList();
        list.Reverse();
        foreach (var t2 in list)
        {
            var clip = t2.Clips.FirstOrDefault(c => t >= c.TimelineStart - 0.001 && t <= c.End + 0.001);
            if (clip != null) return clip;
        }
        return null;
    }

    private double ClipDuration() => Math.Max(0.5, _currentDur);

    private void AppendClip(TimelineTrack track, string source, double inPoint, double dur, double start)
    {
        var clip = new TimelineClip { SourcePath = source, SourceIn = inPoint, Duration = dur, TimelineStart = Math.Max(0, start) };
        track.Clips.Add(clip);
        track.Clips.Sort((a, b) => a.TimelineStart.CompareTo(b.TimelineStart));
    }

    private void SetStatus(string text)
    {
        if (StatusText != null) StatusText.Text = text;
    }

    private void SetBusy(bool busy, string text)
    {
        if (PreviewBusy == null) return;
        PreviewBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy && !string.IsNullOrEmpty(text)) BusyText.Text = text;
        if (OpenButton != null) OpenButton.IsEnabled = !busy;
        if (ExportButton != null) ExportButton.IsEnabled = !busy;
    }

    private void UpdateTrackCount()
    {
        if (TrackCountText != null)
            TrackCountText.Text = $"{_project.Tracks.Count}/{TimelineConstants.MaxTracks} tracks";
        if (AddVideoButton != null) AddVideoButton.IsEnabled = _project.CanAdd(TrackKind.Video);
        if (AddAudioButton != null) AddAudioButton.IsEnabled = _project.CanAdd(TrackKind.Audio);
    }

    private void UpdateTransport()
    {
        if (Scrub == null) return;
        Scrub.IsEnabled = _project.AllClips().Any();
        Scrub.Maximum = Math.Max(_project.Duration, 0.5);
        Scrub.Value = Math.Clamp(_playTime, 0, Scrub.Maximum);
        TimeText.Text = $"{Math.Clamp(_playTime, 0, Scrub.Maximum):0.0} / {Scrub.Maximum:0.0} s";
    }

    private void UpdateEngineText()
    {
        if (EngineText == null) return;
        EngineText.Text = FfmpegRunner.Available
            ? $"Engine ready · {Path.GetFileName(FfmpegRunner.Ffmpeg)}"
            : "ffmpeg engine not found — export is disabled.";
    }

    private static FontFamily FontFamilyOfIcon()
    {
        if (Application.Current?.TryFindResource("IconFont") is FontFamily ff) return ff;
        return new FontFamily("Segoe MDL2 Assets");
    }

    // ==================================================================== keyboard

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (control && e.Key == Key.K) { SplitAtPlayhead(); e.Handled = true; }
        else if (e.Key == Key.Delete || (e.Key == Key.Back && _project.Selected != null)) { CutSelected(); e.Handled = true; }
        else if (e.Key == Key.Space) { TogglePlay(); e.Handled = true; }
        else if (e.Key == Key.Home) { MovePlayhead(0); e.Handled = true; }
        else if (e.Key == Key.End) { MovePlayhead(_project.Duration); e.Handled = true; }
    }
}
