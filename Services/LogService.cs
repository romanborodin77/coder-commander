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

    /// <summary>app.log is rotated to app.log.old once it passes this size, instead of growing
    /// without bound for the lifetime of the AppData folder.</summary>
    private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB

    /// <summary>Returns the full path to the application log file.</summary>
    /// <returns>Absolute path to <c>app.log</c> in the user's AppData directory.</returns>
    public static string GetLogPath() => LogPath;

    /// <summary>Deletes the log file and starts a fresh log session.</summary>
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

    /// <summary>Logs a debug-level message.</summary>
    /// <param name="message">The message to log.</param>
    /// <param name="category">Optional category label (e.g. <c>"UI"</c>, <c>"Nav"</c>).</param>
    public static void Debug(string message, string category = "")
        => Write("DEBUG", message, category, null);

    /// <summary>Logs an informational message.</summary>
    /// <param name="message">The message to log.</param>
    /// <param name="category">Optional category label.</param>
    public static void Info(string message, string category = "")
        => Write("INFO", message, category, null);

    /// <summary>Logs a warning-level message.</summary>
    /// <param name="message">The message to log.</param>
    /// <param name="category">Optional category label.</param>
    public static void Warning(string message, string category = "")
        => Write("WARN", message, category, null);

    /// <summary>Logs an error-level message with an optional exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="ex">Optional exception to include in the log entry.</param>
    /// <param name="category">Optional category label.</param>
    public static void Error(string message, Exception? ex = null, string category = "")
        => Write("ERROR", message, category, ex);

    /// <summary>Logs an error-level message with a required category and optional exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="category">Category label.</param>
    /// <param name="ex">Optional exception to include in the log entry.</param>
    public static void Error(string message, string category, Exception? ex = null)
        => Write("ERROR", message, category, ex);

    /// <summary>Logs a UI interaction event (button click, selection change, etc.).</summary>
    /// <param name="eventType">Type of UI event (e.g. <c>"Click"</c>, <c>"ValueChanged"</c>).</param>
    /// <param name="details">Additional details about the event.</param>
    /// <param name="control">Optional control name that triggered the event.</param>
    public static void LogUIEvent(string eventType, string details, string control = "")
    {
        var cat = string.IsNullOrEmpty(control) ? "UI" : $"UI.{control}";
        Info($"{eventType}: {details}", cat);
    }

    /// <summary>Logs a command execution result.</summary>
    /// <param name="commandId">The command identifier (e.g. <c>"cm_Copy"</c>).</param>
    /// <param name="result">Optional result description (e.g. <c>"ok"</c>, <c>"failed"</c>).</param>
    public static void LogCommand(string commandId, string result = "")
    {
        var msg = string.IsNullOrEmpty(result) ? commandId : $"{commandId} → {result}";
        Info(msg, "Command");
    }

    /// <summary>Logs a directory navigation event.</summary>
    /// <param name="path">The directory path navigated to.</param>
    /// <param name="panel">Optional panel identifier (e.g. <c>"Left"</c>, <c>"Right"</c>).</param>
    public static void LogNavigation(string path, string panel = "")
    {
        var cat = string.IsNullOrEmpty(panel) ? "Nav" : $"Nav.{panel}";
        Info($"Navigate: {path}", cat);
    }

    /// <summary>Logs a file system operation (copy, move, delete, etc.).</summary>
    /// <param name="operation">Operation name (e.g. <c>"Copy"</c>, <c>"Delete"</c>).</param>
    /// <param name="source">Source file or directory path.</param>
    /// <param name="target">Optional target path for operations that have a destination.</param>
    public static void LogFileOperation(string operation, string source, string? target = null)
    {
        var msg = target == null ? $"{operation}: {source}" : $"{operation}: {source} → {target}";
        Info(msg, "FileOp");
    }

    /// <summary>Logs a ViewModel property state change for debugging.</summary>
    /// <param name="property">Property name that changed.</param>
    /// <param name="value">Current value of the property.</param>
    /// <param name="viewModel">Optional ViewModel name.</param>
    public static void LogViewModelState(string property, object? value, string viewModel = "")
    {
        var cat = string.IsNullOrEmpty(viewModel) ? "State" : $"State.{viewModel}";
        Info($"{property} = {value}", cat);
    }

    /// <summary>Writes a formatted log entry to the log file.</summary>
    /// <param name="level">Log level (DEBUG, INFO, WARN, ERROR).</param>
    /// <param name="message">The log message.</param>
    /// <param name="category">Optional category label.</param>
    /// <param name="ex">Optional exception to append.</param>
    private static void Write(string level, string message, string category, Exception? ex)
    {
        try
        {
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                RotateIfTooLarge();

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

    /// <summary>Rotates app.log to app.log.old (overwriting any previous one) once it passes
    /// <see cref="MaxLogSizeBytes"/>. Must be called with <see cref="_lock"/> already held.</summary>
    private static void RotateIfTooLarge()
    {
        try
        {
            var info = new FileInfo(LogPath);
            if (!info.Exists || info.Length < MaxLogSizeBytes)
                return;

            var oldPath = LogPath + ".old";
            if (File.Exists(oldPath))
                File.Delete(oldPath);
            File.Move(LogPath, oldPath);
        }
        catch { /* best effort - a failed rotation shouldn't stop logging */ }
    }
}
