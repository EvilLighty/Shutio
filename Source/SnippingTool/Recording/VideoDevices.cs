using System.Diagnostics;
using SnippingTool.Core;

namespace SnippingTool.Recording;

/// <summary>
/// Enumerates webcam (DirectShow video) devices by asking ffmpeg to list them. Using the same
/// binary we encode with guarantees the device names we hand back are exactly the ones ffmpeg
/// will accept (no name-mismatch surprises).
/// </summary>
public static class VideoDevices
{
    public static List<string> Enumerate()
    {
        var result = new List<string>();
        try
        {
            string? ffmpeg = RecordingController.FfmpegPath;
            if (ffmpeg == null) return result;

            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("dshow");
            psi.ArgumentList.Add("-list_devices"); psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("dshow");

            using var proc = Process.Start(psi);
            if (proc == null) return result;

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            if (!proc.WaitForExit(8000)) { try { proc.Kill(); } catch { } }

            foreach (var raw in (stdout + "\n" + stderr).Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                string stripped = System.Text.RegularExpressions.Regex.Replace(line, @"^\s*\[[^\]]*\]\s*", "").Trim();
                var match = System.Text.RegularExpressions.Regex.Match(stripped, "^\"(.+)\"\\s*\\(video\\)\\s*$");
                if (match.Success)
                    result.Add(match.Groups[1].Value);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not enumerate webcams", ex);
        }
        return result;
    }
}
