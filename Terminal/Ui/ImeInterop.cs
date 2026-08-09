using System.Runtime.InteropServices;

namespace CoderCommander.Terminal.Ui;

/// <summary>
/// Positions the IME composition window at the terminal's cursor cell. Without this, Windows
/// defaults to placing the CJK candidate popup at a fixed corner of the control regardless of
/// where the caret actually is - workable for a single-line text box, disorienting for a
/// full-screen grid where the cursor can be anywhere. <see cref="TerminalCanvas"/> calls
/// <see cref="RepositionAt"/> on <c>WM_IME_STARTCOMPOSITION</c> (composition just began, this is
/// the one moment Windows actually reads the position) and whenever the cursor cell changes while
/// a composition is in progress.
/// </summary>
internal static partial class ImeInterop
{
    private const int CfsPoint = 0x0002;

    [LibraryImport("imm32.dll")]
    private static partial nint ImmGetContext(nint hWnd);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ImmReleaseContext(nint hWnd, nint hImc);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ImmSetCompositionWindow(nint hImc, ref CompositionForm form);

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionForm
    {
        public int DwStyle;
        public Point PtCurrentPos;
        public Rect RcArea;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>Positions the composition window's anchor point at (x, y) in <paramref name="handle"/>'s
    /// client coordinates - the top-left of the terminal cursor's cell is the right choice, so the
    /// candidate popup opens right below/beside where the user is about to see their typed text
    /// land.</summary>
    public static void RepositionAt(nint handle, int x, int y)
    {
        var hImc = ImmGetContext(handle);
        if (hImc == nint.Zero) return;
        try
        {
            var form = new CompositionForm
            {
                DwStyle = CfsPoint,
                PtCurrentPos = new Point { X = x, Y = y }
            };
            ImmSetCompositionWindow(hImc, ref form);
        }
        finally
        {
            ImmReleaseContext(handle, hImc);
        }
    }
}
