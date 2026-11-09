using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace MakeIco;

/// <summary>
/// Builds a classic multi-resolution .ico from a square source PNG.
/// Sizes 16..128 are written as DIB (BI_RGB, 32bpp) entries (best small-size
/// compatibility), and 256 is written as a PNG entry (the modern format).
/// The source already has an opaque black background, so the icon is opaque.
/// Usage: MakeIco &lt;source.png&gt; &lt;output.ico&gt;
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: MakeIco <source.png> <output.ico>");
            return 1;
        }
        string src = args[0], outIco = args[1];
        if (!File.Exists(src)) { Console.Error.WriteLine("Source not found: " + src); return 1; }

        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        byte[][] blobs = new byte[sizes.Length][];
        using (var baseImg = new Bitmap(src))
        {
            for (int i = 0; i < sizes.Length; i++)
            {
                using var scaled = Scale(baseImg, sizes[i]);
                blobs[i] = sizes[i] >= 256 ? ToPng(scaled) : ToDib(scaled);
            }
        }
        WriteIco(outIco, sizes, blobs);
        Console.WriteLine("Wrote " + outIco + " (" + new FileInfo(outIco).Length + " bytes)");
        return 0;
    }

    private static Bitmap Scale(Image img, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.DrawImage(img, new Rectangle(0, 0, size, size));
        return bmp;
    }

    private static byte[] ToPng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] ToDib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // BITMAPINFOHEADER
        bw.Write(40);                 // biSize
        bw.Write(w);                  // biWidth
        bw.Write(h * 2);              // biHeight (image + AND mask)
        bw.Write((short)1);           // biPlanes (2 bytes)
        bw.Write((short)32);          // biBitCount (2 bytes)
        bw.Write(0);                  // biCompression (BI_RGB)
        bw.Write(w * h * 4);          // biSizeImage
        bw.Write(0);                  // biXPelsPerMeter
        bw.Write(0);                  // biYPelsPerMeter
        bw.Write(0);                  // biClrUsed
        bw.Write(0);                  // biClrImportant

        // Pixel data, bottom-up, BGRA, opaque (alpha = 255)
        for (int y = h - 1; y >= 0; y--)
            for (int x = 0; x < w; x++)
            {
                Color c = bmp.GetPixel(x, y);
                bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write((byte)255);
            }

        // AND mask, all zero (opaque), each row padded to a 32-bit boundary
        int maskRowBytes = ((w + 31) / 32) * 4;
        var maskRow = new byte[maskRowBytes];
        for (int y = 0; y < h; y++) bw.Write(maskRow, 0, maskRow.Length);

        return ms.ToArray();
    }

    private static void WriteIco(string path, int[] sizes, byte[][] blobs)
    {
        int count = sizes.Length;
        int offset = 6 + 16 * count;   // header + directory
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        bw.Write((short)0);           // reserved
        bw.Write((short)1);           // type = icon
        bw.Write((short)count);

        for (int i = 0; i < count; i++)
        {
            int s = sizes[i];
            bw.Write((byte)(s >= 256 ? 0 : s));   // width
            bw.Write((byte)(s >= 256 ? 0 : s));   // height
            bw.Write((byte)0);                    // colorCount
            bw.Write((byte)0);                    // reserved
            bw.Write((short)1);                   // planes
            bw.Write((short)32);                  // bitCount
            bw.Write(blobs[i].Length);            // bytesInRes
            bw.Write(offset);                     // imageOffset
            offset += blobs[i].Length;
        }

        for (int i = 0; i < count; i++) bw.Write(blobs[i]);
    }
}
