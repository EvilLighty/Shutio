using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ShutioInstaller;

/// <summary>
/// COMPLETE OFFLINE INSTALLER for the portable Shutio.
///
/// This single file contains the entire application (as an embedded archive) plus a
/// standalone uninstaller. Running it needs nothing else: no internet, no admin, no
/// PowerShell, no other files. Share this one .exe with anyone and it installs on any
/// Windows 10/11 x64 PC.
///
/// At install time it:
///   1. Asks you to choose a folder (or takes one from the command line).
///   2. Extracts the whole app there.
///   3. Drops UninstallShutio.exe into that folder.
///   4. Creates Start Menu + Desktop shortcuts.
///   5. Registers a per-user "Apps &amp; features" entry (uninstall via that too).
///
/// Usage:
///   Shutio-setup.exe                     -> choose a folder (dialog)
///   Shutio-setup.exe "C:\Wherever"       -> install there
///   Shutio-setup.exe "C:\Wherever" --skip-prompts   -> non-interactive
///   Shutio-setup.exe --help
/// </summary>
internal static class Program
{
    private const string AppName      = "Shutio";
    private const string ExeName      = "Shutio.exe";
    private const string Version      = "1.1.1";
    private const string Publisher    = "SalsaNOW Edition";
    private const string AppResource  = "AppPayload.zip";
    private const string UninstExe    = "UninstallShutio.exe";
    private const string UninstRes    = "UninstallShutio.exe";
    private const string RegPath      = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Shutio";

    [STAThread]
    private static int Main(string[] args)
    {
        Console.WriteLine();
        Console.WriteLine($"  {AppName} — Offline Installer v{Version}");
        Console.WriteLine();

        string? dest = null;
        bool    skipPrompts = false;
        bool    autoLaunch  = false;

        foreach (string a in args)
        {
            string s = a.Trim();
            if (s.Length > 1 && (s[0] == '-' || s[0] == '/'))
            {
                switch (s.TrimStart('-', '/').ToLowerInvariant())
                {
                    case "skip-prompts":
                    case "skipprompts":
                    case "np":
                    case "y":
                    case "n":
                        skipPrompts = true;
                        break;
                    case "launch":
                        autoLaunch = true;
                        break;
                    case "help":
                    case "h":
                    case "?":
                        PrintHelp();
                        return 0;
                }
            }
            else if (dest == null)
            {
                dest = s;
            }
        }

        // ------------------------------------------------- choose the folder -
        if (string.IsNullOrWhiteSpace(dest))
        {
            dest = PickFolder();
            if (string.IsNullOrWhiteSpace(dest))
            {
                Console.WriteLine("  Installation cancelled.");
                return 0;
            }
        }
        dest = dest.Trim().TrimEnd('\\', '/');

        if (!IsInstallable(dest))
        {
            Console.Error.WriteLine($"  ERROR: that location is not writable: {dest}");
            Console.Error.WriteLine("         Pick a folder under your user profile or on the I: drive.");
            return 1;
        }

        Console.WriteLine($"  Installing to: {dest}");

        // ------------------------------------------------------- extract -----
        try
        {
            Console.WriteLine("  Extracting the application...");
            ExtractPayload(dest);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("  ERROR: could not extract the application: " + ex.Message);
            return 1;
        }
        Console.WriteLine("  Extract complete.");

        // -------------------------------------------------- drop uninstaller -
        try
        {
            WriteUninstaller(dest);
            Console.WriteLine("  Added the uninstaller to the install folder.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("  (could not add the uninstaller: " + ex.Message + ")");
        }

        // -------------------------------------------------------- shortcuts --
        if (CreateShortcuts(dest))
            Console.WriteLine("  Created Start Menu and Desktop shortcuts.");
        else
            Console.WriteLine("  (could not create shortcuts — you can still launch from the install folder).");

        // -------------------------------------------------- apps & features --
        if (WriteUninstallEntry(dest))
            Console.WriteLine("  Registered an entry in Apps & features.");
        else
            Console.WriteLine("  (could not register the Apps & features entry).");

        // ------------------------------------------------------------ done ---
        Console.WriteLine();
        Console.WriteLine("  ==============  DONE  =============");
        Console.WriteLine($"  Installed to : {dest}");
        Console.WriteLine($"  Launch       : Start Menu / Desktop icon: {AppName}");
        Console.WriteLine($"  Uninstall    : {UninstExe}  (or Apps & features)");
        Console.WriteLine();

        if (skipPrompts)
        {
            if (autoLaunch) Launch(dest);
            Console.WriteLine("  (interactive prompts skipped)");
            return 0;
        }

        Console.Write($"  Launch the {AppName} now? (Y/N) ");
        string? ans = Console.ReadLine();
        if (ans is { } a2 && a2.Trim().StartsWith("Y", StringComparison.OrdinalIgnoreCase))
            Launch(dest);

        Console.WriteLine("  Press Enter to close this window.");
        Console.ReadLine();
        return 0;
    }

    // ------------------------------------------------------------ extraction --

    private static void ExtractPayload(string dest)
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        using Stream? res = asm.GetManifestResourceStream(AppResource);
        if (res == null)
            throw new InvalidOperationException("The embedded application is missing from this installer.");

        string root = Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar);
        int total = 0;
        using (var zip = new ZipArchive(res, ZipArchiveMode.Read))
        {
            total = zip.Entries.Count;
            int i = 0;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string target = SafePath(root, entry.FullName);
                if (string.IsNullOrEmpty(entry.Name))          // a directory entry
                {
                    Directory.CreateDirectory(target);
                }
                else
                {
                    string? dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    entry.ExtractToFile(target, overwrite: true);
                }
                i++;
                if (i % 250 == 0) Console.WriteLine($"    {i}/{total} files...");
            }
        }
    }

    private static void WriteUninstaller(string dest)
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        using Stream? res = asm.GetManifestResourceStream(UninstRes);
        if (res == null)
            throw new InvalidOperationException("The embedded uninstaller is missing from this installer.");
        string target = Path.Combine(dest, UninstExe);
        using FileStream fs = File.Create(target);
        res.CopyTo(fs);
    }

    private static string SafePath(string root, string entryName)
    {
        string target = Path.GetFullPath(Path.Combine(root, entryName));
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && target != root)
            throw new IOException("Blocked a path in the payload: " + entryName);
        return target;
    }

    // ------------------------------------------------------------ shortcuts --

    private static bool CreateShortcuts(string dest)
    {
        try
        {
            dynamic shell = (dynamic)Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            string target = Path.Combine(dest, ExeName);
            string icon   = Path.Combine(dest, "Assets", "app.ico");

            string startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs", "Shutio.lnk");
            string desktop = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Shutio.lnk");

            MakeShortcut(shell, startMenu, target, dest, icon);
            MakeShortcut(shell, desktop, target, dest, icon);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void MakeShortcut(dynamic shell, string lnkPath, string target, string workDir, string icon)
    {
        string? dir = Path.GetDirectoryName(lnkPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        dynamic sc = shell.CreateShortcut(lnkPath);
        sc.TargetPath       = target;
        sc.WorkingDirectory = workDir;
        sc.Description      = "Capture, annotate, OCR and screen recording";
        if (File.Exists(icon)) sc.IconLocation = icon + ",0";
        sc.Save();
    }

    // ----------------------------------------------------------------- reg ---

    private static bool WriteUninstallEntry(string dest)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegPath);
            if (key == null) return false;

            key.SetValue("DisplayName",     AppName);
            key.SetValue("DisplayVersion",  Version);
            key.SetValue("Publisher",       Publisher);
            key.SetValue("InstallLocation", dest);
            key.SetValue("UninstallString", $"\"{Path.Combine(dest, UninstExe)}\"");
            key.SetValue("NoModify", 1);
            key.SetValue("NoChange", 1);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ----------------------------------------------------------------- misc ---

    private static void PrintHelp()
    {
        Console.WriteLine("  This is a complete offline installer — the whole app is inside this file.");
        Console.WriteLine("  Usage:");
        Console.WriteLine("    Shutio-setup.exe                        choose a folder (dialog)");
        Console.WriteLine("    Shutio-setup.exe \"C:\\Wherever\"         install to that folder");
        Console.WriteLine("    Shutio-setup.exe \"C:\\Wherever\" --skip-prompts   non-interactive");
        Console.WriteLine("    Shutio-setup.exe --help                 show this help");
    }

    private static string? PickFolder()
    {
        try
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = $"Choose where to install the {AppName}",
                ShowNewFolderButton = true,
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
        catch
        {
            string fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Shutio");
            Console.WriteLine($"  (folder picker unavailable — using default) {fallback}");
            return fallback;
        }
    }

    private static bool IsInstallable(string dest)
    {
        string checkDir = Directory.Exists(dest) ? dest : Path.GetDirectoryName(dest) ?? string.Empty;
        if (string.IsNullOrEmpty(checkDir) || !Directory.Exists(checkDir)) checkDir = @"C:\";
        string probe = Path.Combine(checkDir, "probe_" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Launch(string dest)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(dest, ExeName)) { UseShellExecute = true });
            Console.WriteLine("  Launched.");
        }
        catch
        {
            // non-fatal
        }
    }
}
