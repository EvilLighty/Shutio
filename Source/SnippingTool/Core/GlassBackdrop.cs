using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SnippingTool.Core;

/// <summary>
/// Builds a frosted-glass ("acrylic") background for a window: it samples the desktop behind the
/// window, blurs it, and dims it with the app's surface tint. This is the classic acrylic look
/// reproduced fully offline with plain GDI (a downscale/upscale blur), so it works on Windows 10
/// and 11 with no external dependency. The window is hidden for the few milliseconds it takes to
/// sample, so we capture what is BEHIND it rather than our own pixels.
/// </summary>
public static class GlassBackdrop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    /// <summary>The window's rectangle on the virtual screen (device pixels), or <see cref="Rectangle.Empty"/>.</summary>
    public static Rectangle GetWindowScreenRect(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return Rectangle.Empty;
        if (!GetWindowRect(hwnd, out var r)) return Rectangle.Empty;
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    /// <summary>
    /// Produces a frosted-glass bitmap for the given screen rectangle, ready to be stretched to fill a window.
    /// </summary>
    /// <param name="bounds">The window rectangle in device pixels (virtual-screen coordinates).</param>
    /// <param name="surface">The tint color laid over the blurred desktop.</param>
    /// <param name="transparency">0 = fully opaque surface; higher = more of the blurred desktop shows through.</param>
    /// <param name="blur">0 = off; 1..4 = increasing frosted blur.</param>
    public static Bitmap Build(Rectangle bounds, Color surface, double transparency, int blur)
    {
        if (bounds.Width < 2 || bounds.Height < 2) bounds = new Rectangle(0, 0, 2, 2);

        Bitmap working;
        using (var shot = Capture(bounds))
        {
            working = blur > 0 ? Blur(shot, Math.Clamp(blur, 1, 4)) : (Bitmap)shot.Clone();
        }

        // Dim with the surface tint so the window reads as a frosted surface rather than a clear window.
        byte alpha = (byte)Math.Round(Math.Clamp(1.0 - transparency, 0.0, 1.0) * 255);
        using (var g = Graphics.FromImage(working))
        using (var tint = new SolidBrush(Color.FromArgb(alpha, surface)))
        {
            g.FillRectangle(tint, 0, 0, working.Width, working.Height);
        }
        return working;
    }

    private static Bitmap Capture(Rectangle bounds)
    {
        var shot = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(shot);
        g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        return shot;
    }

    /// <summary>
    /// Cheap frosted blur: downscale to a fraction (strongly averaging neighboring pixels) and
    /// upscale back with bilinear smoothing. Smaller fraction = blurrier.
    /// </summary>
    private static Bitmap Blur(Bitmap src, int strength)
    {
        int factor = Math.Clamp(2 + strength, 3, 6); // strength 1 -> /3 ... 4 -> /6
        int w = Math.Max(8, src.Width / factor);
        int h = Math.Max(8, src.Height / factor);

        var small = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            Setup(g);
            g.DrawImage(src, new Rectangle(0, 0, w, h), new Rectangle(0, 0, src.Width, src.Height), GraphicsUnit.Pixel);
        }

        var outp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(outp))
        {
            Setup(g);
            g.DrawImage(small, new Rectangle(0, 0, outp.Width, outp.Height), new Rectangle(0, 0, w, h), GraphicsUnit.Pixel);
        }
        small.Dispose();
        return outp;
    }

    private static void Setup(Graphics g)
    {
        g.InterpolationMode = InterpolationMode.Bilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.SmoothingMode = SmoothingMode.HighQuality;
    }
}
