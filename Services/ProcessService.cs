using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CoderCommander.Services;

/// <summary>
/// Представляет результат выполнения внешнего процесса.
/// Represents the result of executing an external process.
/// </summary>
/// <param name="ExitCode">Код возврата процесса. / Process exit code.</param>
/// <param name="StdOut">Стандартный вывод (stdout). / Standard output (stdout).</param>
/// <param name="StdErr">Стандартный вывод ошибок (stderr). / Standard error output (stderr).</param>
public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    /// <summary>
    /// Возвращает true, если процесс завершился с кодом 0 (успех).
    /// Returns true if the process exited with code 0 (success).
    /// </summary>
    public bool Success => ExitCode == 0;
}

/// <summary>
/// Интерфейс для запуска внешних процессов.
/// Interface for launching external processes.
/// </summary>
public interface IProcessService
{
    /// <summary>
    /// Запускает процесс, где первый элемент argumentList — имя файла, остальные — аргументы.
    /// Runs a process where the first element of argumentList is the file name and the rest are arguments.
    /// </summary>
    /// <param name="argumentList">Список аргументов: [0] = имя файла, [1..] = аргументы. / Argument list: [0] = file name, [1..] = arguments.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения процесса. / Process execution result.</returns>
    Task<ProcessResult> RunAsync(string[] argumentList, CancellationToken ct = default);
    /// <summary>
    /// Запускает процесс с указанным файлом и набором аргументов.
    /// Runs a process with the specified file name and arguments.
    /// </summary>
    /// <param name="fileName">Путь к исполняемому файлу. / Path to the executable file.</param>
    /// <param name="arguments">Коллекция аргументов командной строки. / Collection of command-line arguments.</param>
    /// <param name="workingDirectory">Рабочий каталог (может быть null). / Working directory (may be null).</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения процесса. / Process execution result.</returns>
    Task<ProcessResult> RunAsync(string fileName, System.Collections.Generic.IEnumerable<string> arguments, string? workingDirectory = null, CancellationToken ct = default);
    /// <summary>
    /// Запускает команду через cmd.exe /c.
    /// Runs a command via cmd.exe /c.
    /// </summary>
    /// <param name="commandLine">Строка команды. / Command line string.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения процесса. / Process execution result.</returns>
    Task<ProcessResult> RunRawAsync(string commandLine, CancellationToken ct = default);
}

/// <summary>
/// Реализация IProcessService, запускающая внешние процессы с захватом вывода.
/// Implementation of IProcessService that launches external processes with output capture.
/// </summary>
public sealed class ProcessService : IProcessService
{
    /// <summary>
    /// Запускает процесс, используя массив аргументов (первый элемент — имя файла).
    /// Runs a process using an argument array (first element is the file name).
    /// </summary>
    /// <param name="argumentList">Массив: [0] = файл, [1..] = аргументы. / Array: [0] = file, [1..] = arguments.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения процесса. / Process execution result.</returns>
    public Task<ProcessResult> RunAsync(string[] argumentList, CancellationToken ct = default)
        => RunAsync(argumentList[0], argumentList.Skip(1).ToArray(), null, ct);

    /// <summary>
    /// Запускает процесс с указанным файлом, аргументами и рабочим каталогом.
    /// Runs a process with the specified file, arguments, and working directory.
    /// </summary>
    /// <param name="fileName">Путь к исполняемому файлу. / Path to the executable file.</param>
    /// <param name="arguments">Коллекция аргументов командной строки. / Collection of command-line arguments.</param>
    /// <param name="workingDirectory">Рабочий каталог (может быть null). / Working directory (may be null).</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения процесса. / Process execution result.</returns>
    public Task<ProcessResult> RunAsync(string fileName, System.Collections.Generic.IEnumerable<string> arguments, string? workingDirectory = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = string.Join(" ", arguments.Select(EscapeArg)),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? ""
        };
        return RunCoreAsync(psi, ct);
    }

    /// <summary>
    /// Запускает сырую команду через cmd.exe /c.
    /// Runs a raw command via cmd.exe /c.
    /// </summary>
    /// <param name="commandLine">Командная строка. / Command line string.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения процесса. / Process execution result.</returns>
    public Task<ProcessResult> RunRawAsync(string commandLine, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + commandLine,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        return RunCoreAsync(psi, ct);
    }

    /// <summary>
    /// Основной метод запуска процесса, ожидания завершения и сбора вывода.
    /// Core method to start the process, wait for exit, and collect output.
    /// </summary>
    /// <param name="psi">Параметры запуска процесса. / Process start info.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения процесса. / Process execution result.</returns>
    /// <exception cref="InvalidOperationException">Процесс не запустился. / The process failed to start.</exception>
    /// <exception cref="OperationCanceledException">Операция отменена; процесс убит. / Operation cancelled; the process was killed.</exception>
    private static async Task<ProcessResult> RunCoreAsync(ProcessStartInfo psi, CancellationToken ct)
    {
        psi.StandardOutputEncoding = System.Text.Encoding.UTF8;
        psi.StandardErrorEncoding = System.Text.Encoding.UTF8;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdOut = new System.Text.StringBuilder();
        var stdErr = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

        if (!process.Start())
            throw new InvalidOperationException($"Не удалось запустить процесс: {psi.FileName} {psi.Arguments}");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process.Id);
            throw;
        }

        return new ProcessResult(process.ExitCode, stdOut.ToString().TrimEnd(), stdErr.ToString().TrimEnd());
    }

    /// <summary>
    /// Принудительно завершает процесс и все его дочерние процессы через taskkill.
    /// Forcefully terminates the process and all its child processes via taskkill.
    /// </summary>
    /// <param name="pid">Идентификатор процесса. / Process ID.</param>
    private static void KillProcessTree(int pid)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/PID {pid} /T /F",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(2000);
        }
        catch { }
    }

    /// <summary>
    /// Экранирует аргумент для командной строки Windows (обратные слеши и кавычки).
    /// Escapes a command-line argument for Windows (backslashes and quotes).
    /// </summary>
    /// <param name="arg">Исходная строка аргумента. / Raw argument string.</param>
    /// <returns>Экранированная строка. / Escaped string.</returns>
    private static string EscapeArg(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "\"\"";
        if (!arg.Contains(' ') && !arg.Contains('"') && !arg.Contains('\\')
            && !arg.Contains('`') && !arg.Contains('$')) return arg;
        var sb = new System.Text.StringBuilder("\"");
        foreach (var c in arg)
        {
            if (c == '\\') sb.Append("\\\\");
            else if (c == '"') sb.Append("\\\"");
            else if (c == '`') sb.Append("``");
            else if (c == '$') sb.Append("`$");
            else sb.Append(c);
        }
        sb.Append('"');
        return sb.ToString();
    }
}