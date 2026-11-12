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

        if (HasFlag(args, "--guardian"))
        {
            // Guardian mode: a hidden helper (a second copy of this same executable) that
            // outlives nothing but protects the encoder. It waits on OUR process object;
            // the moment we die — normal exit, crash, or a hard kill from Task Manager — it
            // kills the encoder, releasing the webcam. It never shows any UI.
            // Usage: --guardian <appPid> <encoderPid>
            Logger.Info("Guardian mode requested.");
            int appPid = -1, encoderPid = -1;
            for (int i = 0; i + 2 < args.Length; i++)
            {
                if (string.Equals(args[i], "--guardian", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(args[i + 1], out appPid);
                    int.TryParse(args[i + 2], out encoderPid);
                    break;
                }
            }
            if (appPid < 0 || encoderPid < 0)
            {
                Logger.Error("Guardian mode: expected --guardian <appPid> <encoderPid>.");
                Shutdown(1);
                return;
            }
            int code = ProcessGuard.RunGuardianLoop(appPid, encoderPid);
            Logger.Info($"Guardian loop finished (code {code}).");
            Shutdown(code);
            return;
        }

        if (HasFlag(args, "--cutexport"))
        {
            // Headless, no-GUI end-to-end check of the Shutio Cut engine (model + ffmpeg runner
            // + export filtergraph). Mirrors what the timeline editor builds, so we can verify a
            // real MP4 is produced without driving the UI. Usage: --cutexport <input> [--out <path>]
            Logger.Info("Headless Shutio Cut export requested.");
            string? input = GetValue(args, "--cutexport");
            string? output = GetValue(args, "--out");
            Dispatcher.InvokeAsync(async () =>
            {
                int code = 1;
                try
                {
                    if (string.IsNullOrWhiteSpace(input) || !System.IO.File.Exists(input))
                    {
                        Console.WriteLine("ERR: input file not found: " + input);
                        Logger.Error("Cut export: input missing: " + input);
                        return;
                    }
                    string outPath = string.IsNullOrWhiteSpace(output)
                        ? System.IO.Path.Combine(AppPaths.Exports, "cutheadless.mp4")
                        : output!;
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath)!);

                    var probe = await SnippingTool.Editing.FfmpegRunner.ProbeAsync(input);
                    double dur = probe.Duration > 0.3 ? probe.Duration : 1.0;

                    var project = new SnippingTool.Editing.TimelineProject();
                    var v1 = project.Tracks.First(t => t.Kind == SnippingTool.Editing.TrackKind.Video);
                    var a1 = project.Tracks.First(t => t.Kind == SnippingTool.Editing.TrackKind.Audio);
                    project.AddTrack(SnippingTool.Editing.TrackKind.Video);     // second video track -> PiP overlay
                    var v2 = project.Tracks.Last(t => t.Kind == SnippingTool.Editing.TrackKind.Video);
                    v1.Clips.Add(new SnippingTool.Editing.TimelineClip { SourcePath = input, SourceIn = 0, Duration = dur, TimelineStart = 0 });
                    v2.Clips.Add(new SnippingTool.Editing.TimelineClip { SourcePath = input, SourceIn = 0, Duration = Math.Max(0.3, dur * 0.6), TimelineStart = dur * 0.2 });
                    a1.Clips.Add(new SnippingTool.Editing.TimelineClip { SourcePath = input, SourceIn = 0, Duration = dur, TimelineStart = 0, Volume = 0.8 });

                    Console.WriteLine($"src={System.IO.Path.GetFileName(input)} dur={dur:0.##}s -> {outPath}");
                    var (ok, error, path) = await SnippingTool.Editing.TimelineExport.ExportAsync(project, outPath, new Progress<string>(m => Console.WriteLine("  " + m)));
                    if (ok)
                    {
                        var p = await SnippingTool.Editing.FfmpegRunner.ProbeAsync(path);
                        Console.WriteLine($"OK: {path}  ({p.Duration:0.##}s, {p.Width}x{p.Height}, video={p.HasVideo}, audio={p.HasAudio})");
                        Logger.Info($"Headless Cut export OK: {path} {p.Duration:0.##}s {p.Width}x{p.Height} v={p.HasVideo} a={p.HasAudio}");
                        code = 0;
                    }
                    else
                    {
                        Console.WriteLine("ERR: " + error);
                        Logger.Error("Headless Cut export failed: " + error);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ERR: " + ex.Message);
                    Logger.Error("Headless Cut export crashed", ex);
                }
                Shutdown(code);
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (HasFlag(args, "--previewtest"))
        {
            // Headless check of the smooth-preview pipeline (one ffmpeg image2pipe process,
            // JPEG framing on a background thread, time-indexed buffer) exactly as the UI uses it.
            Logger.Info("Headless preview decoder test requested.");
            string? input = GetValue(args, "--previewtest");
            Dispatcher.InvokeAsync(async () =>
            {
                int code = 1;
                try
                {
                    if (string.IsNullOrWhiteSpace(input) || !System.IO.File.Exists(input))
                    {
                        Console.WriteLine("ERR: input file not found: " + input);
                        return;
                    }
                    var probe = await SnippingTool.Editing.FfmpegRunner.ProbeAsync(input);
                    double dur = probe.Duration > 0.3 ? probe.Duration : 1.0;
                    var dec = new SnippingTool.Editing.PreviewDecoder();
                    dec.Start(input, 0, dur, 30, 512, 288);
                    await Task.Delay(750);
                    bool any = false;
                    long total = 0;
                    for (double t = 0.1; t < dur; t += 0.2)
                    {
                        var jpg = dec.FrameAt(t);
                        if (jpg != null) { any = true; total += jpg.Length; }
                        Console.WriteLine($"  t={t:0.##}s -> {(jpg == null ? "no frame" : jpg.Length + " bytes")}");
                    }
                    dec.Stop();
                    if (any && total > 500)
                    {
                        Console.WriteLine($"OK: preview decoder delivered frames (first {dur:0.##}s of source, {total} bytes total)");
                        Logger.Info($"Headless preview decode OK: {input} dur={dur:0.##}s bytes={total}");
                        code = 0;
                    }
                    else
                    {
                        Console.WriteLine("ERR: no frames delivered");
                        Logger.Error("Headless preview decode: no frames delivered.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ERR: " + ex.Message);
                    Logger.Error("Headless preview decode crashed", ex);
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
                               HasFlag(args, "--history") || HasFlag(args, "--settings") || HasFlag(args, "--record") ||
                               HasFlag(args, "--video");
        bool showMain = !hideRequested && !settings.StartMinimizedInTray && !actionRequested;
        Host.Start(showMain);

        // Optional update prompt at startup (quiet unless a newer release actually exists).
        if (!hideRequested && settings.CheckForUpdatesAtStartup)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // Let the startup UI settle first — don't compete with it.
                    await Task.Delay(3000);
                    if (!Host.IsExiting) Host.MaybePromptStartupUpdate();
                }
                catch
                {
                    // Update checking is best-effort and must never affect the app.
                }
            }, DispatcherPriority.ApplicationIdle);
        }

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

                if (HasFlag(args, "--video"))
                {
                    Host.ShowMain();
                    // The window's Loaded handler resets to the Screenshot tab at a higher priority,
                    // so defer the Video-tab switch to ContextIdle (runs after Loaded) for --video.
                    var mw = Host.MainWindow;
                    mw?.Dispatcher.BeginInvoke(new Action(() => mw.SwitchToVideoTab()),
                        System.Windows.Threading.DispatcherPriority.ContextIdle);
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
