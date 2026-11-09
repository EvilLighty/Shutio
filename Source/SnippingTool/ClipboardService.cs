using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using SnippingTool.Core;
using SnippingTool.Imaging;

namespace SnippingTool;

public static class ClipboardService
{
    public static bool SetImage(Bitmap bitmap)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                var source = BitmapUtils.ToBitmapSource(bitmap);
                Clipboard.SetImage(source);
                Logger.Info($"Image copied to clipboard ({bitmap.Width}x{bitmap.Height}).");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Clipboard image attempt {attempt + 1} failed", ex);
                Thread.Sleep(60);
            }
        }
        return false;
    }

    public static bool SetText(string text)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                Clipboard.SetText(text ?? "");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Clipboard text attempt {attempt + 1} failed", ex);
                Thread.Sleep(60);
            }
        }
        return false;
    }
}
