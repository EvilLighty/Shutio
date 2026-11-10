using System.Globalization;
using SnippingTool.Core;

namespace SnippingTool.Editing;

/// <summary>
/// Renders a <see cref="TimelineProject"/> to an MP4 with the bundled ffmpeg: the video
/// tracks are composited (bottom track fills the frame, later tracks as picture-in-picture
/// overlays) and the audio tracks are mixed (each clip delayed to its timeline position).
/// </summary>
public static class TimelineExport
{
    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static (int w, int h, int x, int y) PlacementGeometry(VideoPlacement pl, int OW, int OH)
    {
        if (pl == VideoPlacement.Full) return (OW, OH, 0, 0);
        int w = OW / 4, h = OH / 4;
        if (w % 2 != 0) w -= 1;
        if (h % 2 != 0) h -= 1;
        int m = 8;
        switch (pl)
        {
            case VideoPlacement.TopRight: return (w, h, OW - w - m, m);
            case VideoPlacement.BottomRight: return (w, h, OW - w - m, OH - h - m);
            case VideoPlacement.TopLeft: return (w, h, m, m);
            case VideoPlacement.BottomLeft: return (w, h, m, OH - h - m);
            default: return (OW, OH, 0, 0);
        }
    }

    public static async Task<(bool Ok, string Error, string OutPath)> ExportAsync(
        TimelineProject project, string outPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!FfmpegRunner.Available)
            return (false, "The video engine (ffmpeg) is not installed in the Tools folder.", outPath);

        progress?.Report("Gathering sources…");
        // Collect the clips that will actually be rendered.
        var videoClips = new List<(TimelineTrack Track, TimelineClip Clip)>();
        foreach (var t in project.Tracks)
            if (t.Kind == TrackKind.Video && !t.Hidden)
                foreach (var c in t.Clips.OrderBy(c => c.TimelineStart))
                    videoClips.Add((t, c));
        var audioClips = new List<(TimelineTrack Track, TimelineClip Clip)>();
        foreach (var t in project.Tracks)
            if (t.Kind == TrackKind.Audio && !t.Muted)
                foreach (var c in t.Clips.OrderBy(c => c.TimelineStart))
                    audioClips.Add((t, c));

        if (videoClips.Count == 0 && audioClips.Count == 0)
            return (false, "Nothing to export — add a clip to a track first.", outPath);

        // Unique sources (stable order: video then audio).
        var sourceIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<string>();
        void AddSource(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            if (!sourceIndex.ContainsKey(s)) { sourceIndex[s] = sources.Count; sources.Add(s); }
        }
        foreach (var (_, c) in videoClips) AddSource(c.SourcePath);
        foreach (var (_, c) in audioClips) AddSource(c.SourcePath);

        // Probe every unique source once (parallel) to know which have video / audio.
        progress?.Report("Probing media…");
        var probeMap = new Dictionary<string, ProbeInfo>(StringComparer.OrdinalIgnoreCase);
        var probeTasks = sources.Select(async s =>
        {
            ProbeInfo p;
            try { p = await FfmpegRunner.ProbeAsync(s); }
            catch { p = new ProbeInfo(s, 0, 1920, 1080, false, false, 30); }
            lock (probeMap) probeMap[s] = p;
        });
        await Task.WhenAll(probeTasks);

        // Keep only clips whose source actually has the needed stream.
        videoClips = videoClips.Where(x => probeMap.TryGetValue(x.Clip.SourcePath, out var pv) && pv.HasVideo).ToList();
        audioClips = audioClips.Where(x => probeMap.TryGetValue(x.Clip.SourcePath, out var pa) && pa.HasAudio).ToList();

        if (videoClips.Count == 0 && audioClips.Count == 0)
            return (false, "The selected clips have no usable video or audio stream.", outPath);

        // Output size / fps from the first video clip's source (fall back to 1080p @ 30).
        int OW = 1920, OH = 1080, fps = 30;
        if (videoClips.Count > 0)
        {
            var pv = probeMap[videoClips[0].Clip.SourcePath];
            OW = pv.Width; OH = pv.Height; fps = Math.Max(15, (int)Math.Round(pv.Fps));
        }
        if (OW % 2 != 0) OW -= 1;
        if (OH % 2 != 0) OH -= 1;
        if (OW < 16 || OH < 16) { OW = 1920; OH = 1080; }

        double dur = project.Duration;
        string DUR = N(dur);

        // ---- inputs ----
        var args = new List<string>();
        foreach (var s in sources) { args.Add("-i"); args.Add(s); }
        int canvasIdx = sources.Count;
        args.Add("-f"); args.Add("lavfi"); args.Add("-i");
        args.Add($"color=c=black:s={OW}x{OH}:d={DUR}:r={fps}");

        // ---- filtergraph ----
        var filter = new List<string>();
        filter.Add($"[{canvasIdx}:v]null[cv]");
        var acc = "cv";
        int vi = 0;
        foreach (var (track, clip) in videoClips)
        {
            string label = "v" + vi;
            var (w, h, x, y) = PlacementGeometry(track.Placement, OW, OH);
            filter.Add($"[{sourceIndex[clip.SourcePath]}:v]trim=start={N(clip.SourceIn)}:end={N(clip.SourceIn + clip.Duration)},setpts=PTS-STARTPTS,scale={w}:{h}[{label}]");
            string next = "c" + vi;
            filter.Add($"[{acc}][{label}]overlay={x}:{y}:enable='between(t,{N(clip.TimelineStart)},{N(clip.TimelineStart + clip.Duration)})'[{next}]");
            acc = next;
            vi++;
        }
        string vout = acc;

        var audioLabels = new List<string>();
        int ai = 0;
        foreach (var (_, clip) in audioClips)
        {
            string label = "a" + ai++;
            int ms = (int)Math.Round(clip.TimelineStart * 1000);
            var parts = new List<string>
            {
                $"[{sourceIndex[clip.SourcePath]}:a]atrim=start={N(clip.SourceIn)}:end={N(clip.SourceIn + clip.Duration)},asetpts=PTS-STARTPTS"
            };
            if (Math.Abs(clip.Volume - 1.0) > 0.001) parts.Add($"volume={N(Math.Clamp(clip.Volume, 0, 2))}");
            parts.Add($"adelay={ms}|{ms}");
            parts.Add($"aformat=sample_rates=44100:channel_layouts=stereo");
            filter.Add(string.Join(",", parts) + "[" + label + "]");
            audioLabels.Add(label);
        }
        string? aout = null;
        if (audioLabels.Count == 1) aout = audioLabels[0];
        else if (audioLabels.Count > 1)
        {
            aout = "amixed";
            filter.Add(string.Concat(audioLabels.Select(l => "[" + l + "]")) +
                       $"amix=inputs={audioLabels.Count}:duration=longest:normalize=0[{aout}]");
        }

        args.Add("-filter_complex");
        args.Add(string.Join(";", filter));
        args.Add("-map"); args.Add("[" + vout + "]");
        if (aout != null) { args.Add("-map"); args.Add("[" + aout + "]"); args.Add("-c:a"); args.Add("aac"); args.Add("-b:a"); args.Add("160k"); }
        else args.Add("-an");
        args.Add("-c:v"); args.Add("libx264"); args.Add("-preset"); args.Add("ultrafast"); args.Add("-crf"); args.Add("23");
        args.Add("-pix_fmt"); args.Add("yuv420p"); args.Add("-movflags"); args.Add("+faststart"); args.Add("-r"); args.Add(fps.ToString());
        args.Add("-t"); args.Add(DUR);
        args.Add("-y"); args.Add(outPath);

        progress?.Report("Rendering MP4…");
        Logger.Info($"Shutio Cut export: {videoClips.Count} video clip(s), {audioClips.Count} audio clip(s), {OW}x{OH}@{fps}, {dur:0.##}s -> {outPath}");
        var (code, err) = await FfmpegRunner.RunAsync(args, TimeSpan.FromMinutes(20), ct);
        bool ok = code == 0 && File.Exists(outPath) && new FileInfo(outPath).Length > 1024;
        if (!ok)
        {
            var tail = string.IsNullOrWhiteSpace(err) ? "unknown error" : err.Trim().Split('\n').Last();
            Logger.Error("Shutio Cut export failed: " + err.Trim());
            return (false, "Export failed. " + tail.Trim(), outPath);
        }
        return (true, "", outPath);
    }
}
