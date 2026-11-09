using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnippingTool.Core;
using Color = System.Drawing.Color;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Rectangle = System.Drawing.Rectangle;

namespace SnippingTool.Imaging;

public static class BitmapUtils
{
    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            int bytes = stride * bitmap.Height;
            var buffer = new byte[bytes];
            Marshal.Copy(data.Scan0, buffer, 0, bytes);
            var source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96,
                PixelFormats.Bgra32, null, buffer, stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public static BitmapSource LoadBitmapSource(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public static Bitmap Clone(Bitmap source)
    {
        var clone = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(clone);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImageUnscaled(source, 0, 0);
        return clone;
    }

    public static Bitmap Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var loaded = Image.FromStream(stream, true, true);
        var result = new Bitmap(loaded.Width, loaded.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImageUnscaled(loaded, 0, 0);
        return result;
    }

    public static Bitmap Resize(Bitmap source, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return result;
    }

    public static Bitmap Rotate(Bitmap source, bool clockwise)
    {
        var clone = Clone(source);
        clone.RotateFlip(clockwise ? RotateFlipType.Rotate90FlipNone : RotateFlipType.Rotate270FlipNone);
        return clone;
    }

    public static Bitmap Crop(Bitmap source, Rectangle region)
    {
        region = Rectangle.Intersect(region, new Rectangle(0, 0, source.Width, source.Height));
        if (region.Width < 1 || region.Height < 1)
            throw new ArgumentException("Crop region is empty.");
        var result = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(source, new Rectangle(0, 0, region.Width, region.Height), region, GraphicsUnit.Pixel);
        return result;
    }

    public static Bitmap FlattenOnBackground(Bitmap source, Color background)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.Clear(background);
        g.CompositingMode = CompositingMode.SourceOver;
        g.DrawImageUnscaled(source, 0, 0);
        return result;
    }

    public static Bitmap CreateThumbnail(Bitmap source, int maxWidth, int maxHeight)
    {
        double scale = Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height);
        scale = Math.Min(scale, 1.0);
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        return Resize(source, width, height);
    }

    public static void Save(Bitmap bitmap, string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppPaths.Data);
        var temp = path + ".saving";
        try
        {
            if (extension is ".jpg" or ".jpeg" or ".bmp")
            {
                using var flattened = FlattenOnBackground(bitmap, Color.White);
                if (extension is ".jpg" or ".jpeg")
                    SaveJpeg(flattened, temp);
                else
                    flattened.Save(temp, ImageFormat.Bmp);
            }
            else
            {
                SavePng(bitmap, temp);
            }
            File.Move(temp, path, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static void SavePng(Bitmap bitmap, string path)
    {
        var encoder = GetEncoder(ImageFormat.Png);
        using var parameters = new EncoderParameters(1);
        using var quality = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 100L);
        parameters.Param[0] = quality;
        bitmap.Save(path, encoder, parameters);
    }

    private static void SaveJpeg(Bitmap bitmap, string path)
    {
        var encoder = GetEncoder(ImageFormat.Jpeg);
        using var parameters = new EncoderParameters(1);
        using var quality = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
        parameters.Param[0] = quality;
        bitmap.Save(path, encoder, parameters);
    }

    private static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        return ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == format.Guid)
               ?? throw new InvalidOperationException($"No image encoder is available for {format}.");
    }

    public static bool HasMeaningfulAlpha(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                byte* ptr = (byte*)data.Scan0;
                int stride = data.Stride;
                for (int y = 0; y < bitmap.Height; y += 4)
                {
                    byte* row = ptr + y * stride;
                    for (int x = 0; x < bitmap.Width; x += 4)
                        if (row[x * 4 + 3] < 250) return true;
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return false;
    }
}
