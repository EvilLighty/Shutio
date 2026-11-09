using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SnippingTool.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using BitmapPixelFormat = Windows.Graphics.Imaging.BitmapPixelFormat;

namespace SnippingTool.Imaging;

public sealed class OcrLanguageInfo
{
    public string Tag { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public override string ToString() => DisplayName;
}

public sealed class OcrResult
{
    public string Text { get; init; } = "";
    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();
    public string Language { get; init; } = "";
}

public static class OcrService
{
    public static bool IsAvailable
    {
        get
        {
            try { return OcrEngine.AvailableRecognizerLanguages.Count > 0; }
            catch { return false; }
        }
    }

    public static IReadOnlyList<OcrLanguageInfo> GetLanguages()
    {
        var list = new List<OcrLanguageInfo>();
        try
        {
            foreach (var language in OcrEngine.AvailableRecognizerLanguages)
            {
                list.Add(new OcrLanguageInfo
                {
                    Tag = language.LanguageTag,
                    DisplayName = language.DisplayName
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not enumerate OCR languages", ex);
        }
        return list;
    }

    public static async Task<OcrResult> RecognizeAsync(Bitmap bitmap, string preferredLanguage = "")
    {
        OcrEngine? engine = null;
        if (!string.IsNullOrWhiteSpace(preferredLanguage))
        {
            try { engine = OcrEngine.TryCreateFromLanguage(new Language(preferredLanguage)); }
            catch { }
        }
        engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null)
        {
            var languages = GetLanguages();
            if (languages.Count > 0)
            {
                try { engine = OcrEngine.TryCreateFromLanguage(new Language(languages[0].Tag)); }
                catch { }
            }
        }
        if (engine == null)
            throw new InvalidOperationException("No Windows OCR language is installed for this account.");

        int max = (int)OcrEngine.MaxImageDimension;
        using var prepared = PrepareForOcr(bitmap, max);
        var bytes = GetBgraBytes(prepared);
        var writer = new DataWriter();
        writer.WriteBytes(bytes);
        var buffer = writer.DetachBuffer();
        using var softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8,
            prepared.Width, prepared.Height, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(softwareBitmap);
        var lines = result.Lines.Select(l => l.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var text = string.Join(Environment.NewLine, lines);
        if (string.IsNullOrWhiteSpace(text)) text = result.Text ?? "";
        Logger.Info($"OCR completed with {engine.RecognizerLanguage.LanguageTag}: {lines.Count} lines, {text.Length} characters.");
        return new OcrResult
        {
            Text = text.Trim(),
            Lines = lines,
            Language = engine.RecognizerLanguage.DisplayName
        };
    }

    private static Bitmap PrepareForOcr(Bitmap source, int maxDimension)
    {
        Bitmap opaque = BitmapUtils.FlattenOnBackground(source, Color.White);
        int width = opaque.Width, height = opaque.Height;
        if (width <= maxDimension && height <= maxDimension) return opaque;
        double scale = Math.Min((double)maxDimension / width, (double)maxDimension / height);
        var resized = BitmapUtils.Resize(opaque, (int)(width * scale), (int)(height * scale));
        opaque.Dispose();
        return resized;
    }

    private static byte[] GetBgraBytes(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            // SoftwareBitmap expects tightly packed rows.
            int packedStride = bitmap.Width * 4;
            var buffer = new byte[packedStride * bitmap.Height];
            var row = new byte[stride];
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, stride);
                System.Buffer.BlockCopy(row, 0, buffer, y * packedStride, packedStride);
            }
            return buffer;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
