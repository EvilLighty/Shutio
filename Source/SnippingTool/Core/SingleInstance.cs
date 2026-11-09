namespace SnippingTool.Core;

public static class SingleInstance
{
    private static Mutex? _mutex;
    private static uint _activateMessage;

    public static uint ActivateMessage => _activateMessage;

    public static bool TryAcquire()
    {
        try
        {
            _activateMessage = NativeMethods.RegisterWindowMessage("SalsaNOW_SnippingTool_Activate_v1");
            _mutex = new Mutex(true, @"Local\SalsaNOW.Shutio.SingleInstance.v1", out bool created);
            if (created)
            {
                Logger.Info("Single-instance mutex acquired.");
                return true;
            }
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        catch (Exception ex)
        {
            Logger.Warn("Single-instance check failed; continuing", ex);
            return true;
        }
    }

    public static void SignalExistingInstance()
    {
        try
        {
            if (_activateMessage != 0)
            {
                NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, (int)_activateMessage, IntPtr.Zero, IntPtr.Zero);
                Logger.Info("Signalled the running instance.");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not signal existing instance", ex);
        }
    }
}
