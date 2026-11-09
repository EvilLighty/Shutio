using System.Text;

namespace SnippingTool.Core;

public static class AppPaths
{
    public static string Root { get; } = AppContext.BaseDirectory.TrimEnd('\\');
    public static string Data { get; } = Path.Combine(Root, "Data");
    public static string Logs { get; } = Path.Combine(Data, "Logs");
    public static string Screenshots { get; } = Path.Combine(Data, "Screenshots");
    public static string Recordings { get; } = Path.Combine(Data, "Recordings");
    public static string Thumbnails { get; } = Path.Combine(Data, "Thumbnails");
    public static string Tests { get; } = Path.Combine(Data, "Tests");
    public static string SettingsFile { get; } = Path.Combine(Data, "settings.json");
    public static string HistoryFile { get; } = Path.Combine(Data, "history.json");
    public static string Tools { get; } = Path.Combine(Root, "Tools");

    public static void EnsureDirectories()
    {
        foreach (var dir in new[] { Data, Logs, Screenshots, Recordings, Thumbnails, Tests })
        {
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex) { Logger.Error($"Unable to create directory {dir}", ex); }
        }
    }
}

public static class Logger
{
    private static readonly object Gate = new();
    private static string _file = "";
    private static bool _initialized;

    public static string CurrentLogFile => _file;

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Logs);
            _file = Path.Combine(AppPaths.Logs, $"app-{DateTime.Now:yyyy-MM-dd}.log");
            _initialized = true;
            Cleanup();
            Info($"===== Shutio starting (v{typeof(Logger).Assembly.GetName().Version}) =====");
            Info($"OS: {Environment.OSVersion}; 64-bit process: {Environment.Is64BitProcess}; user: {Environment.UserName}");
            Info($"Root: {AppPaths.Root}");
        }
        catch
        {
            _initialized = false;
        }
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);
    public static void Debug(string message) => Write("DEBUG", message, null);

    private static void Write(string level, string message, Exception? ex)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        if (ex != null) line += Environment.NewLine + ex;
        try
        {
            lock (Gate)
            {
                if (!_initialized || string.IsNullOrEmpty(_file))
                {
                    Directory.CreateDirectory(AppPaths.Logs);
                    _file = Path.Combine(AppPaths.Logs, $"app-{DateTime.Now:yyyy-MM-dd}.log");
                    _initialized = true;
                }
                File.AppendAllText(_file, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    private static void Cleanup()
    {
        try
        {
            foreach (var file in Directory.GetFiles(AppPaths.Logs, "app-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14))
                    File.Delete(file);
            }
        }
        catch { }
    }
}
