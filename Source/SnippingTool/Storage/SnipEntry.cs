using System.Text.Json.Serialization;

namespace SnippingTool.Storage;

public sealed class SnipEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FilePath { get; set; } = "";
    public string ThumbnailPath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Mode { get; set; } = "Rectangle";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSize { get; set; }

    [JsonIgnore]
    public DateTime CreatedLocal => CreatedUtc.ToLocalTime();

    [JsonIgnore]
    public string DisplayTime
    {
        get
        {
            var local = CreatedLocal;
            var today = DateTime.Today;
            if (local.Date == today) return local.ToString("h:mm tt");
            if (local.Date == today.AddDays(-1)) return "Yesterday " + local.ToString("h:mm tt");
            if (local.Year == today.Year) return local.ToString("MMM d, h:mm tt");
            return local.ToString("MMM d, yyyy");
        }
    }

    [JsonIgnore]
    public string Dimensions => $"{Width} × {Height}";

    [JsonIgnore]
    public string ModeLabel => Mode switch
    {
        "Rectangle" => "Rectangular",
        "Freeform" => "Freeform",
        "Window" => "Window",
        "Fullscreen" => "Full desktop",
        "Monitor" => "Display",
        _ => Mode
    };
}
