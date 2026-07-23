using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CoderCommander.Services;

/// <summary>
/// Предоставляет методы для логирования сообщений в файл с ротацией по дате.
/// Provides methods for logging messages to a date-rotated file.
/// </summary>
public static class LogService
{
    private static string _logDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoderCommander", "logs");
    private static string _logPath = "";
    private static string _currentDateKey = "";

    /// <summary>
    /// Возвращает путь к лог-файлу на текущую дату; кэширует его до смены дня.
    /// Returns the path to the log file for the current date; caches it until the day changes.
    /// </summary>
    private static string LogPath
    {
        get
        {
            lock (_lock)
            {
                var key = DateTime.Now.ToString("yyyyMMdd");
                if (key != _currentDateKey)
                {
                    _currentDateKey = key;
                    _logPath = Path.Combine(_logDir, $"codercommander_{key}.log");
                }
                return _logPath;
            }
        }
    }

    private static readonly object _lock = new();
    private static bool _initialized;

    /// <summary>
    /// Статический конструктор; инициализирует директорию для логов.
    /// Static constructor; initializes the log directory.
    /// </summary>
    static LogService()
    {
        Initialize();
    }

    /// <summary>
    /// Создаёт директорию для логов при первом вызове.
    /// Creates the log directory on first invocation.
    /// </summary>
    private static void Initialize()
    {
        if (_initialized) return;
        try
        {
            if (!Directory.Exists(_logDir))
                Directory.CreateDirectory(_logDir);
            CleanupOldLogs();
            _initialized = true;
        }
        catch { }
    }

    /// <summary>
    /// Удаляет лог-файлы старше указанного количества дней.
    /// Deletes log files older than the specified number of days.
    /// </summary>
    /// <param name="maxDays">Максимальный возраст лог-файла в днях. / Max log file age in days.</param>
    public static void CleanupOldLogs(int maxDays = 30)
    {
        try
        {
            if (!Directory.Exists(_logDir)) return;
            var cutoff = DateTime.Now.AddDays(-maxDays);
            foreach (var file in Directory.EnumerateFiles(_logDir, "codercommander_*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// Записывает отладочное сообщение в лог.
    /// Writes a debug message to the log.
    /// </summary>
    /// <param name="message">Текст сообщения. / Message text.</param>
    /// <param name="source">Имя источника (например, имя класса). / Source name (e.g. class name).</param>
    public static void Debug(string message, string? source = null)
        => Write(LogLevel.Debug, message, source);

    /// <summary>
    /// Записывает информационное сообщение в лог.
    /// Writes an informational message to the log.
    /// </summary>
    /// <param name="message">Текст сообщения. / Message text.</param>
    /// <param name="source">Имя источника. / Source name.</param>
    public static void Info(string message, string? source = null)
        => Write(LogLevel.Info, message, source);

    /// <summary>
    /// Записывает предупреждение в лог.
    /// Writes a warning message to the log.
    /// </summary>
    /// <param name="message">Текст предупреждения. / Warning text.</param>
    /// <param name="source">Имя источника. / Source name.</param>
    public static void Warn(string message, string? source = null)
        => Write(LogLevel.Warn, message, source);

    /// <summary>
    /// Записывает сообщение об ошибке (с опциональным исключением) в лог.
    /// Writes an error message (with optional exception) to the log.
    /// </summary>
    /// <param name="message">Текст ошибки. / Error text.</param>
    /// <param name="source">Имя источника. / Source name.</param>
    /// <param name="ex">Исключение (будет сериализовано в сообщение). / Exception (will be serialized into the message).</param>
    public static void Error(string message, string? source = null, Exception? ex = null)
        => Write(LogLevel.Error, message + (ex != null ? $"\n{ex}" : ""), source);

    /// <summary>
    /// Форматирует и записывает строку лога в файл.
    /// Formats and writes a log line to the file.
    /// </summary>
    /// <param name="level">Уровень логирования. / Log level.</param>
    /// <param name="message">Текст сообщения. / Message text.</param>
    /// <param name="source">Имя источника (может быть null). / Source name (may be null).</param>
    private static void Write(LogLevel level, string message, string? source)
    {
        if (!_initialized) Initialize();
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            sb.Append(' ');
            sb.Append(level.ToString().ToUpperInvariant());
            if (source != null) { sb.Append(" ["); sb.Append(source); sb.Append(']'); }
            sb.Append(": ");
            sb.Append(message);
            sb.AppendLine();

            var path = LogPath;
            lock (_lock)
            {
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
        }
        catch { }
    }

    /// <summary>
    /// Уровни логирования.
    /// Logging levels.
    /// </summary>
    private enum LogLevel { Debug, Info, Warn, Error }
}