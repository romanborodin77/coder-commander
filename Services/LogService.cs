namespace CoderCommander.Services;

/// <summary>
/// File-based logging service with UI event tracking.
/// Logs are written to %APPDATA%/CoderCommander/app.log
/// </summary>
public static class LogService
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CoderCommander", "app.log");

    private static readonly object _lock = new();

    public static string GetLogPath() => LogPath;

    public static void ClearLog()
    {
        try
        {
            lock (_lock)
            {
                if (File.Exists(LogPath))
                    File.Delete(LogPath);
                Info("--- Log started ---", "System");
            }
        }
        catch { }
    }

    public static void Debug(string message, string category = "")
        => Write("DEBUG", message, category, null);

    public static void Info(string message, string category = "")
        => Write("INFO", message, category, null);

    public static void Warning(string message, string category = "")
        => Write("WARN", message, category, null);

    public static void Error(string message, Exception? ex = null, string category = "")
        => Write("ERROR", message, category, ex);

    public static void Error(string message, string category, Exception? ex = null)
        => Write("ERROR", message, category, ex);

    // UI Event logging helpers
    public static void LogUIEvent(string eventType, string details, string control = "")
    {
        var cat = string.IsNullOrEmpty(control) ? "UI" : $"UI.{control}";
        Info($"{eventType}: {details}", cat);
    }

    public static void LogCommand(string commandId, string result = "")
    {
        var msg = string.IsNullOrEmpty(result) ? commandId : $"{commandId} → {result}";
        Info(msg, "Command");
    }

    public static void LogNavigation(string path, string panel = "")
    {
        var cat = string.IsNullOrEmpty(panel) ? "Nav" : $"Nav.{panel}";
        Info($"Navigate: {path}", cat);
    }

    public static void LogFileOperation(string operation, string source, string? target = null)
    {
        var msg = target == null ? $"{operation}: {source}" : $"{operation}: {source} → {target}";
        Info(msg, "FileOp");
    }

    public static void LogViewModelState(string property, object? value, string viewModel = "")
    {
        var cat = string.IsNullOrEmpty(viewModel) ? "State" : $"State.{viewModel}";
        Info($"{property} = {value}", cat);
    }

    private static void Write(string level, string message, string category, Exception? ex)
    {
        try
        {
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}]";
                if (!string.IsNullOrEmpty(category))
                    line += $" [{category}]";
                line += $" {message}";
                if (ex != null)
                    line += $"\n  {ex}";

                File.AppendAllText(LogPath, line + "\n");
            }
        }
        catch { }
    }
}
