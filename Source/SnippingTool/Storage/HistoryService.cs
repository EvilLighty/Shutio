using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using SnippingTool.Core;
using SnippingTool.Imaging;

namespace SnippingTool.Storage;

public sealed class HistoryService
{
    private readonly object _gate = new();
    private List<SnipEntry> _entries = new();

    public event EventHandler? Changed;

    public IReadOnlyList<SnipEntry> All
    {
        get
        {
            lock (_gate)
                return _entries.OrderByDescending(e => e.CreatedUtc).ToList();
        }
    }

    public IReadOnlyList<SnipEntry> Recent(int count)
    {
        lock (_gate)
            return _entries.OrderByDescending(e => e.CreatedUtc).Take(count).ToList();
    }

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public void Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(AppPaths.HistoryFile)) return;
                var json = File.ReadAllText(AppPaths.HistoryFile);
                var entries = JsonSerializer.Deserialize<List<SnipEntry>>(json, SettingsService.JsonOptions);
                if (entries != null)
                {
                    _entries = entries
                        .Where(e => !string.IsNullOrWhiteSpace(e.FilePath))
                        .GroupBy(e => e.Id)
                        .Select(g => g.First())
                        .OrderByDescending(e => e.CreatedUtc)
                        .ToList();
                    foreach (var entry in _entries)
                    {
                        if (entry.Width <= 0 || entry.Height <= 0)
                            TryReadDimensions(entry);
                    }
                }
                Logger.Info($"History loaded: {_entries.Count} entries.");
            }
            catch (Exception ex)
            {
                Logger.Error("History load failed", ex);
                TryBackup();
                _entries = new List<SnipEntry>();
            }
        }
    }

    public SnipEntry? Add(string filePath, CaptureMode mode, Bitmap bitmap)
    {
        try
        {
            var info = new FileInfo(filePath);
            var entry = new SnipEntry
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Mode = mode.ToString(),
                CreatedUtc = DateTime.UtcNow,
                ModifiedUtc = DateTime.UtcNow,
                Width = bitmap.Width,
                Height = bitmap.Height,
                FileSize = info.Exists ? info.Length : 0
            };
            entry.ThumbnailPath = CreateThumbnail(entry.Id, bitmap);
            lock (_gate)
            {
                _entries.Add(entry);
                Save();
            }
            Changed?.Invoke(this, EventArgs.Empty);
            Logger.Info($"Snip added to history: {entry.FileName} ({entry.Width}x{entry.Height}, {mode}).");
            return entry;
        }
        catch (Exception ex)
        {
            Logger.Error("Could not add snip to history", ex);
            return null;
        }
    }

    public SnipEntry? AddExistingFile(string filePath, string mode)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            using var bitmap = BitmapUtils.Load(filePath);
            return Add(filePath, Enum.TryParse<CaptureMode>(mode, out var m) ? m : CaptureMode.Rectangle, bitmap);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not register existing image", ex);
            return null;
        }
    }

    public void Update(SnipEntry entry, Bitmap bitmap, bool refreshThumbnail = true)
    {
        try
        {
            var info = new FileInfo(entry.FilePath);
            entry.FileName = Path.GetFileName(entry.FilePath);
            entry.Width = bitmap.Width;
            entry.Height = bitmap.Height;
            entry.FileSize = info.Exists ? info.Length : 0;
            entry.ModifiedUtc = DateTime.UtcNow;
            if (refreshThumbnail)
            {
                var newThumb = CreateThumbnail(entry.Id, bitmap);
                if (!string.IsNullOrEmpty(newThumb)) entry.ThumbnailPath = newThumb;
            }
            lock (_gate) Save();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not update history entry", ex);
        }
    }

    public void Remove(SnipEntry entry, bool deleteFiles)
    {
        try
        {
            lock (_gate)
            {
                _entries.RemoveAll(e => e.Id == entry.Id);
                Save();
            }
            if (deleteFiles)
            {
                try { if (File.Exists(entry.FilePath)) File.Delete(entry.FilePath); }
                catch (Exception ex) { Logger.Warn("Could not delete snip file", ex); }
                try { if (File.Exists(entry.ThumbnailPath)) File.Delete(entry.ThumbnailPath); }
                catch { }
            }
            Changed?.Invoke(this, EventArgs.Empty);
            Logger.Info($"Snip removed from history: {entry.FileName}");
        }
        catch (Exception ex)
        {
            Logger.Error("Could not remove history entry", ex);
        }
    }

    public string CreateThumbnail(string id, Bitmap bitmap)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Thumbnails);
            var path = Path.Combine(AppPaths.Thumbnails, id + ".png");
            using var thumb = BitmapUtils.CreateThumbnail(bitmap, 520, 340);
            BitmapUtils.Save(thumb, path);
            return path;
        }
        catch (Exception ex)
        {
            Logger.Warn("Thumbnail creation failed", ex);
            return "";
        }
    }

    public void RefreshEntryMetadata(SnipEntry entry)
    {
        try
        {
            if (!File.Exists(entry.FilePath)) return;
            using var bitmap = BitmapUtils.Load(entry.FilePath);
            entry.Width = bitmap.Width;
            entry.Height = bitmap.Height;
            entry.FileSize = new FileInfo(entry.FilePath).Length;
            if (string.IsNullOrEmpty(entry.ThumbnailPath) || !File.Exists(entry.ThumbnailPath))
                entry.ThumbnailPath = CreateThumbnail(entry.Id, bitmap);
            lock (_gate) Save();
        }
        catch { }
    }

    private void TryReadDimensions(SnipEntry entry)
    {
        try
        {
            if (!File.Exists(entry.FilePath)) return;
            using var image = Image.FromFile(entry.FilePath);
            entry.Width = image.Width;
            entry.Height = image.Height;
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Data);
            var temp = AppPaths.HistoryFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_entries, SettingsService.JsonOptions));
            File.Move(temp, AppPaths.HistoryFile, true);
        }
        catch (Exception ex)
        {
            Logger.Error("History save failed", ex);
        }
    }

    private void TryBackup()
    {
        try
        {
            if (File.Exists(AppPaths.HistoryFile))
                File.Copy(AppPaths.HistoryFile, AppPaths.HistoryFile + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}", true);
        }
        catch { }
    }
}
