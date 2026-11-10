using System.Diagnostics;
using System.Globalization;
using SnippingTool.Core;

namespace SnippingTool.Editing;

/// <summary>
/// Decodes a video source as a real-time stream of JPEG frames using ONE ffmpeg process
/// (<c>image2pipe</c>) instead of re-seeking per frame. Frames are read on a background
/// thread into a bounded, time-indexed buffer, so the preview can play smoothly and can
/// also be scrubbed by asking for the frame nearest a given source time.
/// </summary>
public sealed class PreviewDecoder : IDisposable
{
    private const int MaxKept = 240;      // frames to keep in the buffer (~few MB of JPEGs)

    private readonly object _gate = new();
    private readonly Dictionary<long, byte[]> _frames = new();
    private Process? _proc;
    private Task? _reader;
    private CancellationTokenSource? _cts;
    private long _minIdx = -1, _maxIdx = -1;
    private double _firstTime;
    private double _fps = 30;

    public bool IsRunning { get { lock (_gate) return _proc is { HasExited: false }; } }

    /// <summary>
    /// Starts decoding <paramref name="source"/> from <paramref name="firstTime"/> for
    /// <paramref name="duration"/> seconds at <paramref name="fps"/> frames/s.
    /// </summary>
    public void Start(string source, double firstTime, double duration, double fps, int width, int height)
    {
        StopInternal();
        _fps = Math.Clamp(fps, 10, 60);
        _firstTime = firstTime;
        lock (_gate) { _frames.Clear(); _minIdx = _maxIdx = -1; }

        int w = Math.Max(160, width), h = Math.Max(90, height);
        if (w % 2 != 0) w -= 1;
        if (h % 2 != 0) h -= 1;

        var psi = new ProcessStartInfo
        {
            FileName = FfmpegRunner.Ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in new[]
        {
            "-hide_banner", "-loglevel", "error",
            "-ss", firstTime.ToString("0.###", CultureInfo.InvariantCulture),
            "-i", source,
            "-t", Math.Max(0.2, duration).ToString("0.###", CultureInfo.InvariantCulture),
            "-vf", $"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:color=black",
            "-r", _fps.ToString(),
            "-f", "image2pipe", "-vcodec", "mjpeg", "-q:v", "6",
            "-an", "-"
        })
            psi.ArgumentList.Add(a);

        _cts = new CancellationTokenSource();
        _proc = new Process { StartInfo = psi };
        _proc.ErrorDataReceived += (_, _) => { };
        if (!_proc.Start()) return;
        _proc.BeginErrorReadLine();
        _reader = Task.Run(() => ReadLoop(_proc, _cts.Token));
    }

    private void ReadLoop(Process p, CancellationToken ct)
    {
        var stream = p.StandardOutput.BaseStream;
        var buf = new byte[65536];
        var acc = new List<byte>();
        int start = -1;
        long idx = 0;
        try
        {
            int n;
            while (!ct.IsCancellationRequested && (n = stream.Read(buf, 0, buf.Length)) > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    byte b = buf[i];
                    acc.Add(b);
                    if (acc.Count < 2) continue;
                    byte prev = acc[acc.Count - 2];
                    if (prev == 0xFF && b == 0xD8 && start < 0)
                    {
                        start = acc.Count - 2;                 // start of a new JPEG
                    }
                    else if (prev == 0xFF && b == 0xD9 && start >= 0)
                    {
                        var frame = acc.GetRange(start, acc.Count - start).ToArray();
                        acc.Clear();
                        start = -1;
                        if (frame.Length > 60) AddFrame(idx++, frame);
                    }
                }
            }
        }
        catch
        {
            // Stream ended or the process was killed — the buffer keeps whatever we have.
        }
    }

    private void AddFrame(long idx, byte[] bytes)
    {
        lock (_gate)
        {
            _frames[idx] = bytes;
            if (_minIdx < 0) _minIdx = idx;
            _maxIdx = idx;
            int excess = _frames.Count - MaxKept;
            for (int k = 0; k < excess; k++)
            {
                _frames.Remove(_minIdx + k);
                _minIdx++;
            }
        }
    }

    /// <summary>Returns the JPEG bytes for the frame nearest (at or before) <paramref name="sourceTime"/>, or null.</summary>
    public byte[]? FrameAt(double sourceTime)
    {
        long want = (long)Math.Round((sourceTime - _firstTime) * _fps);
        lock (_gate)
        {
            if (_maxIdx < 0) return null;
            long idx = Math.Clamp(want, _minIdx, _maxIdx);
            byte[]? found = null;
            while (idx >= _minIdx)
            {
                if (_frames.TryGetValue(idx, out found)) break;
                idx--;
            }
            return idx >= _minIdx ? found : null;
        }
    }

    public void Stop() => StopInternal();

    private void StopInternal()
    {
        try { _cts?.Cancel(); } catch { }
        try { if (_proc is { HasExited: false }) _proc.Kill(entireProcessTree: true); } catch { }
        try { _proc?.Dispose(); } catch { }
        _proc = null;
        _reader = null;
        _cts = null;
    }

    public void Dispose() => Stop();
}
