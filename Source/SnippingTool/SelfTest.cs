using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Interop;
using SnippingTool.Capture;
using SnippingTool.Core;
using SnippingTool.Imaging;
using SnippingTool.Recording;
using SnippingTool.Storage;

namespace SnippingTool;

/// <summary>
/// Headless verification of every subsystem that can be exercised without a human.
/// Run with Shutio.exe --selftest. Results are written to Data\Logs and Data\Tests.
/// </summary>
public static class SelfTest
{
    private sealed record Result(string Name, string Status, string Detail, long Milliseconds);

    private sealed class SkipTestException : Exception
    {
        public SkipTestException(string message) : base(message) { }
    }

    private static readonly List<Result> Results = new();
    private static readonly Stopwatch Clock = new();

    public static async Task<int> RunAsync()
    {
        Results.Clear();
        Clock.Start();
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Logger.Info("===== Self-test started =====");
        AppPaths.EnsureDirectories();

        await Run("Environment", () =>
        {
            var vs = CaptureService.VirtualScreen;
            var monitors = NativeMethods.GetMonitors();
            Assert(vs.Width > 0 && vs.Height > 0, "Virtual screen has no size.");
            Assert(monitors.Count > 0, "No displays were detected.");
            string dpi = "";
            try
            {
                dpi = "; process DPI awareness: " + System.Windows.Forms.Application.HighDpiMode;
            }
            catch { }
            return Task.FromResult($"Windows {Environment.OSVersion.Version} · user {Environment.UserName} · virtual screen {vs.Width}×{vs.Height} at {vs.X},{vs.Y} · {monitors.Count} display(s){dpi}");
        });

        await Run("Desktop capture (BitBlt)", () =>
        {
            var vs = CaptureService.VirtualScreen;
            using var shot = CaptureService.CaptureVirtualScreen();
            Assert(shot.Width == vs.Width && shot.Height == vs.Height,
                $"Expected {vs.Width}×{vs.Height}, got {shot.Width}×{shot.Height}.");
            Assert(!CaptureService.IsMostlyBlank(shot), "The captured desktop looks blank.");
            string path = Path.Combine(AppPaths.Tests, "selftest-screen.png");
            BitmapUtils.Save(shot, path);
            return Task.FromResult($"{shot.Width}×{shot.Height} captured; evidence {path}");
        });

        await Run("Region capture", () =>
        {
            var vs = CaptureService.VirtualScreen;
            var region = Rectangle.Intersect(new Rectangle(vs.Left + 30, vs.Top + 30, 200, 120), vs);
            Assert(region.Width > 0 && region.Height > 0, "Region is outside the desktop.");
            using var shot = CaptureService.CaptureRegion(region);
            Assert(shot.Width == region.Width && shot.Height == region.Height,
                $"Expected {region.Width}×{region.Height}, got {shot.Width}×{shot.Height}.");
            return Task.FromResult($"{shot.Width}×{shot.Height} at {region.X},{region.Y}");
        });

        await Run("Freeform transparency", () =>
        {
            using var source = new Bitmap(400, 300, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(source))
            {
                g.Clear(Color.FromArgb(255, 240, 240, 240));
                using var brush = new SolidBrush(Color.FromArgb(255, 60, 120, 220));
                g.FillRectangle(brush, 40, 40, 320, 220);
            }
            var points = new List<Point>
            {
                new(50, 50), new(350, 60), new(340, 250), new(60, 240)
            };
            using var masked = CaptureService.CreateFreeformSnip(source, points);
            int outside = masked.GetPixel(8, 8).A;
            int inside = masked.GetPixel(200, 150).A;
            Assert(outside == 0, $"Outside pixel alpha was {outside}, expected 0.");
            Assert(inside > 240, $"Inside pixel alpha was {inside}, expected opaque.");
            string path = Path.Combine(AppPaths.Tests, "selftest-freeform.png");
            BitmapUtils.Save(masked, path);
            return Task.FromResult($"outside alpha {outside}, inside alpha {inside}; evidence {path}");
        });

        await Run("Window detection", () =>
        {
            var windows = CaptureService.EnumerateWindows();
            Assert(windows.Count > 0, "No capturable top-level windows were found.");
            var first = windows[0];
            var bounds = CaptureService.GetWindowBounds(first.Handle);
            Assert(bounds.Width > 0 && bounds.Height > 0, "A detected window has empty bounds.");
            var hit = CaptureService.WindowAt(new Point(first.Bounds.Left + first.Bounds.Width / 2,
                first.Bounds.Top + first.Bounds.Height / 2), windows);
            Assert(hit != null, "Window hit-testing returned nothing.");
            return Task.FromResult($"{windows.Count} windows; first: {first.Title} ({first.Bounds.Width}×{first.Bounds.Height})");
        });

        await Run("PNG, JPEG, and BMP round-trip", () =>
        {
            using var source = new Bitmap(160, 100, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(source))
            {
                using var gradient = new LinearGradientBrush(new Rectangle(0, 0, 160, 100),
                    Color.FromArgb(255, 30, 110, 210), Color.FromArgb(255, 240, 120, 40), 35f);
                g.FillRectangle(gradient, 0, 0, 160, 100);
                g.FillEllipse(Brushes.White, 30, 20, 60, 60);
            }
            var sizes = new List<string>();
            foreach (var extension in new[] { ".png", ".jpg", ".bmp" })
            {
                string path = Path.Combine(AppPaths.Tests, "selftest-format" + extension);
                BitmapUtils.Save(source, path);
                Assert(File.Exists(path), $"{extension} file was not created.");
                var info = new FileInfo(path);
                Assert(info.Length > 100, $"{extension} file is suspiciously small ({info.Length} bytes).");
                using var loaded = BitmapUtils.Load(path);
                Assert(loaded.Width == source.Width && loaded.Height == source.Height,
                    $"{extension} dimensions changed to {loaded.Width}×{loaded.Height}.");
                sizes.Add($"{extension} {info.Length / 1024.0:0.0} KB");
            }
            return Task.FromResult(string.Join(", ", sizes));
        });

        await Run("Clipboard image", () =>
        {
            using var source = new Bitmap(120, 80, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(source)) g.Clear(Color.FromArgb(255, 210, 60, 90));
            bool copied = ClipboardService.SetImage(source);
            Assert(copied, "ClipboardService.SetImage returned false.");
            Thread.Sleep(90);
            bool contains = System.Windows.Clipboard.ContainsImage();
            Assert(contains, "The clipboard does not contain an image after copying.");
            var image = System.Windows.Clipboard.GetImage();
            Assert(image != null && image.PixelWidth == 120 && image.PixelHeight == 80,
                $"Clipboard image was {image?.PixelWidth}×{image?.PixelHeight}.");
            System.Windows.Clipboard.Clear();
            return Task.FromResult("120×80 image written and read back from the Windows clipboard");
        });

        await Run("History persistence", () =>
        {
            string? backup = File.Exists(AppPaths.HistoryFile) ? File.ReadAllText(AppPaths.HistoryFile) : null;
            string testPath = Path.Combine(AppPaths.Tests, $"selftest-history-{DateTime.Now:HHmmss}.png");
            var history = new HistoryService();
            history.Load();
            int before = history.Count;
            try
            {
                using var source = new Bitmap(140, 90, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(source)) g.Clear(Color.FromArgb(255, 40, 160, 120));
                BitmapUtils.Save(source, testPath);
                var entry = history.Add(testPath, CaptureMode.Rectangle, source);
                Assert(entry != null, "History did not add the test snip.");

                var reloaded = new HistoryService();
                reloaded.Load();
                var found = reloaded.All.FirstOrDefault(e => e.Id == entry!.Id);
                Assert(found != null, "The history entry was not present after reloading history.json.");
                Assert(File.Exists(found!.ThumbnailPath), "The history thumbnail was not created.");
                Assert(reloaded.Count >= before + 1, "History count did not increase.");

                history.Remove(entry!, deleteFiles: true);
                Assert(!File.Exists(testPath), "Deleting a history entry did not delete its image.");
                return Task.FromResult($"added, reloaded, verified, and removed “{found.FileName}” ({found.Width}×{found.Height})");
            }
            finally
            {
                if (backup != null) File.WriteAllText(AppPaths.HistoryFile, backup);
                else if (File.Exists(AppPaths.HistoryFile)) File.Delete(AppPaths.HistoryFile);
                try { if (File.Exists(testPath)) File.Delete(testPath); } catch { }
            }
        });

        await Run("Settings validation", () =>
        {
            var settings = new Settings
            {
                DelaySeconds = 7,
                FileNamePattern = "bad/name:with<invalid>chars",
                DefaultMode = (CaptureMode)99,
                RecordingFps = 13
            };
            settings.Sanitize();
            Assert(settings.DelaySeconds == 0, "Invalid delay was not reset.");
            Assert(settings.RecordingFps == 15, "Invalid recording frame rate was not reset.");
            Assert(settings.DefaultMode == CaptureMode.Rectangle, "Invalid capture mode was not reset.");
            foreach (char c in Path.GetInvalidFileNameChars())
                Assert(!settings.FileNamePattern.Contains(c), "File name pattern still contains an invalid character.");
            Assert(settings.Hotkeys.Count == HotkeyDefaults.Create().Count, "Default hotkeys were not restored.");
            string generated = CaptureStorage.GenerateFilePath(new Settings
            {
                SaveDirectory = AppPaths.Tests,
                FileNamePattern = "selftest-name_{yyyy}-{MM}-{dd}_{HH}-{mm}-{ss}_{mode}"
            }, CaptureMode.Freeform);
            Assert(generated.StartsWith(AppPaths.Tests, StringComparison.OrdinalIgnoreCase), "Generated path is outside the test folder.");
            Assert(generated.Contains("Freeform"), "Mode token was not expanded in the generated file name.");
            return Task.FromResult("invalid delay, fps, mode, and file name characters were repaired");
        });

        await Run("Global hotkey registration", () =>
        {
            var parameters = new HwndSourceParameters("SnippingToolSelfTestHotkeys")
            {
                Width = 1,
                Height = 1,
                WindowStyle = 0
            };
            using var source = new HwndSource(parameters);
            var manager = new HotkeyManager();
            manager.Attach(source.Handle, _ => { });

            // Friendly bare-key parsing (the stop-recording shortcut defaults to "Esc").
            Assert(HotkeyGesture.Parse("Esc", allowBare: true)?.Key == System.Windows.Input.Key.Escape,
                "Bare 'Esc' did not resolve to the Escape key.");
            Assert(HotkeyGesture.Parse("Esc") == null,
                "A bare key must be rejected for an always-on shortcut (no modifier).");
            Assert(HotkeyGesture.Parse("Ctrl+N")?.Key == System.Windows.Input.Key.N,
                "'Ctrl+N' did not resolve to the N key.");
            try
            {
                var results = manager.RegisterAll(new Dictionary<string, string>
                {
                    ["TestOne"] = "Ctrl+Alt+Shift+F8",
                    ["TestTwo"] = "Ctrl+Alt+Shift+F9"
                });
                Assert(results.All(r => r.Success),
                    "Could not register the test hotkeys: " + string.Join("; ", results.Select(r => $"{r.Gesture}={r.Error}")));
                Assert(manager.AnyRegistered, "HotkeyManager reported nothing registered.");

                var duplicates = manager.RegisterAll(new Dictionary<string, string>
                {
                    ["DupOne"] = "Ctrl+Alt+Shift+F10",
                    ["DupTwo"] = "Ctrl+Alt+Shift+F10"
                });
                Assert(duplicates.Count(r => !r.Success) == 1 && duplicates.Any(r => r.ErrorCode == -3),
                    "Duplicate shortcut detection did not reject exactly one entry.");
                return Task.FromResult("Ctrl+Alt+Shift+F8/F9 registered; duplicate gesture rejected with conflict code -3");
            }
            finally
            {
                manager.UnregisterAll();
            }
        });

        await Run("Windows OCR", async () =>
        {
            if (!OcrService.IsAvailable)
                throw new SkipTestException("No Windows OCR language pack is installed for this account.");
            using var source = new Bitmap(1000, 260, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(source))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using var font = new Font("Segoe UI", 58, FontStyle.Bold, GraphicsUnit.Pixel);
                g.DrawString("Snipping Tool 12345", font, Brushes.Black, 36, 80);
            }
            string path = Path.Combine(AppPaths.Tests, "selftest-ocr-input.png");
            BitmapUtils.Save(source, path);
            var result = await OcrService.RecognizeAsync(source);
            Assert(result.Text.Length > 0, "OCR returned no text.");
            bool matches = result.Text.Contains("Snipping", StringComparison.OrdinalIgnoreCase) ||
                           result.Text.Contains("12345", StringComparison.OrdinalIgnoreCase) ||
                           result.Text.Contains("Tool", StringComparison.OrdinalIgnoreCase);
            Assert(matches, $"OCR text did not contain the expected words: “{result.Text}”");
            return $"{result.Language}: “{result.Text.Replace(Environment.NewLine, " / ")}” (input {path})";
        });

        await Run("ffmpeg engine", () =>
        {
            string? path = RecordingController.FfmpegPath;
            if (path == null) throw new SkipTestException("ffmpeg.exe was not found in the Tools folder.");
            var info = new FileInfo(path);
            Assert(info.Length > 100_000, "ffmpeg.exe looks incomplete.");
            return Task.FromResult($"{path} ({info.Length / 1024 / 1024.0:0.0} MB)");
        });

        await Run("MP4 screen recording", async () =>
        {
            if (!RecordingController.IsAvailable)
                throw new SkipTestException("ffmpeg.exe is not installed, so recording cannot be tested.");
            var vs = CaptureService.VirtualScreen;
            var region = new Rectangle(vs.Left + 24, vs.Top + 24,
                Math.Min(320, Math.Max(16, vs.Width - 48)),
                Math.Min(240, Math.Max(16, vs.Height - 48)));
            string path = Path.Combine(AppPaths.Tests, $"selftest-recording-{DateTime.Now:HHmmss}.mp4");
            using var controller = new RecordingController();
            controller.Start(new RecordingOptions
            {
                Region = region,
                Fps = 15,
                IncludeCursor = true,
                OutputPath = path
            });
            await Task.Delay(2600);
            var stopped = await controller.StopAsync();
            Assert(stopped.Success, stopped.Error ?? "The recorder reported failure.");
            Assert(File.Exists(path), "The MP4 file was not created.");
            long length = new FileInfo(path).Length;
            Assert(length > 10_000, $"The MP4 file is too small ({length} bytes).");
            var header = new byte[12];
            using (var stream = File.OpenRead(path)) stream.ReadExactly(header, 0, header.Length);
            bool ftyp = header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p';
            Assert(ftyp, "The output does not start with an MP4 'ftyp' box.");
            return $"{region.Width}×{region.Height}, {stopped.Frames} frames in {stopped.Duration.TotalSeconds:0.0}s, {length / 1024.0:0.0} KB, {path}";
        });

        Clock.Stop();
        return WriteReport(stamp, exitCode: Results.Any(r => r.Status == "FAIL") ? 1 : 0);
    }

    private static async Task Run(string name, Func<Task<string>> test)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            string detail = await test();
            watch.Stop();
            Results.Add(new Result(name, "PASS", detail, watch.ElapsedMilliseconds));
            Logger.Info($"[SELFTEST PASS] {name}: {detail}");
        }
        catch (SkipTestException ex)
        {
            watch.Stop();
            Results.Add(new Result(name, "SKIP", ex.Message, watch.ElapsedMilliseconds));
            Logger.Warn($"[SELFTEST SKIP] {name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            watch.Stop();
            Results.Add(new Result(name, "FAIL", ex.Message, watch.ElapsedMilliseconds));
            Logger.Error($"[SELFTEST FAIL] {name}", ex);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static int WriteReport(string stamp, int exitCode)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Shutio — self-test report");
        builder.AppendLine("=================================");
        builder.AppendLine($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"User: {Environment.UserName}");
        builder.AppendLine($"OS: {Environment.OSVersion}");
        builder.AppendLine($"Process: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}");
        builder.AppendLine($"Root: {AppPaths.Root}");
        builder.AppendLine();
        foreach (var result in Results)
            builder.AppendLine($"[{result.Status}] {result.Name} ({result.Milliseconds} ms)\n        {result.Detail}");
        int pass = Results.Count(r => r.Status == "PASS");
        int fail = Results.Count(r => r.Status == "FAIL");
        int skip = Results.Count(r => r.Status == "SKIP");
        builder.AppendLine();
        builder.AppendLine($"Summary: {pass} passed, {fail} failed, {skip} skipped ({(int)Math.Round(Clock.Elapsed.TotalSeconds)} s)");
        builder.AppendLine($"Exit code: {exitCode}");

        string text = builder.ToString();
        try
        {
            string latest = Path.Combine(AppPaths.Logs, "selftest-latest.txt");
            string dated = Path.Combine(AppPaths.Logs, $"selftest-{stamp}.txt");
            string evidence = Path.Combine(AppPaths.Tests, "selftest-report.txt");
            File.WriteAllText(latest, text, Encoding.UTF8);
            File.WriteAllText(dated, text, Encoding.UTF8);
            File.WriteAllText(evidence, text, Encoding.UTF8);
            Logger.Info($"Self-test report written to {latest}");
        }
        catch (Exception ex)
        {
            Logger.Error("Could not write the self-test report", ex);
        }
        try { Console.WriteLine(text); } catch { }
        return exitCode;
    }
}
