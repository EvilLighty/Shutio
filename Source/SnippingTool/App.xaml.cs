using System.Windows;
using System.Windows.Threading;
using SnippingTool.Core;

namespace SnippingTool;

public partial class App : System.Windows.Application
{
    public static AppHost? Host { get; private set; }
    private bool _reportedCrash;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.EnsureDirectories();
        Logger.Initialize();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Error("Unhandled application exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Warn("Unobserved background task exception", args.Exception);
            args.SetObserved();
        };

        var args = e.Args;
        if (HasFlag(args, "--selftest"))
        {
            Logger.Info("Self-test mode requested.");
            Dispatcher.InvokeAsync(async () =>
            {
                int code;
                try
                {
                    code = await SelfTest.RunAsync();
                }
                catch (Exception ex)
                {
                    Logger.Error("Self-test crashed", ex);
                    code = 2;
                }
                Shutdown(code);
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (!SingleInstance.TryAcquire())
        {
            SingleInstance.SignalExistingInstance();
            Logger.Info("Another instance is running; signalled it and exiting.");
            Shutdown(0);
            return;
        }

        var settings = SettingsService.Load();
        ThemeManager.Initialize(settings.Theme);
        Logger.Info($"Settings loaded: theme={settings.Theme}, mode={settings.DefaultMode}, delay={settings.DelaySeconds}, autoCopy={settings.AutoCopy}, autoSave={settings.AutoSave}, editor={settings.OpenEditorAfterCapture}.");

        Host = new AppHost(settings);
        bool hideRequested = HasFlag(args, "--hidden");
        bool actionRequested = HasFlag(args, "--capture") || HasFlagPrefix(args, "--capture=") ||
                               HasFlag(args, "--history") || HasFlag(args, "--settings") || HasFlag(args, "--record");
        bool showMain = !hideRequested && !settings.StartMinimizedInTray && !actionRequested;
        Host.Start(showMain);

        // Run requested actions after the message loop is flowing.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                if (HasFlag(args, "--capture") || HasFlagPrefix(args, "--capture="))
                {
                    var mode = ParseMode(GetValue(args, "--capture"), settings.DefaultMode);
                    _ = Host.CaptureAsync(mode);
                }
                else if (HasFlag(args, "--record"))
                {
                    Host.BeginRecordingFromUi();
                }
                else if (HasFlag(args, "--history"))
                {
                    Host.ShowHistory();
                }
                else if (HasFlag(args, "--settings"))
                {
                    Host.ShowSettings();
                }

                if (HasFlag(args, "--exit"))
                    Host.RequestExit();
            }
            catch (Exception ex)
            {
                Logger.Error("Startup action failed", ex);
            }
        }));
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
        if (_reportedCrash) return;
        _reportedCrash = true;
        try
        {
            var owner = Host?.MainWindow is { IsVisible: true } main ? main : null;
            UI.MessageDialog.Error(owner, "Something went wrong",
                $"{e.Exception.Message}\n\nThe error was written to the log:\n{Logger.CurrentLogFile}");
        }
        catch { }
        finally
        {
            _reportedCrash = false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Host?.Dispose();
            Logger.Info($"===== Shutio exited (code {e.ApplicationExitCode}) =====");
        }
        catch { }
        base.OnExit(e);
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static bool HasFlagPrefix(string[] args, string prefix) =>
        args.Any(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static string? GetValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : "";
            if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return args[i][(name.Length + 1)..];
        }
        return null;
    }

    private static CaptureMode ParseMode(string? value, CaptureMode fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return value.Trim().ToLowerInvariant() switch
        {
            "rect" or "rectangle" => CaptureMode.Rectangle,
            "free" or "freeform" => CaptureMode.Freeform,
            "window" => CaptureMode.Window,
            "full" or "fullscreen" or "desktop" => CaptureMode.Fullscreen,
            "monitor" or "display" or "screen" => CaptureMode.Monitor,
            _ => fallback
        };
    }
}
