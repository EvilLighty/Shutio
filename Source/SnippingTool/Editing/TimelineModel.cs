namespace SnippingTool.Editing;

public enum TrackKind { Video, Audio }

/// <summary>Where a video track sits in the frame. Track 0 fills the frame; later tracks are PiP in corners.</summary>
public enum VideoPlacement { Full, TopRight, BottomRight, TopLeft, BottomLeft }

public sealed class TimelineClip
{
    public string SourcePath { get; set; } = "";
    public double SourceIn { get; set; }          // seconds into the source file
    public double Duration { get; set; }          // seconds on the timeline
    public double TimelineStart { get; set; }     // seconds from the start of the timeline
    public double Volume { get; set; } = 1.0;     // 0..1 (audio tracks)
    public double End => TimelineStart + Duration;
}

public sealed class TimelineTrack
{
    public int Id { get; }
    public TrackKind Kind { get; }
    public string Label { get; set; } = "";
    public bool Muted { get; set; }
    public bool Hidden { get; set; }
    public VideoPlacement Placement { get; set; } = VideoPlacement.Full;
    public List<TimelineClip> Clips { get; } = new();
    public double End => Clips.Count == 0 ? 0 : Clips.Max(c => c.End);

    internal TimelineTrack(int id, TrackKind kind, string label)
    {
        Id = id;
        Kind = kind;
        Label = label;
    }
}

public static class TimelineConstants
{
    public const int MaxTracks = 6;   // the timeline supports up to 6 tracks (mixed video + audio)
    public const int MinTracks = 1;   // always keep at least one track
    // Default placement by video-track order: the first fills the frame, the rest are PiP.
    public static readonly VideoPlacement[] OverlayOrder =
    {
        VideoPlacement.Full, VideoPlacement.TopRight, VideoPlacement.BottomRight,
        VideoPlacement.TopLeft, VideoPlacement.BottomLeft
    };
}

public sealed class TimelineProject
{
    private int _nextId = 1;

    public string MainSource { get; set; } = "";
    public List<TimelineTrack> Tracks { get; } = new();
    public TimelineClip? Selected { get; set; }

    public int VideoTrackCount => Tracks.Count(t => t.Kind == TrackKind.Video);
    public int AudioTrackCount => Tracks.Count(t => t.Kind == TrackKind.Audio);

    public double Duration
    {
        get
        {
            double max = 0;
            foreach (var t in Tracks) max = Math.Max(max, t.End);
            return Math.Max(0.5, Math.Ceiling(max * 10) / 10);
        }
    }

    public TimelineProject()
    {
        AddTrack(TrackKind.Video);
        AddTrack(TrackKind.Audio);
    }

    public bool CanAdd(TrackKind kind) => Tracks.Count < TimelineConstants.MaxTracks;
    public bool CanRemove() => Tracks.Count > TimelineConstants.MinTracks;

    public TimelineTrack? AddTrack(TrackKind kind)
    {
        if (Tracks.Count >= TimelineConstants.MaxTracks) return Tracks.FirstOrDefault();
        var track = new TimelineTrack(_nextId++, kind, kind == TrackKind.Video ? "V" : "A");
        Tracks.Add(track);
        Relabel();
        return track;
    }

    public void RemoveTrack(int id)
    {
        if (Tracks.Count <= TimelineConstants.MinTracks) return;
        var t = Tracks.FirstOrDefault(x => x.Id == id);
        if (t == null) return;
        if (Selected != null && t.Clips.Contains(Selected)) Selected = null;
        Tracks.Remove(t);
        Relabel();
    }

    private void Relabel()
    {
        int v = 0, a = 0;
        foreach (var tr in Tracks)
        {
            if (tr.Kind == TrackKind.Video)
            {
                tr.Label = "V" + (++v);
                tr.Placement = TimelineConstants.OverlayOrder[Math.Min(v - 1, TimelineConstants.OverlayOrder.Length - 1)];
            }
            else
            {
                tr.Label = "A" + (++a);
            }
        }
    }

    public TimelineTrack? Find(int id) => Tracks.FirstOrDefault(t => t.Id == id);
    public TimelineTrack? TrackOf(TimelineClip clip) => Tracks.FirstOrDefault(t => t.Clips.Contains(clip));
    public IEnumerable<TimelineClip> AllClips()
    {
        foreach (var t in Tracks)
            foreach (var c in t.Clips)
                yield return c;
    }
}
