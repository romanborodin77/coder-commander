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
    /// <summary>Best-effort clipboard write with retry. Logs and returns false on failure
    /// instead of throwing. Antivirus scanners and clipboard managers frequently hold the
    /// clipboard open briefly — retrying with a short delay handles the common case.</summary>
    public static bool TrySetClipboard(string text)
    {
        for (var i = 0; i < 10; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (ExternalException)
            {
                System.Threading.Thread.Sleep(50);
            }
        }
        LogService.Error("Clipboard copy failed after 10 retries");
        return false;
    }
}
