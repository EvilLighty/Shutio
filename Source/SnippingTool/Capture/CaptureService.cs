using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using SnippingTool.Core;
using WinForms = System.Windows.Forms;

namespace SnippingTool.Capture;

public sealed class WindowInfo
{
    public IntPtr Handle { get; init; }
    public string Title { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public uint ProcessId { get; init; }
    public Rectangle Bounds { get; init; }

    public override string ToString() => $"{Title} ({ClassName})";
}

public static class CaptureService
{
    private static readonly string[] ExcludedClasses =
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow", "ApplicationManager_DesktopShellWindow",
        "XamlExplorerHostIslandWindow", "MultitaskingViewFrame", "ForegroundStaging",
        "TaskListThumbnailWnd", "EdgeUiInputTopWndClass"
    };

    public static Rectangle VirtualScreen => WinForms.SystemInformation.VirtualScreen;

    public static Bitmap CaptureVirtualScreen() => CaptureRegion(VirtualScreen);

    public static Bitmap CaptureMonitor(Rectangle monitorBounds) => CaptureRegion(monitorBounds);

    public static Bitmap CaptureRegion(Rectangle region, bool includeLayeredWindows = true)
    {
        if (region.Width <= 0 || region.Height <= 0)
            throw new ArgumentException("Capture region must have a positive size.", nameof(region));

        region = Rectangle.Intersect(region, VirtualScreen);
        if (region.Width <= 0 || region.Height <= 0)
            throw new InvalidOperationException("The capture region is outside the desktop.");

        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            throw new InvalidOperationException("The desktop device context is unavailable.");

        IntPtr memDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr old = IntPtr.Zero;
        try
        {
            memDc = NativeMethods.CreateCompatibleDC(screenDc);
            hBitmap = NativeMethods.CreateCompatibleBitmap(screenDc, region.Width, region.Height);
            if (memDc == IntPtr.Zero || hBitmap == IntPtr.Zero)
                throw new OutOfMemoryException("Could not allocate a capture bitmap.");
            old = NativeMethods.SelectObject(memDc, hBitmap);
            uint rop = NativeMethods.SRCCOPY | (includeLayeredWindows ? NativeMethods.CAPTUREBLT : 0);
            if (!NativeMethods.BitBlt(memDc, 0, 0, region.Width, region.Height,
                    screenDc, region.Left, region.Top, rop))
                throw new InvalidOperationException("BitBlt failed. The desktop may not be capturable in this session.");

            using var temp = Image.FromHbitmap(hBitmap);
            var result = new Bitmap(temp.Width, temp.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(temp, 0, 0);
            }
            return result;
        }
        finally
        {
            if (memDc != IntPtr.Zero && old != IntPtr.Zero) NativeMethods.SelectObject(memDc, old);
            if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
            if (memDc != IntPtr.Zero) NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public static List<WindowInfo> EnumerateWindows()
    {
        var list = new List<WindowInfo>();
        uint ownPid = (uint)Environment.ProcessId;
        var virtualScreen = VirtualScreen;

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            try
            {
                if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd)) return true;
                NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == ownPid) return true;

                string className = NativeMethods.GetWindowClassName(hWnd);
                if (ExcludedClasses.Contains(className, StringComparer.OrdinalIgnoreCase)) return true;

                long exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();
                if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;

                int cloaked = 0;
                if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;

                var rect = GetWindowBounds(hWnd);
                if (rect.Width < 24 || rect.Height < 24) return true;
                var clipped = Rectangle.Intersect(rect, virtualScreen);
                if (clipped.Width < 16 || clipped.Height < 16) return true;

                string title = NativeMethods.GetWindowTitle(hWnd);
                string processName = "";
                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    processName = process.ProcessName;
                }
                catch { }

                // Shell surfaces and untitled helper windows add noise to window snip mode.
                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrEmpty(processName)) return true;

                list.Add(new WindowInfo
                {
                    Handle = hWnd,
                    Title = string.IsNullOrWhiteSpace(title) ? processName : title,
                    ClassName = className,
                    ProcessId = pid,
                    ProcessName = processName,
                    Bounds = clipped
                });
            }
            catch (Exception ex)
            {
                Logger.Debug("Skipping window during enumeration: " + ex.Message);
            }
            return true;
        }, IntPtr.Zero);

        return list;
    }

    public static Rectangle GetWindowBounds(IntPtr hWnd)
    {
        if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out NativeMethods.RECT dwmRect, Marshal.SizeOf<NativeMethods.RECT>()) == 0)
        {
            var rect = dwmRect.ToRectangle();
            if (rect.Width > 0 && rect.Height > 0) return rect;
        }
        if (NativeMethods.GetWindowRect(hWnd, out var winRect))
            return winRect.ToRectangle();
        return Rectangle.Empty;
    }

    public static Bitmap CaptureWindow(IntPtr hWnd, Bitmap frozenDesktop)
    {
        var bounds = GetWindowBounds(hWnd);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("The selected window has no visible bounds.");

        // First try a direct window render. This can include content hidden behind other windows.
        Bitmap? direct = null;
        try
        {
            direct = PrintWindowBitmap(hWnd, bounds.Width, bounds.Height);
            if (direct != null && !IsMostlyBlank(direct))
            {
                Logger.Info($"Window capture used PrintWindow ({bounds.Width}x{bounds.Height}).");
                return direct;
            }
            direct?.Dispose();
        }
        catch (Exception ex)
        {
            direct?.Dispose();
            Logger.Warn("PrintWindow capture failed; falling back to desktop crop", ex);
        }

        var virtualScreen = VirtualScreen;
        var source = Rectangle.Intersect(bounds, virtualScreen);
        var local = new Rectangle(source.Left - virtualScreen.Left, source.Top - virtualScreen.Top, source.Width, source.Height);
        local = Rectangle.Intersect(local, new Rectangle(0, 0, frozenDesktop.Width, frozenDesktop.Height));
        if (local.Width <= 0 || local.Height <= 0)
            throw new InvalidOperationException("The window is outside the captured desktop.");

        var result = new Bitmap(local.Width, local.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.DrawImage(frozenDesktop, new Rectangle(0, 0, local.Width, local.Height), local, GraphicsUnit.Pixel);
        Logger.Info($"Window capture used frozen desktop crop ({local.Width}x{local.Height}).");
        return result;
    }

    private static Bitmap? PrintWindowBitmap(IntPtr hWnd, int width, int height)
    {
        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;
        IntPtr memDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr old = IntPtr.Zero;
        try
        {
            memDc = NativeMethods.CreateCompatibleDC(screenDc);
            hBitmap = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
            if (memDc == IntPtr.Zero || hBitmap == IntPtr.Zero) return null;
            old = NativeMethods.SelectObject(memDc, hBitmap);
            bool ok = NativeMethods.PrintWindow(hWnd, memDc, NativeMethods.PW_RENDERFULLCONTENT);
            if (!ok) return null;
            using var temp = Image.FromHbitmap(hBitmap);
            var result = new Bitmap(temp.Width, temp.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(temp, 0, 0);
            }
            return result;
        }
        finally
        {
            if (memDc != IntPtr.Zero && old != IntPtr.Zero) NativeMethods.SelectObject(memDc, old);
            if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
            if (memDc != IntPtr.Zero) NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public static bool IsMostlyBlank(Bitmap bitmap)
    {
        try
        {
            int step = Math.Max(1, Math.Min(bitmap.Width, bitmap.Height) / 40);
            int samples = 0, dark = 0, transparent = 0;
            var reference = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
            for (int y = step / 2; y < bitmap.Height; y += step)
            {
                for (int x = step / 2; x < bitmap.Width; x += step)
                {
                    var c = bitmap.GetPixel(x, y);
                    samples++;
                    if (c.A < 8) transparent++;
                    if (c.R < 6 && c.G < 6 && c.B < 6) dark++;
                }
            }
            if (samples == 0) return true;
            return transparent > samples * 0.9 || dark > samples * 0.92;
        }
        catch
        {
            return false;
        }
    }

    public static Bitmap CreateFreeformSnip(Bitmap source, IReadOnlyList<Point> points)
    {
        if (points.Count < 3)
            throw new ArgumentException("A freeform selection needs at least three points.", nameof(points));

        using var path = new GraphicsPath(FillMode.Winding);
        path.AddPolygon(points.ToArray());

        using var mask = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var mg = Graphics.FromImage(mask))
        {
            mg.SmoothingMode = SmoothingMode.AntiAlias;
            mg.Clear(Color.Transparent);
            using var white = new SolidBrush(Color.White);
            mg.FillPath(white, path);
        }

        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(source, 0, 0);
        }

        var rect = new Rectangle(0, 0, source.Width, source.Height);
        var resultData = result.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var maskData = mask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int bytes = Math.Abs(resultData.Stride) * source.Height;
            var buffer = new byte[bytes];
            var maskBuffer = new byte[bytes];
            Marshal.Copy(resultData.Scan0, buffer, 0, bytes);
            Marshal.Copy(maskData.Scan0, maskBuffer, 0, bytes);
            int stride = Math.Abs(resultData.Stride);
            for (int y = 0; y < source.Height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < source.Width; x++)
                {
                    int i = row + x * 4;
                    // mask is white inside; its red channel is the antialiased coverage
                    int coverage = maskBuffer[i + 3];
                    if (coverage == 0)
                    {
                        buffer[i] = 0; buffer[i + 1] = 0; buffer[i + 2] = 0; buffer[i + 3] = 0;
                    }
                    else
                    {
                        buffer[i + 3] = (byte)((buffer[i + 3] * coverage + 127) / 255);
                    }
                }
            }
            Marshal.Copy(buffer, 0, resultData.Scan0, bytes);
        }
        finally
        {
            result.UnlockBits(resultData);
            mask.UnlockBits(maskData);
        }
        return result;
    }


    public static WindowInfo? WindowAt(Point screenPoint, IReadOnlyList<WindowInfo> windows)
    {
        foreach (var window in windows)
            if (window.Bounds.Contains(screenPoint)) return window;
        return null;
    }

    public static (Rectangle Bounds, string Name) MonitorAt(Point screenPoint)
    {
        var monitors = NativeMethods.GetMonitors();
        foreach (var monitor in monitors)
        {
            var rect = monitor.Bounds.ToRectangle();
            if (rect.Contains(screenPoint)) return (rect, monitor.Name);
        }
        if (monitors.Count > 0) return (monitors[0].Bounds.ToRectangle(), monitors[0].Name);
        var vs = VirtualScreen;
        return (vs, "Display 1");
    }

    private static bool _cursorErrorLogged;

    public static void DrawCursor(Graphics graphics, Point screenOrigin)
    {
        try
        {
            var ci = new NativeMethods.CURSORINFO { cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>() };
            if (!NativeMethods.GetCursorInfo(ref ci) || (ci.flags & NativeMethods.CURSOR_SHOWING) == 0 || ci.hCursor == IntPtr.Zero)
                return;
            int x = ci.ptScreenPos.X - screenOrigin.X;
            int y = ci.ptScreenPos.Y - screenOrigin.Y;
            if (!NativeMethods.GetIconInfo(ci.hCursor, out var info))
                return;
            try
            {
                int hotX = info.xHotspot, hotY = info.yHotspot;
                IntPtr hdc = graphics.GetHdc();
                try
                {
                    NativeMethods.DrawIconEx(hdc, x - hotX, y - hotY, ci.hCursor, 0, 0, 0, IntPtr.Zero, NativeMethods.DI_NORMAL);
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }
            finally
            {
                if (info.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(info.hbmMask);
                if (info.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(info.hbmColor);
            }
        }
        catch (Exception ex)
        {
            if (!_cursorErrorLogged)
            {
                _cursorErrorLogged = true;
                Logger.Debug("Cursor overlay failed (further messages suppressed): " + ex.Message);
            }
        }
    }
}

