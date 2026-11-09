using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ShutioUninstaller;

/// <summary>
/// Standalone, per-user, no-admin uninstaller for the portable Shutio.
///
/// Finds the installed app (via the "Apps &amp; features" registry entry, or next to
/// itself, or a path you pass) and removes the app files, the Start Menu + Desktop
/// shortcuts, and the registry entry. Your snips/recordings in Data\ are kept unless
/// you pass --remove-data.
///
/// Usage:
///   UninstallShutio.exe                  -> prompt to confirm, then remove (keeps Data)
///   UninstallShutio.exe "C:\Wherever"    -> uninstall the copy at that path
///   UninstallShutio.exe --yes            -> no confirmation prompt
///   UninstallShutio.exe --remove-data    -> also delete Data\ (snips / recordings)
///   UninstallShutio.exe --help
/// </summary>
internal static class Program
{
    private const string AppName = "Shutio";
    private const string ExeName = "Shutio.exe";
    private const string RegPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Shutio";

    [STAThread]
    private static int Main(string[] args)
    {
        bool      skipConfirm = false;
        bool      removeData  = false;
        bool      silent      = false;
        string?   destArg     = null;

        foreach (string a in args)
        {
            string s = a.Trim();
            if (s.Length > 1 && (s[0] == '-' || s[0] == '/'))
            {
                switch (s.TrimStart('-', '/').ToLowerInvariant())
                {
                    case "yes":
                    case "y":
                    case "force":
                    case "skip-confirm":
                        skipConfirm = true;
                        break;
                    case "remove-data":
                    case "removedata":
                        removeData = true;
                        break;
                    case "silent":
                    case "quiet":
                        silent = true;
                        break;
                    case "help":
                    case "h":
                    case "?":
                        PrintHelp();
                        return 0;
                }
            }
            else if (destArg == null)
            {
                destArg = s;
            }
        }

        string? dest = destArg;
        if (string.IsNullOrWhiteSpace(dest))
            dest = FindInstallLocation();

        if (string.IsNullOrWhiteSpace(dest) || !File.Exists(Path.Combine(dest, ExeName)))
        {
            if (!silent)
                MessageBox.Show(
                    $"Could not find an installed {AppName}.\n\n" +
                    $"Looked here: {dest ?? "(unknown)"}\n\n" +
                    $"If you know the folder, run:\nUninstallShutio.exe \"<folder>\"",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }

        if (!skipConfirm && !silent)
        {
            DialogResult r = MessageBox.Show(
                $"Uninstall the {AppName} installed at:\n\n{dest}\n\n" +
                (removeData
                    ? "Your snips and recordings in Data\\ will also be deleted."
                    : "Your snips and recordings in Data\\ will be KEPT (delete them separately if you want).") +
                "\n\nClick Yes to uninstall, or No to cancel.",
                AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return 0;
        }

        CloseRunningApp();

        // Shortcuts.
        RemoveShortcut(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs\Shutio.lnk"));
        RemoveShortcut(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Shutio.lnk"));

        // Registry (Apps & features entry).
        try { Registry.CurrentUser.DeleteSubKeyTree(RegPath, throwOnMissingSubKey: false); }
        catch { /* ignore */ }

        // Files.
        if (removeData)
        {
            DeleteAll(dest);
        }
        else
        {
            foreach (string item in Directory.EnumerateFileSystemEntries(dest))
            {
                if (Path.GetFileName(item).Equals("Data", StringComparison.OrdinalIgnoreCase)) continue;
                DeleteAll(item);
            }
            try { if (!Directory.EnumerateFileSystemEntries(dest).Any()) Directory.Delete(dest); } catch { }
        }

        if (!silent)
            MessageBox.Show($"The {AppName} was removed.\n", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }

    private static void PrintHelp()
    {
        MessageBox.Show(
            "Usage:\n" +
            "  UninstallShutio.exe                  prompt to confirm, then remove (keeps Data)\n" +
            "  UninstallShutio.exe \"C:\\Wherever\" uninstall that folder\n" +
            "  UninstallShutio.exe --yes            no confirmation prompt\n" +
            "  UninstallShutio.exe --remove-data    also delete Data\\ (snips / recordings)\n" +
            "  UninstallShutio.exe --silent         no dialogs at all (for automation)\n",
            AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string? FindInstallLocation()
    {
        // 1) The "Apps & features" entry we wrote at install time.
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegPath);
            string? loc = key?.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(loc) && File.Exists(Path.Combine(loc, ExeName))) return loc;
        }
        catch { }

        // 2) Next to this executable (in case it was dropped into the install folder).
        string beside = new DirectoryInfo(AppContext.BaseDirectory).FullName.TrimEnd('\\', '/');
        if (File.Exists(Path.Combine(beside, ExeName))) return beside;

        // 3) Common default locations.
        foreach (string c in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Shutio"),
            @"I:\Shutio",
            @"I:\Apps\Shutio",
            @"C:\Program Files\Shutio",
        })
        {
            if (File.Exists(Path.Combine(c, ExeName))) return c;
        }
        return null;
    }

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
    }

    private static void RemoveShortcut(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void DeleteAll(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                foreach (string d in Directory.EnumerateDirectories(path)) DeleteAll(d);
                foreach (string f in Directory.EnumerateFiles(path))
                {
                    try { File.Delete(f); } catch { }
                }
                try { Directory.Delete(path); } catch { }
            }
            else
            {
                try { File.Delete(path); } catch { }
            }
        }
        catch { }
    }
}
