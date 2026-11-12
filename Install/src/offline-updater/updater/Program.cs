using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ShutioUpdater;

/// <summary>
/// COMPLETE OFFLINE UPDATER for the portable Shutio.
///
/// This single file contains the whole (new) application as an embedded archive.
/// Running it needs nothing else: no internet, no admin, no PowerShell, no other files.
///
/// It repairs an ALREADY-INSTALLED copy of Shutio in place:
///   1. Finds the install folder (the "Apps &amp; features" entry, or beside itself,
///      or a common default, or a folder you pick).
///   2. Closes a running Shutio so the files are not locked.
///   3. Overwrites the application files with the new build.
///   4. PRESERVES your Data\ folder (settings, snips, recordings, history) untouched.
///   5. Runs the built-in self-test to prove the update is healthy (13/13), unless --no-verify.
///   6. Optionally relaunches the app.
///
/// Your saved snips, settings and recordings are never deleted.
///
/// Usage:
///   Shutio-Updater.exe                    -> auto-detect install, update, verify
///   Shutio-Updater.exe "C:\Wherever"      -> update that specific folder
///   Shutio-Updater.exe --yes              -> skip the confirmation prompt
///   Shutio-Updater.exe --no-verify        -> skip the self-test step
///   Shutio-Updater.exe --launch           -> start the app after updating
///   Shutio-Updater.exe --silent           -> no dialogs at all (for automation)
///   Shutio-Updater.exe --help
/// </summary>
internal static class Program
{
    private const string AppName      = "Shutio";
    private const string ExeName      = "Shutio.exe";
    private const string Version      = "2.1.1";
    private const string AppResource  = "AppPayload.zip";
    private const string RegPath      = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Shutio";

    [STAThread]
    private static int Main(string[] args)
    {
        string?   destArg    = null;
        bool      skipConfirm = false;
        bool      verify     = true;
        bool      autoLaunch = false;
        bool      silent     = false;

        foreach (string a in args)
        {
            string s = a.Trim();
            if (s.Length > 1 && (s[0] == '-' || s[0] == '/'))
            {
                switch (s.TrimStart('-', '/').ToLowerInvariant())
                {
                    case "yes":
                    case "y":
                    case "skip-confirm":
                        skipConfirm = true;
                        break;
                    case "no-verify":
                    case "noverify":
                        verify = false;
                        break;
                    case "launch":
                        autoLaunch = true;
                        break;
                    case "silent":
                    case "quiet":
                    case "skip-prompts":
                        silent = true;
                        skipConfirm = true;
                        break;
                    case "help":
                    case "h":
                    case "?":
                        PrintHelp(silent);
                        return 0;
                }
            }
            else if (destArg == null)
            {
                destArg = s;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"  {AppName} — Updater v{Version}");
        Console.WriteLine();

        // ----------------------------------------------------------- detect --
        string? dest = destArg;
        string  detectNote = "";
        if (string.IsNullOrWhiteSpace(dest))
        {
            dest = FindInstallLocation();
            detectNote = dest != null ? " (auto-detected)" : "";
        }
        else
        {
            detectNote = " (specified)";
        }

        if (string.IsNullOrWhiteSpace(dest) && !silent)
        {
            Console.WriteLine("  No installed Shutio found automatically — choose its folder.");
            dest = PickFolder();
        }

        dest = dest?.Trim().TrimEnd('\\', '/');
        if (string.IsNullOrWhiteSpace(dest) || !File.Exists(Path.Combine(dest, ExeName)))
        {
            if (!silent)
                MessageBox.Show(
                    $"Could not find an installed {AppName} to update.\n\n" +
                    $"Looked at: {dest ?? "(none)"}\n\n" +
                    $"If you know the folder, run:\nShutio-Updater.exe \"<folder>\"",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Console.Error.WriteLine($"  ERROR: no installed {AppName} found at '{dest}'.");
            return 1;
        }

        Console.WriteLine($"  Updating install: {dest}{detectNote}");

        // ------------------------------------------------------------- confirm -
        if (!skipConfirm && !silent)
        {
            DialogResult r = MessageBox.Show(
                $"Update the {AppName} installed at:\n\n{dest}\n\n" +
                "This replaces the app files with the new build and KEEPS your\n" +
                "snips, settings and recordings in the Data\\ folder.\n\n" +
                "Click Yes to update, or No to cancel.",
                AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes)
            {
                Console.WriteLine("  Update cancelled.");
                return 0;
            }
        }

        // ---------------------------------------------------------- close app --
        CloseRunningApp();

        // ------------------------------------------------------------- extract -
        try
        {
            Console.WriteLine("  Installing the new application files...");
            int count = ExtractPayload(dest);
            Console.WriteLine($"  {count} files updated. (your Data\\ folder was left untouched)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("  ERROR: could not update the application: " + ex.Message);
            if (!silent)
                MessageBox.Show("The update failed:\n\n" + ex.Message, AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        // ------------------------------------------- refresh Apps & features ---
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegPath);
            if (key != null)
            {
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayVersion", Version);
                key.SetValue("InstallLocation", dest);
            }
        }
        catch { /* cosmetic only */ }

        // ------------------------------------------------------------- verify --
        bool verifyOk = true;
        string verifyNote = "skipped";
        if (verify)
        {
            Console.WriteLine("  Running the built-in self-test to verify the update...");
            (verifyOk, verifyNote) = RunSelfTest(dest);
            Console.WriteLine($"  Self-test: {verifyNote}");
        }

        // --------------------------------------------------------------- done --
        Console.WriteLine();
        Console.WriteLine("  ==============  DONE  =============");
        Console.WriteLine($"  Updated : {dest}");
        Console.WriteLine($"  Data    : preserved (your snips / recordings / settings)");
        Console.WriteLine($"  Verify  : {(verifyOk ? "PASS" : "CHECK")}: {verifyNote}");
        Console.WriteLine($"  Launch  : Start Menu / Desktop icon: {AppName}");
        Console.WriteLine();

        if (autoLaunch)
        {
            Launch(dest);
            Console.WriteLine("  Launched.");
        }

        if (!silent)
        {
            string summary =
                $"The {AppName} was updated in place.\n\n" +
                $"Location : {dest}\n" +
                $"Your Data\\ folder (snips, recordings, settings) was KEPT.\n\n" +
                $"Verification: {(verifyOk ? "self-test passed" : verifyNote)}\n\n" +
                "Would you like to launch the app now?";
            DialogResult r = MessageBox.Show(summary, AppName,
                MessageBoxButtons.YesNo, verifyOk ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            if (r == DialogResult.Yes) Launch(dest);
        }

        return verifyOk ? 0 : 2;
    }

    // --------------------------------------------------------------- extraction

    private static int ExtractPayload(string dest)
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        using Stream? res = asm.GetManifestResourceStream(AppResource);
        if (res == null)
            throw new InvalidOperationException("The new application is missing from this updater.");

        string root = Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar);
        int total = 0, written = 0;
        using (var zip = new ZipArchive(res, ZipArchiveMode.Read))
        {
            total = zip.Entries.Count;
            int i = 0;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                // Defensive: never overwrite the user's portable data folder.
                if (IsUnderDataFolder(entry.FullName)) continue;

                string target = SafePath(root, entry.FullName);
                if (string.IsNullOrEmpty(entry.Name))      // a directory entry
                {
                    Directory.CreateDirectory(target);
                }
                else
                {
                    string? dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    // A few files may be briefly locked even after closing the app; retry once.
                    try
                    {
                        entry.ExtractToFile(target, overwrite: true);
                        written++;
                    }
                    catch (IOException)
                    {
                        System.Threading.Thread.Sleep(250);
                        entry.ExtractToFile(target, overwrite: true);
                        written++;
                    }
                }
                i++;
                if (i % 250 == 0) Console.WriteLine($"    {i}/{total} files...");
            }
        }
        return written;
    }

    private static bool IsUnderDataFolder(string entryName)
    {
        string n = entryName.Replace('\\', '/').TrimStart('/');
        return n.Equals("data", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("data/", StringComparison.OrdinalIgnoreCase);
    }

    private static string SafePath(string root, string entryName)
    {
        string target = Path.GetFullPath(Path.Combine(root, entryName));
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && target != root)
            throw new IOException("Blocked a path in the payload: " + entryName);
        return target;
    }

    // ----------------------------------------------------------------- verify --

    private static (bool ok, string note) RunSelfTest(string dest)
    {
        string exe = Path.Combine(dest, ExeName);
        try
        {
            var psi = new ProcessStartInfo(exe, "--selftest")
            {
                UseShellExecute = false,
                WorkingDirectory = dest,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return (false, "self-test would not start");
            p.WaitForExit(90000);
            int code = p.ExitCode;

            string reportFile = Path.Combine(dest, "Data", "Tests", "selftest-report.txt");
            string summary = "";
            if (File.Exists(reportFile))
            {
                foreach (string line in File.ReadAllLines(reportFile))
                    if (line.TrimStart().StartsWith("Summary:", StringComparison.OrdinalIgnoreCase))
                        summary = line.Trim();
            }
            if (code == 0) return (true, string.IsNullOrWhiteSpace(summary) ? "passed (exit 0)" : summary);
            return (false, $"exit code {code}" + (string.IsNullOrWhiteSpace(summary) ? "" : " · " + summary));
        }
        catch (Exception ex)
        {
            return (false, "could not run: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------ detect --

    private static string? FindInstallLocation()
    {
        // 1) The "Apps & features" entry we wrote at install / update time.
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegPath);
            string? loc = key?.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(loc) && File.Exists(Path.Combine(loc, ExeName)))
                return Path.GetFullPath(loc).TrimEnd('\\', '/');
        }
        catch { }

        // 2) Beside this updater (if it was dropped into the install folder).
        string beside = new DirectoryInfo(AppContext.BaseDirectory).FullName.TrimEnd('\\', '/');
        if (File.Exists(Path.Combine(beside, ExeName))) return beside;

        // 3) Common default locations (including this machine's project folder).
        foreach (string c in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Shutio"),
            @"I:\Apps\SnippingTool",
            @"I:\Apps\Shutio",
            @"I:\Shutio",
            @"C:\Program Files\Shutio",
        })
        {
            if (File.Exists(Path.Combine(c, ExeName))) return c;
        }
        return null;
    }

    private static string? PickFolder()
    {
        try
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = $"Choose the folder the {AppName} is installed in",
                ShowNewFolderButton = false,
            };
            foreach (string init in new[] { @"I:\",
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"C:\" })
            {
                if (Directory.Exists(init)) { dlg.SelectedPath = init; break; }
            }
            if (dlg.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                return dlg.SelectedPath;
            return null;
        }
        catch { return null; }
    }

    // --------------------------------------------------------------------- misc

    private static void CloseRunningApp()
    {
        try
        {
            foreach (Process p in Process.GetProcessesByName("Shutio"))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }
        System.Threading.Thread.Sleep(300);
    }

    private static void Launch(string dest)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(dest, ExeName)) { UseShellExecute = true });
        }
        catch { /* non-fatal */ }
    }

    private static void PrintHelp(bool silent)
    {
        string msg =
            "This is a complete offline updater — the new app is inside this file.\n\n" +
            "Usage:\n" +
            "  Shutio-Updater.exe                    auto-detect install, update, verify\n" +
            "  Shutio-Updater.exe \"C:\\Wherever\"      update that folder\n" +
            "  Shutio-Updater.exe --yes              no confirmation prompt\n" +
            "  Shutio-Updater.exe --no-verify        skip the self-test step\n" +
            "  Shutio-Updater.exe --launch           start the app after updating\n" +
            "  Shutio-Updater.exe --silent           no dialogs at all (for automation)\n" +
            "  Shutio-Updater.exe --help             show this help\n\n" +
            "Your Data\\ folder (snips, recordings, settings) is always preserved.";
        if (silent) Console.WriteLine(msg);
        else MessageBox.Show(msg, AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
