using System.Runtime.InteropServices;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Shared clipboard-write helper. <c>Clipboard.SetText</c> throws <see cref="ExternalException"/>
/// when another process (or the shell handler) is holding the clipboard open, which happens often
/// enough in practice (antivirus scanners, clipboard managers) that every caller needs to swallow
/// it rather than let it surface as an unhandled exception.
/// </summary>
internal static class ClipboardHelper
{
    /// <summary>Best-effort clipboard write. Logs and returns false on failure instead of throwing.</summary>
    public static bool TrySetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error($"Clipboard copy failed: {ex.Message}");
            return false;
        }
    }
}
