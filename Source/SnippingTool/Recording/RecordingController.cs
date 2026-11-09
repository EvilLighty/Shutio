using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using SnippingTool.Capture;
using SnippingTool.Core;

namespace SnippingTool.Recording;

public sealed class RecordingOptions
{
    public Rectangle Region { get; init; }
    public int Fps { get; init; } = 15;
    public bool IncludeCursor { get; init; } = true;
    public bool IncludeAudio { get; init; }
    public string Microphone { get; init; } = "";
    public string OutputPath { get; init; } = "";
}

public sealed class RecordingStoppedEventArgs : EventArgs
{
    public string OutputPath { get; init; } = "";
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

    public bool IsRecording { get; private set; }
    public string OutputPath { get; private set; } = "";
    public TimeSpan Elapsed => _elapsed.Elapsed;
    public int Frames => _frames;
    public string? LastError => _error;

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

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("rawvideo");
        psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("bgra");
        psi.ArgumentList.Add("-s"); psi.ArgumentList.Add($"{region.Width}x{region.Height}");
        psi.ArgumentList.Add("-r"); psi.ArgumentList.Add(fps.ToString());
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("pipe:0");

        bool useAudio = options.IncludeAudio && !string.IsNullOrWhiteSpace(options.Microphone);
        if (useAudio)
        {
            // Second input: the microphone, captured via DirectShow under its exact device name.
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("dshow");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("audio=" + options.Microphone);
        }

        psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("0:v:0");
        if (useAudio)
        {
            psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("1:a:0");
            psi.ArgumentList.Add("-c:a"); psi.ArgumentList.Add("aac");
            psi.ArgumentList.Add("-b:a"); psi.ArgumentList.Add("160k");
        }
        else
        {
            psi.ArgumentList.Add("-an");
        }

        psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add("libx264");
        psi.ArgumentList.Add("-preset"); psi.ArgumentList.Add("ultrafast");
        psi.ArgumentList.Add("-crf"); psi.ArgumentList.Add("23");
        psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("yuv420p");
        psi.ArgumentList.Add("-movflags"); psi.ArgumentList.Add("+faststart");
        psi.ArgumentList.Add("-r"); psi.ArgumentList.Add(fps.ToString());
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add(output);

        _stderr.Clear();
        _error = null;
        _frames = 0;
        OutputPath = output;
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
        _ffmpeg.BeginOutputReadLine();
        IsRecording = true;
        _elapsed.Restart();
        _loop = Task.Run(() => RecordLoop(region, fps, options.IncludeCursor, _cts.Token));
        string audioDesc = useAudio ? ("on (" + options.Microphone + ")") : "off";
        Logger.Info($"Recording started: {region.Width}x{region.Height} @ {fps}fps, audio={audioDesc} -> {output}");
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
                _error = "The recording stream stopped unexpectedly: " + ex.Message;
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
                try { if (!_ffmpeg.HasExited) _ffmpeg.Kill(true); } catch { }
                _error ??= "Recording encoder did not finish in time.";
            }
        }
        _elapsed.Stop();
        string stderr;
        lock (_stderr) stderr = _stderr.ToString();
        int exitCode = -1;
        try { exitCode = _ffmpeg?.ExitCode ?? -1; } catch { }
        bool exists = File.Exists(OutputPath) && new FileInfo(OutputPath).Length > 1024;
        bool success = exists && exitCode == 0 && _error == null;
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            if (_error == null) _error = stderr.Trim();
            Logger.Warn("ffmpeg: " + stderr.Trim());
        }
        if (!exists && _error == null) _error = "The encoder did not produce a video file.";
        var result = new RecordingStoppedEventArgs
        {
            OutputPath = OutputPath,
            Success = success,
            Error = success ? null : _error,
            Duration = _elapsed.Elapsed,
            Frames = _frames
        };
        Logger.Info($"Recording stopped: success={success}, frames={_frames}, duration={_elapsed.Elapsed}, file={OutputPath}");
        try { _ffmpeg?.Dispose(); } catch { }
        _ffmpeg = null;
        _loop = null;
        _cts?.Dispose();
        _cts = null;
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
                if (_ffmpeg != null && !_ffmpeg.WaitForExit(3000)) _ffmpeg.Kill(true);
            }
        }
        catch { }
        _ffmpeg?.Dispose();
        _cts?.Dispose();
    }
}
