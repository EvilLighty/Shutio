using System.Diagnostics;
using SnippingTool.Core;

namespace SnippingTool.Recording;

/// <summary>
/// Enumerates available microphones by asking ffmpeg itself to list its DirectShow
/// audio capture devices. Asking the same binary we encode with guarantees the names we
/// hand back to it are exactly the ones it will accept (no name-mismatch surprises).
/// </summary>
public static class AudioDevices
{
    /// <summary>
    /// Returns the microphone names as ffmpeg reports them, in order. Empty if there is
    /// no ffmpeg or no microphone. Safe to call repeatedly.
    /// </summary>
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
                // Drop a leading log prefix such as "[in#0 @ 000002a90bf39cc0]".
                string stripped = System.Text.RegularExpressions.Regex.Replace(line, @"^\s*\[[^\]]*\]\s*", "").Trim();
                // A device line looks like:  "Microphone (…)" (audio)
                var match = System.Text.RegularExpressions.Regex.Match(stripped, "^\"(.+)\"\\s*\\(audio\\)\\s*$");
                if (match.Success)
                    result.Add(match.Groups[1].Value);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not enumerate microphones", ex);
        }
        return result;
    }
}
