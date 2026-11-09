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
