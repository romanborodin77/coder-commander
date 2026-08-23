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

    /// <summary>The clipboard format name Explorer itself writes alongside
    /// <see cref="DataFormats.FileDrop"/> to say whether a paste should copy or move - a plain
    /// <c>4</c>-byte little-endian <c>DROPEFFECT</c> value. Not in <see cref="DataFormats"/>
    /// because it's shell-specific, not a stock Windows Forms format.</summary>
    private const string PreferredDropEffectFormat = "Preferred DropEffect";

    private const int DropEffectCopy = 1;
    private const int DropEffectMove = 2;

    /// <summary>
    /// Puts <paramref name="shellPaths"/> on the clipboard as a real shell file-drop -
    /// interoperable with Explorer's own Copy/Cut/Paste, not just this app's internal clipboard.
    /// Writes <see cref="DataFormats.FileDrop"/> plus the "Preferred DropEffect" stream Explorer
    /// itself always writes, so a paste into Explorer (or another shell-aware app) picks copy vs.
    /// move correctly. <see cref="Clipboard.SetDataObject(object,bool,int,int)"/>'s own retry
    /// handles the same transient-clipboard-owner case <see cref="TrySetClipboard"/> does.
    ///
    /// <para>Known limitation, accepted rather than half-implemented: Explorer ghosts a cut
    /// item's icon because it keeps a live <see cref="IDataObject"/> and watches for a
    /// "Paste Succeeded" callback from the destination. This hands over a plain snapshot instead,
    /// so this app's own rows never grey out after a cut - the paste itself is fully functional,
    /// only that one purely cosmetic feedback is missing.</para>
    /// </summary>
    public static bool TrySetFileDrop(IReadOnlyList<string> shellPaths, bool cut)
    {
        if (shellPaths.Count == 0) return false;
        try
        {
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, true, shellPaths.ToArray());
            data.SetData(PreferredDropEffectFormat, new MemoryStream(BitConverter.GetBytes(cut ? DropEffectMove : DropEffectCopy)));
            Clipboard.SetDataObject(data, copy: true, retryTimes: 10, retryDelay: 50);
            return true;
        }
        catch (ExternalException ex)
        {
            LogService.Error($"Clipboard file copy failed after 10 retries: {ex.Message}");
            return false;
        }
    }

    /// <summary>Cheap clipboard probe for a file-drop's presence - backed by
    /// <c>IsClipboardFormatAvailable</c>, no OLE marshalling to the owning process. Safe to call
    /// on every context-menu build; unlike <see cref="TryGetFileDrop"/>, this cannot hang if the
    /// clipboard's owning process is busy or has stopped responding.</summary>
    public static bool ContainsFileDrop() => Clipboard.ContainsFileDropList();

    /// <summary>
    /// Reads a shell file-drop off the clipboard, if there is one. <paramref name="cut"/> is true
    /// only when the source app explicitly marked the transfer as a move (a "Preferred DropEffect"
    /// stream whose value has the move bit set); missing, unreadable, or any other value defaults
    /// to <see langword="false"/> (copy) - treating an unrecognized effect as a move would delete
    /// the user's source files on a misread. Unlike <see cref="ContainsFileDrop"/>, this does
    /// perform the full OLE round-trip to the clipboard owner (<see cref="Clipboard.GetDataObject()"/>)
    /// and so is only called from the "Paste" click handler itself, never from context-menu
    /// construction.
    /// </summary>
    public static IReadOnlyList<string> TryGetFileDrop(out bool cut)
    {
        cut = false;
        try
        {
            var data = Clipboard.GetDataObject();
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return [];
            if (data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return [];

            if (data.GetData(PreferredDropEffectFormat) is { } raw)
            {
                var bytes = raw switch
                {
                    MemoryStream ms => ms.ToArray(),
                    byte[] b => b,
                    _ => null
                };
                if (bytes is { Length: >= 4 })
                    cut = (BitConverter.ToInt32(bytes, 0) & DropEffectMove) != 0;
            }

            return paths;
        }
        catch (ExternalException ex)
        {
            // Covers COMException too (ExternalException is its base type).
            LogService.Error($"Clipboard file read failed: {ex.Message}");
            return [];
        }
    }
}
