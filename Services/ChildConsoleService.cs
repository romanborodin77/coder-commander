using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoderCommander.Services;

/// <summary>
/// Запускает настоящий интерактивный shell (cmd.exe / powershell.exe) как отдельную
/// консоль и встраивает её окно внутрь нашего WPF-окна через Win32 SetParent + SetWindowPos.
/// Даёт 100% нативный терминал: стрелки, цвета, история, корректный Ctrl+C — без эмуляции.
/// Launches a real interactive shell (cmd.exe / powershell.exe) as a separate
/// console and embeds its window inside the WPF host via Win32 SetParent + SetWindowPos.
/// Provides a 100% native terminal: arrows, colors, history, proper Ctrl+C — no emulation.
/// </summary>
public sealed class ChildConsoleService : IDisposable
{
    private PROCESS_INFORMATION _pi;
    private IntPtr _consoleWindowHandle;
    private bool _disposed;
    private bool _isRunning;
    private int _processId;
    private IntPtr _hostHwnd = IntPtr.Zero;    private string _shellType = "cmd"; // "cmd" или "powershell" - используется в ChangeDirectory

    /// <summary>
    /// Возвращает true, если консольный процесс запущен и работает.
    /// Returns true if the console process is started and running.
    /// </summary>
    public bool IsRunning => _isRunning;
    /// <summary>
    /// Возвращает идентификатор запущенного консольного процесса (PID).
    /// Returns the process ID (PID) of the running console process.
    /// </summary>
    public int ProcessId => _processId;

    // ---- Win32 P/Invoke ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    private const int STARTF_USESHOWWINDOW = 0x01;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int CREATE_NEW_CONSOLE = 0x00000010;
    private const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const int WM_CLOSE = 0x0010;
    private const int WM_CHAR = 0x0102;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int ERROR_CLASS_ALREADY_REGISTERED = 1410;
    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out int lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SWP_SHOWWINDOW = 0x0040;
    private const int SWP_FRAMECHANGED = 0x0020;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // ---- Публичное API ----

    /// <summary>
    /// Запускает shell (cmd/powershell) в отдельной консоли, встраивает окно в hostHwnd через Win32 SetParent.
    /// Launches a shell (cmd/powershell) in a separate console and embeds its window into hostHwnd via Win32 SetParent.
    /// </summary>
    /// <param name="shellExe">Имя исполняемого файла: "cmd" или "powershell". Shell executable name: "cmd" or "powershell".</param>
    /// <param name="workingDir">Рабочая директория для запущенного shell. Working directory for the launched shell.</param>
    /// <param name="hostHwnd">HWND контейнера WPF, в который встраивается окно консоли. WPF container HWND to embed the console window into.</param>
    /// <returns>Task, завершающийся после встраивания окна консоли. Task that completes after the console window is embedded.</returns>
    /// <exception cref="InvalidOperationException">Терминал уже запущен / Terminal is already running.</exception>
    /// <exception cref="ArgumentException">hostHwnd равен IntPtr.Zero / hostHwnd is IntPtr.Zero.</exception>
    /// <exception cref="Win32Exception">CreateProcessW не удался / CreateProcessW failed.</exception>
    public async Task StartAsync(string shellExe, string workingDir, IntPtr hostHwnd)
    {
        if (_isRunning) throw new InvalidOperationException("Терминал уже запущен.");
        if (hostHwnd == IntPtr.Zero) throw new ArgumentException("HWND контейнера не может быть нулевым.");

        _hostHwnd = hostHwnd;
        // Определяем тип shell для корректной смены каталога: powershell/pwsh → "powershell", иначе → "cmd"
        _shellType = shellExe.Equals("powershell", StringComparison.OrdinalIgnoreCase)
                  || shellExe.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
            ? "powershell" : "cmd";

        // Рабочая директория
        if (!System.IO.Directory.Exists(workingDir))
            workingDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Получаем список существующих консольных окон ДО запуска
        var existingConsoles = GetConsoleWindows();

        // Собираем командную строку (запас вместимости для CreateProcessW)
        var commandLine = new StringBuilder(1024);
        if (shellExe.Equals("powershell", StringComparison.OrdinalIgnoreCase))
            commandLine.Append("powershell.exe -NoExit -Command \"Set-Location '" + workingDir.Replace("'", "''") + "'\"");
        else
            // Используем cd /d без внешних кавычек — cmd.exe сам парсит путь с пробелами
            commandLine.Append("cmd.exe /K cd /d \"" + workingDir + "\"");

        var si = new STARTUPINFO
        {
            cb = Marshal.SizeOf<STARTUPINFO>(),
            dwFlags = STARTF_USESHOWWINDOW,
            wShowWindow = SW_SHOW  // Показываем окно консоли
        };

        if (!CreateProcessW(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CREATE_NEW_CONSOLE | CREATE_UNICODE_ENVIRONMENT,
                IntPtr.Zero,
                null,
                ref si,
                out _pi))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err, $"CreateProcessW не удался (ошибка {err}) для {shellExe}");
        }

        LogService.Debug($"ChildConsoleService.StartAsync: process started pid={_pi.dwProcessId}", "Terminal");
        _processId = _pi.dwProcessId;
        _isRunning = true;

        try
        {
            // Поиск окна консоли: сначала по классу окна (ConsoleWindowClass),
            // затем fallback через AttachConsole+GetConsoleWindow (Windows Terminal на Win11)
            _consoleWindowHandle = await WaitForNewConsoleWindowAsync(existingConsoles, TimeSpan.FromSeconds(10));
            if (_consoleWindowHandle == IntPtr.Zero)
            {
                LogService.Debug("StartAsync: class-window search timed out, trying AttachConsole fallback", "Terminal");
                _consoleWindowHandle = FindConsoleViaAttach(_pi.dwProcessId);
            }

            if (_consoleWindowHandle == IntPtr.Zero)
            {
                await StopAsync(force: true);
                throw new InvalidOperationException("Не удалось найти окно консоли после запуска процесса.");
            }

            LogService.Debug($"ChildConsoleService.StartAsync: console hwnd={_consoleWindowHandle:X}, reparenting to {hostHwnd:X}", "Terminal");

            // Убираем рамку окна (заголовок, изменение размера) — будет управлять WPF
            RemoveWindowFrame(_consoleWindowHandle);

            // Встраиваем в hostHwnd
            SetParent(_consoleWindowHandle, hostHwnd);

            // Получаем реальный размер контейнера и выставляем консоль в полный размер
            if (GetClientRect(hostHwnd, out var rect))
            {
                var w = Math.Max(rect.Right - rect.Left, 1);
                var h = Math.Max(rect.Bottom - rect.Top, 1);
                MoveWindow(_consoleWindowHandle, 0, 0, w, h, true);
                SetWindowPos(_consoleWindowHandle, IntPtr.Zero, 0, 0, w, h,
                    SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
            else
            {
                MoveWindow(_consoleWindowHandle, 0, 0, 1, 1, true);
            }
        }
        catch
        {
            await StopAsync(force: true);
            throw;
        }
    }

    /// <summary>
    /// Останавливает консоль: сначала отправляет WM_CLOSE, при неудаче принудительно завершает процесс.
    /// Stops the console: first sends WM_CLOSE, then forcefully terminates the process if needed.
    /// </summary>
    /// <param name="force">Если true — сразу принудительное завершение без WM_CLOSE. If true — immediate forceful termination without WM_CLOSE.</param>
    /// <returns>Task, завершающийся после остановки процесса. Task that completes after the process is stopped.</returns>
    public async Task StopAsync(bool force = false)
    {
        if (!_isRunning && _consoleWindowHandle == IntPtr.Zero) return;

        try
        {
            if (_consoleWindowHandle != IntPtr.Zero && !force)
            {
                PostMessage(_consoleWindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                await Task.Delay(800).ConfigureAwait(false);

                // Проверяем, жив ли процесс
                if (_pi.hProcess != IntPtr.Zero &&
                    GetExitCodeProcess(_pi.hProcess, out var ec) && ec == 259)
                {
                    // Всё ещё жив — убиваем принудительно
                    TryKillProcessTree();
                }
            }
            else
            {
                TryKillProcessTree();
            }
        }
        catch { TryKillProcessTree(); }
        finally
        {
            CleanupHandles();
            _consoleWindowHandle = IntPtr.Zero;
            _hostHwnd = IntPtr.Zero;
            _isRunning = false;            _shellType = "cmd";
        }
    }

    /// <summary>
    /// Отправляет текст в консоль через WM_CHAR, имитируя ввод с клавиатуры.
    /// Sends text to the console via WM_CHAR, simulating keyboard input.
    /// </summary>
    /// <param name="text">Текст для отправки в консоль. Text to send to the console.</param>
    public void SendInput(string text)
    {
        if (_consoleWindowHandle == IntPtr.Zero || string.IsNullOrEmpty(text)) return;
        foreach (var c in text)
        {
            PostMessage(_consoleWindowHandle, WM_CHAR, (IntPtr)c, IntPtr.Zero);
            Thread.Sleep(1);
        }
    }

    /// <summary>
    /// Отправляет нажатие клавиши Enter (WM_KEYDOWN + WM_KEYUP) в окно консоли.
    /// Sends an Enter key press (WM_KEYDOWN + WM_KEYUP) to the console window.
    /// </summary>
    public void SendEnter()
    {
        if (_consoleWindowHandle == IntPtr.Zero) return;
        PostMessage(_consoleWindowHandle, WM_KEYDOWN, (IntPtr)0x0D, IntPtr.Zero);
        PostMessage(_consoleWindowHandle, WM_KEYUP, (IntPtr)0x0D, IntPtr.Zero);
    }

    /// <summary>
    /// Передаёт фокус клавиатуры окну консоли через BringWindowToTop + SetFocus.
    /// Transfers keyboard focus to the console window via BringWindowToTop + SetFocus.
    /// </summary>
    public void Focus()
    {
        if (_consoleWindowHandle != IntPtr.Zero)
        {
            BringWindowToTop(_consoleWindowHandle);
            SetFocus(_consoleWindowHandle);
        }
    }

    /// <summary>
    /// Меняет размер окна консоли (вызывается при изменении размера контейнера WPF).
    /// Resizes the console window (called when the WPF container is resized).
    /// </summary>
    /// <param name="width">Новая ширина окна в пикселях. New window width in pixels.</param>
    /// <param name="height">Новая высота окна в пикселях. New window height in pixels.</param>
    public void Resize(int width, int height)
    {
        if (_consoleWindowHandle == IntPtr.Zero || width <= 0 || height <= 0) return;
        MoveWindow(_consoleWindowHandle, 0, 0, width, height, true);
    }

    /// <summary>
    /// Меняет рабочую директорию shell, отправляя команду cd с учётом типа оболочки.
    /// Changes the shell working directory by sending a cd command with shell-specific escaping.
    /// </summary>
    /// <param name="path">Абсолютный путь к новой рабочей директории. Absolute path to the new working directory.</param>
    public void ChangeDirectory(string path)
    {
        if (_consoleWindowHandle == IntPtr.Zero || !System.IO.Directory.Exists(path)) return;

        // Корректная смена каталога: для PowerShell используем Set-Location с одинарными кавычками,
        // для cmd — cd /d с двойными кавычками. Экранируем одинарные кавычки в пути для PS ('→'').
        // Correct directory change: PowerShell uses Set-Location with single quotes,
        // cmd uses cd /d with double quotes. Escape single quotes in path for PS ('→'').
        if (_shellType == "powershell")
        {
            var escaped = path.Replace("'", "''");
            SendInput($"Set-Location -LiteralPath '{escaped}'\r");
        }
        else
        {
            SendInput($"cd /d \"{path}\"\r");
        }
    }

    /// <summary>
    /// Освобождает ресурсы: останавливает консольный процесс и закрывает дескрипторы.
    /// Releases resources: stops the console process and closes handles.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Task.Run(async () => await StopAsync(force: true)).Wait(TimeSpan.FromSeconds(5)); } catch { }
        GC.SuppressFinalize(this);
    }

    // ---- Приватные методы ----

    private void TryKillProcessTree()
    {
        try
        {
            if (_pi.hProcess != IntPtr.Zero)
            {
                var proc = Process.GetProcessById(_processId);
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(2000);
                }
            }
        }
        catch { }
    }

    private void CleanupHandles()
    {
        if (_pi.hThread != IntPtr.Zero) { CloseHandle(_pi.hThread); _pi.hThread = IntPtr.Zero; }
        if (_pi.hProcess != IntPtr.Zero) { CloseHandle(_pi.hProcess); _pi.hProcess = IntPtr.Zero; }
        _processId = 0;
    }

    private static void RemoveWindowFrame(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        var style = GetWindowLong(hWnd, GWL_STYLE);
        style &= ~WS_CAPTION;
        style &= ~WS_THICKFRAME;
        style |= WS_POPUP;
        SetWindowLong(hWnd, GWL_STYLE, style);
        SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
    }

    private static List<IntPtr> GetConsoleWindows()
    {
        var result = new List<IntPtr>();
        EnumWindows((hWnd, _) =>
        {
            var cls = new StringBuilder(256);
            GetClassName(hWnd, cls, cls.Capacity);
            var name = cls.ToString();
            // Классическая консоль Windows 10 и Windows Terminal (Win11)
            if (name == "ConsoleWindowClass" || name == "CASCADIA_HOSTING_WINDOW_CLASS")
                result.Add(hWnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>
    /// Fallback-поиск HWND консоли через AttachConsole + GetConsoleWindow.
    /// Работает, когда консольное окно не имеет класса ConsoleWindowClass (Windows Terminal).
    /// </summary>
    private static IntPtr FindConsoleViaAttach(int processId)
    {
        try
        {
            if (AttachConsole(processId))
            {
                var hwnd = GetConsoleWindow();
                FreeConsole();
                if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
                {
                    LogService.Debug($"FindConsoleViaAttach: found hwnd={hwnd:X} for pid={processId}", "Term");
                    return hwnd;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"FindConsoleViaAttach: exception {ex.Message}", "Term");
        }
        return IntPtr.Zero;
    }

    private static async Task<IntPtr> WaitForNewConsoleWindowAsync(List<IntPtr> before, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        var beforeSet = new HashSet<IntPtr>(before);
        while (sw.Elapsed < timeout)
        {
            var after = GetConsoleWindows();
            foreach (var hwnd in after)
            {
                if (!beforeSet.Contains(hwnd))
                {
                    var visible = IsWindowVisible(hwnd);
                    var cls = new StringBuilder(256);
                    GetClassName(hwnd, cls, cls.Capacity);
                    LogService.Debug($"WaitForNewConsoleWindowAsync: found new hwnd={hwnd:X} class={cls} visible={visible}", "Term");
                    if (visible) return hwnd;
                }
            }
            await Task.Delay(100).ConfigureAwait(false);
        }
        LogService.Debug($"WaitForNewConsoleWindowAsync: timeout after {timeout.TotalSeconds}s, checked {GetConsoleWindows().Count} windows", "Term");
        return IntPtr.Zero;
    }
}