using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace SnippingTool.Core;

/// <summary>
/// Ties the ffmpeg encoder (and thus the webcam it holds) to the lifetime of THIS process,
/// so the camera is released even when Shutio.exe is hard-killed from Task Manager.
///
/// Two complementary mechanisms:
///  1. A Windows job object with KILL_ON_JOB_CLOSE: the OS kills every assigned process the
///     moment the job handle closes. Used where the OS allows job-object configuration.
///  2. A "guardian" helper: a hidden second copy of Shutio.exe that waits on OUR process
///     object. The moment our process object is destroyed (normal exit, crash, or a hard
///     kill), the guardian kills the encoder. It only uses the most basic Win32 operations
///     (open process / wait / terminate), so it also works on locked-down machines where
///     job-object information settings are policy-blocked (this API can be refused there).
/// </summary>
public static class ProcessGuard
{
    private static readonly object _gate = new();
    private static IntPtr _job;          // zero until created; deliberately never closed
    private static bool _jobAttempted;
    private static Process? _guardian;   // the live guardian helper, if any

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpAttributes, string? lpJobName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInformationClass, IntPtr lpJobObjectInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;
    // JOBOBJECT_EXTENDED_LIMIT_INFORMATION on x64 = 128 bytes:
    //   JOBOBJECT_BASIC_LIMIT_INFORMATION (72)
    // + JOBOBJECT_ENDPOINT_NOTIFICATION  (16 — TWO handles)
    // + 5 * SIZE_T control-flow counters (40).
    // We only set LimitFlags (offset 16); the rest must be present but stays zero.
    // (A wrong size makes SetInformationJobObject fail and the job silently loses the flag.)
    private const int ExtendedLimitSize = 128;
    private const int LimitFlagsOffset = 16;

    /// <summary>
    /// Protects <paramref name="child"/> so it dies with THIS process, however this process
    /// dies. Best-effort: if every mechanism is refused (rare, policy-locked sessions) the
    /// app still works as before — the graceful exit paths kill the encoder themselves.
    /// </summary>
    public static void Protect(Process child)
    {
        try
        {
            EnsureJob();
            if (_job != IntPtr.Zero)
            {
                if (AssignProcessToJobObject(_job, child.Handle))
                    Logger.Info($"Encoder (pid {child.Id}) added to the kill-on-exit job.");
                else
                    Logger.Warn($"Could not add pid {child.Id} to the kill-on-exit job (Win32 {Marshal.GetLastWin32Error()}).");
            }
            StartGuardian(child);
        }
        catch (Exception ex)
        {
            Logger.Warn("ProcessGuard could not protect the process", ex);
        }
    }

    /// <summary>
    /// Releases the guardian after a recording ended normally. The encoder is already gone,
    /// so the guardian would exit on its own — this just closes it out deterministically.
    /// </summary>
    public static void ReleaseGuardian()
    {
        Process? g;
        lock (_gate) { g = _guardian; _guardian = null; }
        if (g == null) return;
        try { if (!g.HasExited) g.Kill(); } catch { }
        try { g.Dispose(); } catch { }
    }

    private static void EnsureJob()
    {
        lock (_gate)
        {
            if (_job != IntPtr.Zero || _jobAttempted) return;
            _jobAttempted = true;
            IntPtr job = CreateJobObjectW(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
            {
                Logger.Warn($"Could not create the process job object (Win32 {Marshal.GetLastWin32Error()}).");
                return;
            }
            IntPtr info = Marshal.AllocHGlobal(ExtendedLimitSize);
            try
            {
                Marshal.WriteInt32(info, LimitFlagsOffset, (int)JobObjectLimitKillOnJobClose);
                if (SetInformationJobObject(job, JobObjectExtendedLimitInformation, info))
                {
                    _job = job;      // keep it open forever — that is the whole point
                    Logger.Info("Kill-on-exit job object is active.");
                }
                else
                {
                    CloseHandle(job);
                    Logger.Warn($"Could not set KILL_ON_JOB_CLOSE on the process job (Win32 {Marshal.GetLastWin32Error()}); the guardian helper covers this machine.");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(info);
            }
        }
    }

    private static void StartGuardian(Process child)
    {
        string exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            Logger.Warn("Guardian: could not resolve our own executable path; skipping the guardian.");
            return;
        }
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        psi.ArgumentList.Add("--guardian");
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        psi.ArgumentList.Add(child.Id.ToString());
        var g = Process.Start(psi);
        if (g == null)
        {
            Logger.Warn("Guardian: failed to start.");
            return;
        }
        lock (_gate)
        {
            try { if (_guardian != null && !_guardian.HasExited) _guardian.Kill(); } catch { }
            _guardian = g;
        }
        Logger.Info($"Guardian started (pid {g.Id}); it will release the encoder (pid {child.Id}) if this app dies.");
    }

    // ===================== the guardian side (runs in the hidden helper process) =====================

    /// <summary>
    /// The guardian loop, executed by the hidden helper process: blocks until the APP
    /// (<paramref name="appPid"/>) dies — by any means, a hard kill included — then kills
    /// the ENCODER (<paramref name="childPid"/>), releasing the camera. If the encoder already
    /// exited (normal stop) the kill is a harmless no-op, and the app's stop path closes the
    /// guardian out explicitly anyway.
    /// </summary>
    public static int RunGuardianLoop(int appPid, int childPid)
    {
        Logger.Info($"Guardian: watching app pid={appPid}, encoder pid={childPid}.");
        Process? app = null;
        Process? child = null;
        try
        {
            app = OpenWithRetry(appPid);
            child = OpenWithRetry(childPid);
            if (app == null)
            {
                Logger.Info("Guardian: the app is already gone -> releasing the encoder if it is still alive.");
                KillChildQuietly(childPid);
                return 0;
            }
            if (child == null)
            {
                Logger.Info("Guardian: the encoder is already gone; releasing.");
                return 0;
            }

            // Block until the app's process object is destroyed. That is the single moment we
            // act: kill the encoder so the webcam is not left grabbed.
            app.WaitForExit();
            Logger.Info($"Guardian: the app (pid {appPid}) died -> killing the encoder (pid {childPid}) to release the camera.");
            if (!child.HasExited)
            {
                try { child.Kill(); } catch { }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Guardian: loop error", ex);
        }
        finally
        {
            try { app?.Dispose(); } catch { }
            try { child?.Dispose(); } catch { }
        }
        return 0;
    }

    private static void KillChildQuietly(int childPid)
    {
        try
        {
            using var p = Process.GetProcessById(childPid);
            if (!p.HasExited) p.Kill();
        }
        catch { }
    }

    private static Process? OpenWithRetry(int pid, int tries = 20)
    {
        for (int i = 0; i < tries; i++)
        {
            try
            {
                var p = Process.GetProcessById(pid);
                _ = p.HasExited;    // force the handle open and confirm the process exists
                return p;
            }
            catch
            {
                Thread.Sleep(150);
            }
        }
        return null;
    }
}
