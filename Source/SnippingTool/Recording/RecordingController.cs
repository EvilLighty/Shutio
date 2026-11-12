using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using SnippingTool.Capture;
using SnippingTool.Core;

namespace SnippingTool.Recording;

/// <summary>
/// Everything needed to start a recording, collected by the UI (the Recording tab) and
/// consumed by <c>AppHost</c>. Kept separate from the ffmpeg <see cref="RecordingOptions"/>
/// so the controller stays a pure engine.
/// </summary>
public sealed class RecordingConfig
{
    public RecordingSource Source { get; set; } = RecordingSource.Fullscreen;
    public int Fps { get; set; } = 15;
    public bool IncludeCursor { get; set; } = true;
    public bool IncludeAudio { get; set; }
    public string Microphone { get; set; } = "";
    public int TargetHeight { get; set; } = 0;      // 0 = native
    public bool SeparateAudio { get; set; }
    public bool WebcamEnabled { get; set; }
    public string WebcamDevice { get; set; } = "";
    public int WebcamSize { get; set; } = 1;        // 0 = S, 1 = M, 2 = L
    public double WebcamX { get; set; } = 0.76;     // 0..1 fraction of output width (left edge of the PiP)
    public double WebcamY { get; set; } = 0.62;     // 0..1 fraction of output height (top edge of the PiP)
    public bool WebcamMirror { get; set; } = true;
}

public sealed class RecordingOptions
{
    public Rectangle Region { get; init; }
    public int Fps { get; init; } = 15;
    public bool IncludeCursor { get; init; } = true;
    public bool IncludeAudio { get; init; }
    public string Microphone { get; init; } = "";
    public string OutputPath { get; init; } = "";

    // Output resolution. 0 = native (record at the captured size); otherwise the target output height.
    public int TargetHeight { get; init; } = 0;

    // When true (and audio is on) the video is saved WITHOUT audio and the microphone is
    // saved as a separate lossless .wav file, so the editor can treat them independently.
    public bool SeparateAudio { get; init; }

    // Live on-screen webcam preview (a second output stream of the webcam feed, shown in a
    // small topmost window while recording). Uses the SAME ffmpeg process / same capture,
    // so the camera is opened exactly once and the preview mirrors the recorded PiP.
    public bool LivePreview { get; init; }

    // Webcam picture-in-picture overlay.
    public bool WebcamEnabled { get; init; }
    public string WebcamDevice { get; init; } = "";
    public int WebcamSize { get; init; } = 1;    // 0 = S, 1 = M, 2 = L
    public double WebcamX { get; init; } = 0.76;  // 0..1 fraction of output width (left edge of the PiP)
    public double WebcamY { get; init; } = 0.62;  // 0..1 fraction of output height (top edge of the PiP)
    public bool WebcamMirror { get; init; } = true;
}

public sealed class RecordingStoppedEventArgs : EventArgs
{
    public string OutputPath { get; init; } = "";
    public string AudioOutputPath { get; init; } = "";
    public bool Success { get; init; }
    public string? Error { get; init; }
    public TimeSpan Duration { get; init; }
    public int Frames { get; init; }
}

public sealed class RecordingController : IDisposable
{
    private Process? _ffmpeg;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly StringBuilder _stderr = new();
    private readonly Stopwatch _elapsed = new();
    private int _frames;
    private string? _error;
    private bool _disposed;
    private CancellationTokenSource? _previewCts;
    private Task? _previewReader;
    private readonly object _previewGate = new();
    private byte[]? _previewFrame;

    public bool IsRecording { get; private set; }
    public string OutputPath { get; private set; } = "";
    public string AudioOutputPath { get; private set; } = "";
    public TimeSpan Elapsed => _elapsed.Elapsed;
    public int Frames => _frames;
    public string? LastError => _error;

    // Live webcam preview stream (raw BGRA frames), when LivePreview was requested.
    public static readonly (int Width, int Height) PreviewSize = (320, 180);
    public int PreviewWidth { get; private set; }
    public int PreviewHeight { get; private set; }
    public bool HasPreview => PreviewWidth > 0 && PreviewHeight > 0;

    /// <summary>
    /// Returns the most recent webcam preview frame (BGRA, row-major, width*height*4 bytes)
    /// when the live preview is running; false until the first frame arrives.
    /// </summary>
    public bool TryGetLatestPreviewFrame(out byte[] frame, out int width, out int height)
    {
        lock (_previewGate)
        {
            if (_previewFrame != null && PreviewWidth > 0)
            {
                frame = _previewFrame;
                width = PreviewWidth;
                height = PreviewHeight;
                return true;
            }
        }
        frame = Array.Empty<byte>();
        width = 0;
        height = 0;
        return false;
    }

    public static string? FfmpegPath
    {
        get
        {
            try
            {
                string direct = Path.Combine(AppPaths.Tools, "ffmpeg.exe");
                if (File.Exists(direct)) return direct;
                string root = Path.Combine(AppPaths.Tools, "ffmpeg");
                if (Directory.Exists(root))
                {
                    var found = Directory.GetFiles(root, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (found != null) return found;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not locate ffmpeg", ex);
            }
            return null;
        }
    }

    public static bool IsAvailable => FfmpegPath != null;

    public event EventHandler<RecordingStoppedEventArgs>? Stopped;

    public void Start(RecordingOptions options)
    {
        if (IsRecording) throw new InvalidOperationException("A recording is already in progress.");
        string? ffmpeg = FfmpegPath;
        if (ffmpeg == null)
            throw new InvalidOperationException("The recording engine (ffmpeg) is not installed in the Tools folder.");

        var region = Rectangle.Intersect(options.Region, CaptureService.VirtualScreen);
        if (region.Width < 8 || region.Height < 8) throw new InvalidOperationException("The recording area is too small.");
        // H.264 requires even dimensions.
        if (region.Width % 2 != 0) region.Width -= 1;
        if (region.Height % 2 != 0) region.Height -= 1;
        if (region.Width < 8 || region.Height < 8) throw new InvalidOperationException("The recording area is too small.");

        int fps = options.Fps is >= 5 and <= 60 ? options.Fps : 15;

        // Output resolution. 0 = native; otherwise scale the video down to this height (even dims for H.264).
        int outW = region.Width, outH = region.Height;
        if (options.TargetHeight > 0 && options.TargetHeight < region.Height)
        {
            outH = options.TargetHeight % 2 == 0 ? options.TargetHeight : options.TargetHeight - 1;
            outW = (int)Math.Round(region.Width * (double)outH / region.Height);
            if (outW % 2 != 0) outW -= 1;
            if (outW < 8 || outH < 8) { outW = region.Width; outH = region.Height; }
        }
        bool scaled = outW != region.Width || outH != region.Height;

        string output = options.OutputPath;
        if (string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(AppPaths.Recordings);
            output = Path.Combine(AppPaths.Recordings, $"Recording_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? AppPaths.Recordings);
        if (File.Exists(output))
            output = Path.Combine(Path.GetDirectoryName(output) ?? AppPaths.Recordings,
                Path.GetFileNameWithoutExtension(output) + "_" + DateTime.Now.ToString("HHmmss") + ".mp4");

        bool wantAudio = options.IncludeAudio && !string.IsNullOrWhiteSpace(options.Microphone);
        bool separate = wantAudio && options.SeparateAudio;
        string? audioOutput = separate
            ? Path.Combine(Path.GetDirectoryName(output) ?? AppPaths.Recordings,
                Path.GetFileNameWithoutExtension(output) + "_audio.wav")
            : null;

        bool webcam = options.WebcamEnabled && !string.IsNullOrWhiteSpace(options.WebcamDevice);
        // Input layout: 0 = screen (pipe), 1 = webcam (if any), last = microphone (if any).
        int micIndex = webcam ? 2 : 1;

        // Fail fast with a clear, actionable message. Handing ffmpeg a device name it can't
        // open (e.g. the "No webcam found" placeholder) makes it die a second after start and
        // the stop then fails with the cryptic "the pipe is being closed" — far better to
        // refuse the start with the real reason.
        if (options.WebcamEnabled) ValidateWebcam(options.WebcamDevice);
        if (options.IncludeAudio) ValidateMicrophone(options.Microphone);

        // Webcam overlay geometry, computed in OUTPUT pixels so it stays proportional at any resolution.
        int camW = 0, camH = 0, camX = 0, camY = 0;
        if (webcam)
        {
            int denom = options.WebcamSize switch { 0 => 5, 2 => 3, _ => 4 };
            camH = Math.Max(48, outH / denom);
            if (camH % 2 != 0) camH -= 1;
            camW = (int)Math.Round(camH * 16.0 / 9.0);
            if (camW % 2 != 0) camW -= 1;
            double fx = Math.Clamp(options.WebcamX, 0.0, 1.0);
            double fy = Math.Clamp(options.WebcamY, 0.0, 1.0);
            camX = Math.Clamp((int)Math.Round(fx * (outW - camW)), 0, Math.Max(0, outW - camW));
            camY = Math.Clamp((int)Math.Round(fy * (outH - camH)), 0, Math.Max(0, outH - camH));
        }

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        var a = psi.ArgumentList;
        a.Add("-hide_banner");
        a.Add("-loglevel"); a.Add("error");
        // Input 0: the screen frames we pipe in (raw BGR A).
        a.Add("-f"); a.Add("rawvideo");
        a.Add("-pix_fmt"); a.Add("bgra");
        a.Add("-s"); a.Add($"{region.Width}x{region.Height}");
        a.Add("-r"); a.Add(fps.ToString());
        a.Add("-i"); a.Add("pipe:0");
        // Input 1 (optional): the webcam, via DirectShow under its exact device name.
        if (webcam)
        {
            a.Add("-f"); a.Add("dshow");
            a.Add("-i"); a.Add("video=" + options.WebcamDevice);
        }
        // Input (last, optional): the microphone, via DirectShow.
        if (wantAudio)
        {
            a.Add("-f"); a.Add("dshow");
            a.Add("-i"); a.Add("audio=" + options.Microphone);
        }

        bool audioInVideo = wantAudio && !separate;
        bool livePreview = webcam && options.LivePreview;
        int prevW = PreviewSize.Width, prevH = PreviewSize.Height;
        if (webcam)
        {
            // Compose screen + webcam overlay, optionally scale, then map the result.
            // eof_action=endall is the stop fix: with the default (repeat) the infinite webcam
            // feed keeps the composite alive after the finite screen pipe ends, so "Stop"
            // hangs until the timeout kills ffmpeg mid-flush (corrupt MP4 + false failure).
            // With endall the overlay ends with the screen and the whole graph goes finite.
            string overlay = $"overlay={camX}:{camY}:eof_action=endall";
            string mirror = options.WebcamMirror ? ",hflip" : "";
            string baseChain = scaled ? $"[0:v]scale={outW}:{outH}[base];" : "[0:v]null[base];";
            // Live preview: derived from the COMPOSITED output ([vout]) as a crop of the exact
            // PiP region — NOT from the infinite cam feed. That keeps the preview identical to
            // what is recorded (same scale, same mirror, same position) AND makes every branch
            // of the graph finite, so the moment the screen input ends ffmpeg finalizes and
            // exits on its own (no timeout kill, no held-open camera).
            string graph = livePreview
                ? $"{baseChain}[1:v]scale={camW}:{camH}{mirror}[cam];[base][cam]{overlay}[vout];[vout]split=2[vmp4][vcrop];[vcrop]crop={camW}:{camH}:{camX}:{camY},scale={prevW}:{prevH}[prev]"
                : $"{baseChain}[1:v]scale={camW}:{camH}{mirror}[cam];[base][cam]{overlay}[vout]";
            a.Add("-filter_complex");
            a.Add(graph);
            a.Add("-map"); a.Add(livePreview ? "[vmp4]" : "[vout]");
            if (audioInVideo) { a.Add("-map"); a.Add($"{micIndex}:a:0"); a.Add("-c:a"); a.Add("aac"); a.Add("-b:a"); a.Add("160k"); }
            else a.Add("-an");
        }
        else
        {
            if (scaled) { a.Add("-vf"); a.Add($"scale={outW}:{outH}"); }
            a.Add("-map"); a.Add("0:v:0");
            if (audioInVideo) { a.Add("-map"); a.Add($"{micIndex}:a:0"); a.Add("-c:a"); a.Add("aac"); a.Add("-b:a"); a.Add("160k"); }
            else a.Add("-an");
        }

        // Output 1: the video file.
        a.Add("-c:v"); a.Add("libx264");
        a.Add("-preset"); a.Add("ultrafast");
        a.Add("-crf"); a.Add("23");
        a.Add("-pix_fmt"); a.Add("yuv420p");
        a.Add("-movflags"); a.Add("+faststart");
        // -shortest: finalize & exit when the finite video input (our pipe:0) ends, even if the
        // live dshow webcam/mic inputs are still streaming. Without this "Stop" hangs until the
        // timeout kills ffmpeg mid-flush -> corrupt MP4 and a false "recording failed" error.
        a.Add("-shortest");
        a.Add("-r"); a.Add(fps.ToString());
        a.Add("-y"); a.Add(output);

        // Output 2 (separate-audio mode): the microphone as a lossless .wav, independent of the video.
        if (separate)
        {
            a.Add("-map"); a.Add($"{micIndex}:a:0");
            a.Add("-c:a"); a.Add("pcm_s16le");
            a.Add("-y"); a.Add(audioOutput!);
        }

        // Output 3 (live preview): a crop of the composited PiP region as raw BGRA frames on
        // stdout, at a fixed 320x180. The reader thread drains it continuously so the encoder
        // never blocks on a full pipe; the preview window shows exactly what is composited into
        // the MP4 (same capture, same mirror, same position — no second grab of the camera, no
        // busy-device races). Because it is derived from [vout] (finite), it ends with the
        // recording and never keeps the encoder alive.
        if (livePreview)
        {
            a.Add("-map"); a.Add("[prev]");
            a.Add("-f"); a.Add("rawvideo");
            a.Add("-pix_fmt"); a.Add("bgra");
            a.Add("-");
        }

        _stderr.Clear();
        _error = null;
        _frames = 0;
        OutputPath = output;
        AudioOutputPath = audioOutput ?? "";
        _ffmpeg = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _ffmpeg.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                lock (_stderr)
                {
                    if (_stderr.Length < 12000) _stderr.AppendLine(args.Data);
                }
            }
        };
        _ffmpeg.OutputDataReceived += (_, _) => { };
        _cts = new CancellationTokenSource();
        if (!_ffmpeg.Start()) throw new InvalidOperationException("ffmpeg could not be started.");
        _ffmpeg.BeginErrorReadLine();
        if (livePreview)
        {
            // stdout carries the raw preview frames — read it as a binary stream (line-based
            // reading would destroy the byte framing). The reader drains it continuously so
            // the encoder never blocks on a full pipe.
            PreviewWidth = PreviewSize.Width;
            PreviewHeight = PreviewSize.Height;
            var previewCts = new CancellationTokenSource();
            _previewCts = previewCts;
            int frameSize = PreviewWidth * PreviewHeight * 4;
            _previewReader = Task.Run(() => PreviewReaderLoop(frameSize, previewCts.Token));
        }
        else
        {
            _ffmpeg.BeginOutputReadLine();
        }
        // Keep the encoder (and thus the webcam/mic it holds) tied to OUR lifetime: if the app
        // exits or is hard-killed, the guardian helper (plus the kill-on-exit job object, where
        // the OS allows it) kills ffmpeg, releasing the camera instead of leaving it grabbed.
        ProcessGuard.Protect(_ffmpeg);
        IsRecording = true;
        _elapsed.Restart();
        _loop = Task.Run(() => RecordLoop(region, fps, options.IncludeCursor, _cts.Token));
        string audioDesc = wantAudio ? ("on (" + options.Microphone + (separate ? ", separate" : "") + ")") : "off";
        string camDesc = webcam ? ("on (" + options.WebcamDevice + ")") : "off";
        string resDesc = scaled ? $"{outW}x{outH}" : "native";
        Logger.Info($"Recording started: {region.Width}x{region.Height} @ {fps}fps, res={resDesc}, audio={audioDesc}, webcam={camDesc} -> {output}");
    }

    private void RecordLoop(Rectangle region, int fps, bool includeCursor, CancellationToken token)
    {
        double interval = 1.0 / fps;
        var clock = Stopwatch.StartNew();
        long frame = 0;
        byte[]? buffer = null;
        try
        {
            while (!token.IsCancellationRequested && _ffmpeg is { HasExited: false })
            {
                using var bitmap = CaptureService.CaptureRegion(region, includeLayeredWindows: false);
                if (includeCursor)
                {
                    using var g = Graphics.FromImage(bitmap);
                    CaptureService.DrawCursor(g, region.Location);
                }

                int width = bitmap.Width, height = bitmap.Height;
                int packedStride = width * 4;
                int bytes = packedStride * height;
                if (buffer == null || buffer.Length != bytes) buffer = new byte[bytes];

                var rect = new Rectangle(0, 0, width, height);
                var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    if (data.Stride == packedStride)
                    {
                        Marshal.Copy(data.Scan0, buffer, 0, bytes);
                    }
                    else
                    {
                        var row = new byte[Math.Abs(data.Stride)];
                        int stride = Math.Abs(data.Stride);
                        for (int y = 0; y < height; y++)
                        {
                            Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, stride);
                            Buffer.BlockCopy(row, 0, buffer, y * packedStride, packedStride);
                        }
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                _ffmpeg!.StandardInput.BaseStream.Write(buffer, 0, bytes);
                _ffmpeg.StandardInput.BaseStream.Flush();
                Interlocked.Increment(ref _frames);
                frame++;

                double target = frame * interval;
                double wait = target - clock.Elapsed.TotalSeconds;
                if (wait > 0)
                {
                    if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(wait))) break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            if (!token.IsCancellationRequested)
            {
                // If the encoder is already gone, the pipe error is a SYMPTOM, not the cause —
                // say so plainly (StopAsync appends the real ffmpeg error line, if there is one).
                bool engineGone = _ffmpeg is { HasExited: true };
                _error = engineGone
                    ? "The recording engine exited before the recording finished."
                    : "The recording stream stopped unexpectedly: " + ex.Message;
                Logger.Error("Recording frame loop failed", ex);
            }
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            Logger.Error("Recording frame loop failed", ex);
        }
        finally
        {
            try { _ffmpeg?.StandardInput.Close(); } catch { }
        }
    }

    public async Task<RecordingStoppedEventArgs> StopAsync()
    {
        if (!IsRecording)
            return new RecordingStoppedEventArgs { Success = false, Error = "No recording is in progress.", OutputPath = OutputPath };

        IsRecording = false;
        _cts?.Cancel();
        var loop = _loop;
        if (loop != null)
        {
            try { await loop.WaitAsync(TimeSpan.FromSeconds(8)); }
            catch { }
        }
        try { _ffmpeg?.StandardInput.Close(); } catch { }
        if (_ffmpeg != null)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await _ffmpeg.WaitForExitAsync(timeout.Token);
            }
            catch
            {
                // Plain Kill (not tree): the encoder is already in our kill-on-exit job object,
                // and .NET's tree-kill re-assignment would fail on a process in a job.
                try { if (!_ffmpeg.HasExited) _ffmpeg.Kill(); } catch { }
                _error ??= "Recording encoder did not finish in time.";
            }
        }
        _elapsed.Stop();
        string stderr;
        lock (_stderr) stderr = _stderr.ToString();
        int exitCode = -1;
        try { exitCode = _ffmpeg?.ExitCode ?? -1; } catch { }
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            Logger.Warn("ffmpeg: " + stderr.Trim());
            // Surface the real ffmpeg error (e.g. "device not found") instead of the generic
            // pipe message the write loop saw.
            string useful = ExtractUsefulError(stderr);
            if (!string.IsNullOrWhiteSpace(useful))
            {
                if (string.IsNullOrWhiteSpace(_error)) _error = useful;
                else if (!_error.Contains(useful)) _error += " — " + useful;
            }
        }
        bool exists = File.Exists(OutputPath) && new FileInfo(OutputPath).Length > 1024;
        bool success = exists && exitCode == 0 && _error == null;
        if (!exists && _error == null) _error = "The encoder did not produce a video file.";
        var result = new RecordingStoppedEventArgs
        {
            OutputPath = OutputPath,
            AudioOutputPath = AudioOutputPath,
            Success = success,
            Error = success ? null : _error,
            Duration = _elapsed.Elapsed,
            Frames = _frames
        };
        Logger.Info($"Recording stopped: success={success}, frames={_frames}, duration={_elapsed.Elapsed}, file={OutputPath}");
        try { _ffmpeg?.Dispose(); } catch { }
        _ffmpeg = null;
        ProcessGuard.ReleaseGuardian();
        _loop = null;
        _cts?.Dispose();
        _cts = null;
        try { _previewCts?.Cancel(); } catch { }
        try
        {
            if (_previewReader != null)
            {
                try { _previewReader.Wait(500); } catch { }
            }
        }
        catch { }
        _previewCts?.Dispose();
        _previewCts = null;
        _previewReader = null;
        lock (_previewGate) _previewFrame = null;
        Stopped?.Invoke(this, result);
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (IsRecording)
            {
                IsRecording = false;
                _cts?.Cancel();
                try { _ffmpeg?.StandardInput.Close(); } catch { }
                if (_ffmpeg != null && !_ffmpeg.WaitForExit(3000)) _ffmpeg.Kill();
            }
        }
        catch { }
        try { _previewCts?.Cancel(); } catch { }
        _ffmpeg?.Dispose();
        _cts?.Dispose();
        _previewCts?.Dispose();
        ProcessGuard.ReleaseGuardian();
    }

    // ===================== live webcam preview (stdout raw frames) =====================

    private void PreviewReaderLoop(int frameSize, CancellationToken ct)
    {
        var stream = _ffmpeg?.StandardOutput.BaseStream;
        if (stream == null) return;
        var frame = new byte[frameSize];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int got = ReadExact(stream, frame, frameSize);
                if (got != frameSize) break;   // preview stream ended (Stop / encoder exit)
                // Keep only the newest frame — the window pulls at ~15 fps and older frames
                // are never worth queueing (backpressure would stall the encoder).
                var copy = new byte[frameSize];
                Buffer.BlockCopy(frame, 0, copy, 0, frameSize);
                lock (_previewGate) _previewFrame = copy;
            }
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested) Logger.Warn("Webcam preview reader stopped", ex);
        }
    }

    private static int ReadExact(Stream stream, byte[] buffer, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            int n = stream.Read(buffer, offset, count - offset);
            if (n <= 0) return offset;
            offset += n;
        }
        return offset;
    }

    // ===================== fail-fast device validation =====================

    private static void ValidateWebcam(string? device)
    {
        var cams = VideoDevices.Enumerate();
        if (cams.Count == 0)
            throw new InvalidOperationException(
                "The webcam is turned on, but no camera was found on this machine.\nConnect a camera — or turn the webcam off on the Recording tab.");
        if (string.IsNullOrWhiteSpace(device))
            throw new InvalidOperationException(
                "The webcam is turned on, but no camera is selected.\nPick one on the Recording tab — or turn the webcam off.");
        if (!cams.Contains(device))
            throw new InvalidOperationException(
                $"The webcam \"{device}\" is no longer available.\nPick a camera on the Recording tab, or turn the webcam off.");
    }

    private static void ValidateMicrophone(string? device)
    {
        var mics = AudioDevices.Enumerate();
        if (mics.Count == 0)
            throw new InvalidOperationException(
                "Microphone audio is turned on, but no microphone was found on this machine.\nConnect a microphone — or turn audio off on the Recording tab.");
        if (string.IsNullOrWhiteSpace(device))
            throw new InvalidOperationException(
                "Microphone audio is turned on, but no microphone is selected.\nPick one on the Recording tab — or turn audio off.");
        if (!mics.Contains(device))
            throw new InvalidOperationException(
                $"The microphone \"{device}\" is no longer available.\nPick one on the Recording tab, or turn audio off.");
    }

    // ===================== error extraction =====================

    /// <summary>Picks the most useful line from ffmpeg's stderr for a user-facing message.</summary>
    private static string ExtractUsefulError(string stderr)
    {
        var lines = stderr.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        if (lines.Count == 0) return "";
        var keywords = new[] { "error", "invalid", "not found", "denied", "failed", "unable", "no such", "busy", "does not exist" };
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            string lower = lines[i]!.ToLowerInvariant();
            if (keywords.Any(k => lower.Contains(k)))
            {
                string line = lines[i]!;
                return line.Length > 300 ? line[..300] + "…" : line;
            }
        }
        string last = lines[^1]!;
        return last.Length > 300 ? last[..300] + "…" : last;
    }
}
