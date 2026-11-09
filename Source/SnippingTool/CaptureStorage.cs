using System.Drawing;
using System.Text;
using SnippingTool.Core;
using SnippingTool.Imaging;
using SnippingTool.Storage;

namespace SnippingTool;

public static class CaptureStorage
{
    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars()
        .Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).Distinct().ToArray();

    public static SnipEntry? SaveNew(Bitmap bitmap, CaptureMode mode, Settings settings, HistoryService history,
        string extension = ".png")
    {
        try
        {
            string path = GenerateFilePath(settings, mode, extension);
            BitmapUtils.Save(bitmap, path);
            var entry = history.Add(path, mode, bitmap);
            return entry;
        }
        catch (Exception ex)
        {
            Logger.Error("Automatic save failed", ex);
            return null;
        }
    }

    public static SnipEntry RegisterSavedFile(string path, CaptureMode mode, Bitmap bitmap, HistoryService history)
    {
        try
        {
            return history.Add(path, mode, bitmap) ?? new SnipEntry
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                Mode = mode.ToString(),
                Width = bitmap.Width,
                Height = bitmap.Height
            };
        }
        catch (Exception ex)
        {
            Logger.Error("Could not register saved file", ex);
            return new SnipEntry
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                Mode = mode.ToString(),
                Width = bitmap.Width,
                Height = bitmap.Height
            };
        }
    }

    public static string GenerateFilePath(Settings settings, CaptureMode mode, string extension = ".png")
    {
        var directory = settings.SaveDirectory;
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-test");
            File.WriteAllText(probe, "test");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Save directory '{directory}' is not writable; using app data folder", ex);
            directory = AppPaths.Screenshots;
            Directory.CreateDirectory(directory);
        }

        var now = DateTime.Now;
        string pattern = settings.FileNamePattern;
        if (pattern.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            pattern.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            pattern.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            pattern.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
            pattern = Path.GetFileNameWithoutExtension(pattern);

        pattern = pattern
            .Replace("{yyyy}", now.ToString("yyyy"))
            .Replace("{yy}", now.ToString("yy"))
            .Replace("{MM}", now.ToString("MM"))
            .Replace("{dd}", now.ToString("dd"))
            .Replace("{HH}", now.ToString("HH"))
            .Replace("{mm}", now.ToString("mm"))
            .Replace("{ss}", now.ToString("ss"))
            .Replace("{mode}", mode.ToString());

        var builder = new StringBuilder();
        foreach (char c in pattern)
            builder.Append(InvalidNameChars.Contains(c) ? '_' : c);
        string baseName = builder.ToString().Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "Snip_" + now.ToString("yyyy-MM-dd_HH-mm-ss");

        if (!extension.StartsWith('.')) extension = "." + extension;
        string path = Path.Combine(directory, baseName + extension);
        int suffix = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{baseName} ({suffix}){extension}");
            suffix++;
        }
        return path;
    }
}
