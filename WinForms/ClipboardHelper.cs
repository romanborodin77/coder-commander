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
    /// clipboard open briefly — retrying with a short delay handles the common case.
    ///
    /// <para>Uses the framework's own <c>Clipboard.SetDataObject(data, copy, retryTimes,
    /// retryDelay)</c> overload rather than a hand-rolled <c>Clipboard.SetText</c> + <c>Thread.Sleep</c>
    /// loop - every call site here is a synchronous UI event handler, so the retry delay still
    /// blocks the UI thread either way, but the framework's own OLE-level retry loop is what
    /// Windows Forms itself ships specifically for this exact "clipboard transiently held open"
    /// case, rather than this app re-implementing the same shape by hand.</para>
    /// </summary>
    public static bool TrySetClipboard(string text)
    {
        try
        {
            Clipboard.SetDataObject(text, copy: true, retryTimes: 10, retryDelay: 50);
            return true;
        }
        catch (ExternalException ex)
        {
            LogService.Error($"Clipboard copy failed after 10 retries: {ex.Message}");
            return false;
        }
    }
}
