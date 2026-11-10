using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SnippingTool.Core;

public static class ThemeManager
{
    private static ResourceDictionary? _current;
    private static bool _listening;
    public static bool IsDark { get; private set; }
    public static AppTheme CurrentMode { get; private set; } = AppTheme.System;

    public static void Initialize(AppTheme mode)
    {
        Apply(mode);
        if (!_listening)
        {
            SystemEvents.UserPreferenceChanged += (_, _) =>
            {
                if (CurrentMode == AppTheme.System)
                {
                    var app = Application.Current;
                    if (app != null)
                        app.Dispatcher.BeginInvoke(() => Apply(AppTheme.System));
                }
            };
            _listening = true;
        }
    }

    public static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i) return i != 0;
        }
        catch { }
        return true;
    }

    public static void Apply(AppTheme mode)
    {
        CurrentMode = mode;
        var light = mode switch
        {
            AppTheme.Light => true,
            AppTheme.Dark => false,
            _ => SystemUsesLightTheme()
        };
        IsDark = !light;
        try
        {
            var uri = new Uri(light ? "Themes/Light.xaml" : "Themes/Dark.xaml", UriKind.Relative);
            var dict = new ResourceDictionary { Source = uri };
            var app = Application.Current;
            if (app == null) return;
            app.Resources.MergedDictionaries.Remove(_current);
            app.Resources.MergedDictionaries.Insert(0, dict);
            _current = dict;
            app.Resources["IsDarkTheme"] = !light;
            Logger.Info($"Theme applied: {(light ? "Light" : "Dark")} (mode {mode})");
            foreach (Window window in app.Windows)
                ApplyWindowChrome(window);
        }
        catch (Exception ex)
        {
            Logger.Error("Theme apply failed", ex);
        }
    }

    /// <summary>
    /// Builds the translucent "glass" background brush for a window. The opacity value is the
    /// fraction of transparency (0 = opaque, 0.9 = very see-through). The desktop shows through
    /// whatever is not painted by the opaque inner cards, giving the frosted-glass look.
    /// </summary>
    public static Brush GlassBrush(double transparency, bool isDark)
    {
        double o = Math.Clamp(transparency, 0.0, 0.95);
        byte alpha = (byte)Math.Round((1.0 - o) * 255);
        return isDark
            ? new SolidColorBrush(Color.FromArgb(alpha, 21, 24, 30))
            : new SolidColorBrush(Color.FromArgb(alpha, 236, 240, 247));
    }

    /// <summary>
    /// The opaque surface color used to tint the frosted-glass background (matches the theme).
    /// Exposed as a System.Drawing color so it can be painted with GDI in <see cref="GlassBackdrop"/>.
    /// </summary>
    public static System.Drawing.Color SurfaceDrawColor(bool isDark) =>
        isDark ? System.Drawing.Color.FromArgb(21, 24, 30) : System.Drawing.Color.FromArgb(236, 240, 247);

    /// <summary>Applies (or clears) the translucent glass background on a window from the current settings.</summary>
    public static void ApplyGlass(Window window, bool translucent, double transparency)
    {
        try
        {
            window.Background = translucent
                ? GlassBrush(transparency, IsDark)
                : window.TryFindResource("AppBackgroundBrush") as Brush
                    ?? (IsDark ? GlassBrush(0.0, true) : GlassBrush(0.0, false));
        }
        catch (Exception ex)
        {
            Logger.Debug("Apply glass failed: " + ex.Message);
        }
    }

    public static void ApplyWindowChrome(Window window)
    {
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(window);
            var hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = IsDark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int corners = 2; // DWMWCP_ROUND
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
        }
        catch (Exception ex)
        {
            Logger.Debug("DWM chrome unavailable: " + ex.Message);
        }
    }
}
