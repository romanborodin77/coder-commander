using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CoderCommander.Services;

namespace CoderCommander.Views;

/// <summary>
/// HwndHost, создающий пустое дочернее нативное окно-плейсхолдер.
/// Реальное окно запущенного shell встраивается в него через Win32 SetParent,
/// образуя нативный терминал внутри WPF-окна.
/// HwndHost that creates an empty child native placeholder window.
/// The actual shell window is embedded into it via Win32 SetParent,
/// forming a native terminal inside the WPF window.
/// </summary>
public sealed class TerminalHost : HwndHost, IDisposable
{
    /// <summary>
    /// Имя оконного класса Win32, регистрируемого для placeholder-окна.
    /// Win32 window class name registered for the placeholder window.
    /// </summary>
    private const string WindowClass = "CoderCommanderTerminalHost";

    /// <summary>
    /// Дескриптор созданного нативного окна-контейнера.
    /// Handle of the created native container window.
    /// </summary>
    private IntPtr _hwnd = IntPtr.Zero;

    /// <summary>
    /// Флаг, был ли объект уже освобожден.
    /// Flag indicating whether the object has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// HWND этого контейнера (куда встраивать консоль).
    /// The container HWND (where to embed the console).
    /// </summary>
    public IntPtr HostHandle => _hwnd;

    /// <summary>
    /// Событие: HWND создан и готов к встраиванию консоли.
    /// Event: the HWND has been created and is ready for console embedding.
    /// </summary>
    public event Action<IntPtr>? HostReady;

    /// <summary>
    /// Флаг, был ли уже зарегистрирован оконный класс Win32 (однократная регистрация).
    /// Flag indicating whether the Win32 window class has already been registered (single registration).
    /// </summary>
    private static int _classRegistered; // FIXED: Use int for Interlocked.CompareExchange (race-safe)
    private static readonly object _classLock = new();

    /// <summary>
    /// Создаёт дочернее нативное окно-плейсхолдер под родительским HWND.
    /// Регистрирует оконный класс при первом вызове. Вызывает HostReady после создания.
    /// Creates a child native placeholder window under the parent HWND.
    /// Registers the window class on first call. Fires HostReady after creation.
    /// </summary>
    /// <param name="hwndParent">Дескриптор родительского окна WPF. / Parent WPF window handle.</param>
    /// <returns>HandleRef на созданное дочернее окно. / HandleRef to the created child window.</returns>
    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TerminalHost));

        LogService.Debug(
            $"TerminalHost.BuildWindowCore: parent={hwndParent.Handle:X} size={Width}x{Height}",
            "Terminal");

        var hInstance = Marshal.GetHINSTANCE(typeof(TerminalHost).Module);

        // Регистрируем оконный класс только один раз (FIXED: thread-safe with lock)
        lock (_classLock)
        {
            if (_classRegistered == 0)
            {
                var wc = new WNDCLASS
                {
                    style = 0,
                    lpfnWndProc = DefWindowProc,
                    hInstance = hInstance,
                    hbrBackground = (IntPtr)(COLOR_WINDOW + 1),
                    lpszClassName = WindowClass,
                };
                var regResult = RegisterClass(wc);
                LogService.Debug(
                    $"TerminalHost.BuildWindowCore: RegisterClass result={regResult}, lastError={Marshal.GetLastWin32Error()}",
                    "Terminal");
                if (regResult == 0)
                {
                    var err = Marshal.GetLastWin32Error();
                    if (err != 1410) // ERROR_CLASS_ALREADY_REGISTERED
                        throw new Win32Exception(err, "RegisterClass TerminalHost failed");
                }
                _classRegistered = 1;
            }
        }

        var w = (int)Math.Max(Width, 1);
        var h = (int)Math.Max(Height, 1);
        _hwnd = CreateWindowEx(
            0,
            WindowClass,
            "",
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
            0, 0, w, h,
            hwndParent.Handle,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            LogService.Debug(
                $"TerminalHost.BuildWindowCore: CreateWindowEx FAILED err={err}", "Terminal");
            throw new Win32Exception(err,
                "CreateWindowEx TerminalHost failed");
        }

        LogService.Debug(
            $"TerminalHost.BuildWindowCore: created hwnd={_hwnd:X}", "Terminal");
        HostReady?.Invoke(_hwnd);

        return new HandleRef(this, _hwnd);
    }

    /// <summary>
    /// Уничтожает дочернее нативное окно при завершении работы HwndHost.
    /// Destroys the child native window when the HwndHost shuts down.
    /// </summary>
    /// <param name="hwnd">HandleRef на уничтожаемое окно. / HandleRef to the window being destroyed.</param>
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (!_disposed && hwnd.Handle != IntPtr.Zero)
            DestroyWindow(hwnd.Handle);
        _hwnd = IntPtr.Zero;
    }

    /// <summary>
    /// Обрабатывает изменение размера хост-контейнера: синхронизирует размер нативного окна через MoveWindow.
    /// Handles host container resize: synchronises the native window size via MoveWindow.
    /// </summary>
    /// <param name="sizeInfo">Информация о новом размере. / Size change information.</param>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_hwnd != IntPtr.Zero && !_disposed)
        {
            MoveWindow(_hwnd, 0, 0,
                (int)sizeInfo.NewSize.Width, (int)sizeInfo.NewSize.Height, true);
        }
    }

    /// <summary>
    /// Освобождает ресурсы: уничтожает нативное окно при необходимости.
    /// Releases resources: destroys native window if needed.
    /// </summary>
public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        
        HostReady = null;
        GC.SuppressFinalize(this);
    }

    // Explicit interface implementation — hides the inherited method.
    void IDisposable.Dispose() => Dispose();

    /// <summary>
    /// Финализатор для гарантированной очистки ресурсов.
    /// Finalizer to ensure resource cleanup.
    /// </summary>
    ~TerminalHost()
    {
        Dispose();
    }

    //========== Win32 constants ==========

    /// <summary>Стиль WS_CHILD: окно является дочерним. / WS_CHILD style: the window is a child.</summary>
    private const int WS_CHILD = 0x40000000;
    /// <summary>Стиль WS_VISIBLE: окно видимо. / WS_VISIBLE style: the window is visible.</summary>
    private const int WS_VISIBLE = 0x10000000;
    /// <summary>Стиль WS_CLIPCHILDREN: исключает дочерние области из рисования. / WS_CLIPCHILDREN style: excludes child areas from drawing.</summary>
    private const int WS_CLIPCHILDREN = 0x02000000;
    /// <summary>Индекс системного цвета COLOR_WINDOW для фона окна. / COLOR_WINDOW system color index for window background.</summary>
    private const int COLOR_WINDOW = 5;

    /// <summary>
    /// Делегат оконной процедуры Win32 (WindowProc).
    /// Win32 window procedure delegate (WindowProc).
    /// </summary>
    /// <param name="hWnd">Дескриптор окна. / Window handle.</param>
    /// <param name="msg">Код сообщения. / Message code.</param>
    /// <param name="wParam">Параметр wParam. / wParam value.</param>
    /// <param name="lParam">Параметр lParam. / lParam value.</param>
    /// <returns>Результат обработки сообщения. / Message processing result.</returns>
    private delegate IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Структура WNDCLASS для регистрации оконного класса Win32.
    /// WNDCLASS structure for registering a Win32 window class.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        /// <summary>Стили класса. / Class styles.</summary>
        public uint style;
        /// <summary>Указатель на оконную процедуру. / Pointer to the window procedure.</summary>
        public WindowProc lpfnWndProc;
        /// <summary>Дополнительная память класса (байты). / Extra class memory (bytes).</summary>
        public int cbClsExtra;
        /// <summary>Дополнительная память окна (байты). / Extra window memory (bytes).</summary>
        public int cbWndExtra;
        /// <summary>Дескриптор экземпляра модуля. / Module instance handle.</summary>
        public IntPtr hInstance;
        /// <summary>Дескриптор иконки. / Icon handle.</summary>
        public IntPtr hIcon;
        /// <summary>Дескриптор курсора. / Cursor handle.</summary>
        public IntPtr hCursor;
        /// <summary>Кисть фона. / Background brush.</summary>
        public IntPtr hbrBackground;
        /// <summary>Имя меню (не используется). / Menu name (unused).</summary>
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        /// <summary>Имя класса окна. / Window class name.</summary>
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    /// <summary>
    /// Регистрирует оконный класс Win32 (user32!RegisterClassW).
    /// Registers a Win32 window class (user32!RegisterClassW).
    /// </summary>
    /// <param name="lpWndClass">Описание класса. / Class description.</param>
    /// <returns>Atom класса при успехе, 0 при ошибке. / Class atom on success, 0 on failure.</returns>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(WNDCLASS lpWndClass);

    /// <summary>
    /// Создаёт окно расширенного стиля (user32!CreateWindowExW).
    /// Creates an extended-style window (user32!CreateWindowExW).
    /// </summary>
    /// <param name="dwExStyle">Расширенные стили окна. / Extended window styles.</param>
    /// <param name="lpClassName">Имя класса окна. / Window class name.</param>
    /// <param name="lpWindowName">Заголовок окна. / Window title.</param>
    /// <param name="dwStyle">Стили окна. / Window styles.</param>
    /// <param name="x">Начальная позиция X. / Initial X position.</param>
    /// <param name="y">Начальная позиция Y. / Initial Y position.</param>
    /// <param name="nWidth">Ширина окна. / Window width.</param>
    /// <param name="nHeight">Высота окна. / Window height.</param>
    /// <param name="hWndParent">Дескриптор родительского окна. / Parent window handle.</param>
    /// <param name="hMenu">Дескриптор меню или идентификатор дочернего окна. / Menu handle or child-window identifier.</param>
    /// <param name="hInstance">Дескриптор экземпляра модуля. / Module instance handle.</param>
    /// <param name="lpParam">Дополнительные данные. / Additional data.</param>
    /// <returns>Дескриптор созданного окна или IntPtr.Zero при ошибке. / Handle of the created window or IntPtr.Zero on error.</returns>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    /// <summary>
    /// Уничтожает окно (user32!DestroyWindow).
    /// Destroys a window (user32!DestroyWindow).
    /// </summary>
    /// <param name="hWnd">Дескриптор окна. / Window handle.</param>
    /// <returns>true при успехе, false при ошибке. / true on success, false on failure.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    /// <summary>
    /// Перемещает и изменяет размер окна (user32!MoveWindow).
    /// Moves and resizes a window (user32!MoveWindow).
    /// </summary>
    /// <param name="hWnd">Дескриптор окна. / Window handle.</param>
    /// <param name="x">Новая позиция X. / New X position.</param>
    /// <param name="y">Новая позиция Y. / New Y position.</param>
    /// <param name="nWidth">Новая ширина. / New width.</param>
    /// <param name="nHeight">Новая высота. / New height.</param>
    /// <param name="bRepaint">Флаг перерисовки. / Repaint flag.</param>
    /// <returns>true при успехе, false при ошибке. / true on success, false on failure.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

    /// <summary>
    /// Стандартная оконная процедура по умолчанию (user32!DefWindowProcW).
    /// Default standard window procedure (user32!DefWindowProcW).
    /// </summary>
    /// <param name="hWnd">Дескриптор окна. / Window handle.</param>
    /// <param name="msg">Код сообщения. / Message code.</param>
    /// <param name="wParam">Параметр wParam. / wParam value.</param>
    /// <param name="lParam">Параметр lParam. / lParam value.</param>
    /// <returns>Результат обработки сообщения. / Message processing result.</returns>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}