using System.Diagnostics;
using System.Text;
using SnippingTool.Core;
using SnippingTool.Recording;

namespace SnippingTool.Editing;

public sealed record ProbeInfo(string Path, double Duration, int Width, int Height, bool HasVideo, bool HasAudio, double Fps);

/// <summary>
/// Thin wrapper around the bundled ffmpeg: run a command with an argument list, capture
/// stderr, and probe a media file for duration / dimensions / streams.
/// </summary>
public static class FfmpegRunner
{
    public static string Ffmpeg => RecordingController.FfmpegPath ?? "";
    public static bool Available => RecordingController.IsAvailable;

    public static async Task<(int Code, string Err)> RunAsync(IList<string> args, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SHUTIO_FFMPEG_LOG")))
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Tests);
                File.WriteAllText(System.IO.Path.Combine(AppPaths.Tests, "ffmpeg_last_args.txt"),
                    string.Join("\n", args.Select(a => "ARG=[" + a + "]")));
            }
            catch { }
        }

        var p = new Process { StartInfo = psi };
        var err = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data) && err.Length < 20000) err.AppendLine(e.Data); };
        p.OutputDataReceived += (_, _) => { };
        if (!p.Start()) return (-1, "ffmpeg could not be started.");
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();

        try
        {
            using var timeoutCts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(10));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            await p.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!p.HasExited) p.Kill(true); } catch { }
            throw;
        }
        int code = -1;
        try { code = p.ExitCode; } catch { }
        return (code, err.ToString());
    }

    public static async Task<ProbeInfo> ProbeAsync(string path, CancellationToken ct = default)
    {
        var (_, err) = await RunAsync(new[] { "-hide_banner", "-i", path }, TimeSpan.FromSeconds(30), ct);
        double dur = 0, fps = 0;
        int w = 0, h = 0;
        bool hasV = false, hasA = false;
        foreach (var raw in err.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Contains("Duration:"))
            {
                var token = line.Substring(line.IndexOf("Duration:", StringComparison.Ordinal) + "Duration:".Length).Trim().Split(',').First().Trim();
                if (token != null && TimeSpan.TryParse(token, out var ts)) dur = ts.TotalSeconds;
            }
            else if (line.Contains("Stream") && line.Contains("Video:"))
            {
                hasV = true;
                var m = System.Text.RegularExpressions.Regex.Match(line, @"(\d{2,5})x(\d{2,5})");
                if (m.Success) { w = int.Parse(m.Groups[1].Value); h = int.Parse(m.Groups[2].Value); }
                var fm = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)\s+fps");
                if (fm.Success) fps = double.Parse(fm.Groups[1].Value);
            }
            else if (line.Contains("Stream") && line.Contains("Audio:"))
            {
                hasA = true;
            }
        }
        if (w < 2) w = 1920;
        if (h < 2) h = 1080;
        if (fps < 1) fps = 30;
        return new ProbeInfo(path, dur, w, h, hasV, hasA, fps);
    }
}
