using System.Diagnostics;
using System.Drawing;
using SnippingTool.Core;

namespace SnippingTool.Editing;

/// <summary>
/// Decodes a single video frame at a given time from a source file (ffmpeg seek + one frame
/// to a temp PNG), used to drive the Shutio Cut preview without a full decode loop.
/// </summary>
public static class FramePreview
{
    private static int _n;

    public static async Task<Bitmap?> GetFrameAsync(string source, double time, int width, int height, CancellationToken ct = default)
    {
        if (!FfmpegRunner.Available) return null;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return null;
        int w = Math.Max(16, width), h = Math.Max(16, height);
        // H.264 wants even dimensions.
        if (w % 2 != 0) w -= 1;
        if (h % 2 != 0) h -= 1;
        string tmp = Path.Combine(AppPaths.Tests, $"cutframe_{Environment.ProcessId}_{Interlocked.Increment(ref _n)}.png");
        try
        {
            Directory.CreateDirectory(AppPaths.Tests);
            var psi = new ProcessStartInfo
            {
                FileName = FfmpegRunner.Ffmpeg,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            foreach (var a in new[]
            {
                "-hide_banner", "-loglevel", "error",
                "-ss", time.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                "-i", source,
                "-frames:v", "1",
                "-vf", $"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:color=black",
                "-f", "image2", "-y", tmp
            })
                psi.ArgumentList.Add(a);
            var p = new Process { StartInfo = psi };
            p.ErrorDataReceived += (_, _) => { };
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            if (!p.Start()) return null;
            p.BeginErrorReadLine();
            try { await p.WaitForExitAsync(linked.Token); }
            catch (OperationCanceledException) { try { if (!p.HasExited) p.Kill(true); } catch { } return null; }
            p.Dispose();
            if (File.Exists(tmp) && new FileInfo(tmp).Length > 64)
            {
                using var ms = new MemoryStream(File.ReadAllBytes(tmp));
                return Image.FromStream(ms) as Bitmap ?? null;
            }
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }
}
