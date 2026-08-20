using System.Text;
using CoderCommander.FileSystem;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Represents a single editor tab: file identity/encoding plus a <see cref="CodeEditorControl"/>.
///
/// <para>Reads/writes through <paramref name="fileSystem"/> (defaulting to
/// <see cref="LocalFileSystem"/> when not given - the New-tab and toolbar File&gt;Open/Save-As
/// dialogs only ever produce real local paths, so they never need to pass one), the same
/// <c>OpenReadAsync</c>/<c>CopyFromStreamAsync</c> pattern <c>Viewers.ViewerSource</c> already
/// established for F3 - this is what lets F4 open (and, for a writable provider, save) a file
/// inside an archive or on an FTP/SFTP/WebDAV connection instead of the previous
/// <c>File.Exists</c> check silently failing and leaving an empty untitled tab with no error at
/// all.</para>
/// </summary>
public sealed class EditorTab : IDisposable
{
    private readonly IFileSystem _fs;

    /// <summary>Gets or sets the full file-system path associated with this tab. Empty for new/unsaved files.</summary>
    public string FilePath { get; set; }
    /// <summary>Gets the display name of the file (without directory), or a localized "New file" placeholder when <see cref="FilePath"/> is empty.</summary>
    public string FileName => string.IsNullOrEmpty(FilePath) ? LocalizationService.Current.GetString("Edit.NewFile") : VfsPath.GetName(FilePath);
    /// <summary>Gets or sets the language identifier used for syntax highlighting.</summary>
    public LanguageId Language { get; set; }
    /// <summary>Defaults to UTF-8 without a BOM for new/unsaved files, matching most editors' convention.</summary>
    public Encoding Encoding { get; set; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    /// <summary>Gets the filesystem this tab reads/writes through.</summary>
    public IFileSystem FileSystem => _fs;
    /// <summary>Gets the underlying code editor control bound to this tab.</summary>
    public CodeEditorControl Editor { get; }
    /// <summary>Gets the tab display name, prefixed with <c>*</c> when the file has unsaved
    /// modifications and suffixed with a read-only marker when <see cref="IsReadOnly"/>.</summary>
    public string DisplayName => (IsModified ? $"*{FileName}" : FileName) + (IsReadOnly ? " [RO]" : "");

    /// <summary>Gets or sets the modified flag. Setting to <c>false</c> resets the clean-state marker used by undo.</summary>
    public bool IsModified
    {
        get => Editor.Modified;
        set => Editor.Modified = value;
    }

    /// <summary>
    /// True when the file's own filesystem can't be written to (set after a successful
    /// <see cref="LoadFileAsync"/> from <see cref="_fs"/>'s own <see cref="FileSystemCapabilities.Writable"/>
    /// - e.g. a read-only archive format like RAR/7z/TAR.XZ). A materialized (downloaded-to-browse)
    /// remote archive is NOT read-only this way - it is fully writable against its local temp copy,
    /// with a write-back offered on leaving it (see <c>DirtyTrackingFileSystem</c>/
    /// <c>PanelViewModel.ReleaseArchiveLease</c>) rather than being refused here. Editing itself is not blocked at the canvas
    /// level - see this class's own scope note in <see cref="SaveFileAsync"/> - but Save refuses
    /// outright instead of attempting (and failing) a write, and the tab title marks it so the
    /// user isn't surprised by an error only once they try to save.
    /// </summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>
    /// Initializes a new editor tab, detecting the language from <paramref name="filePath"/>.
    /// </summary>
    /// <param name="fileSystem">Filesystem the file lives on; null means local disk.</param>
    /// <param name="filePath">Optional file path for language detection and initial file identity.</param>
    public EditorTab(IFileSystem? fileSystem = null, string? filePath = null)
    {
        _fs = fileSystem ?? new LocalFileSystem();
        // FilePath is deliberately NOT set here — LoadFileAsync sets it only after the read
        // succeeds. Setting it in the constructor left a tab pointing at a file whose load
        // failed (locked, permission denied, OOM) with an empty buffer: Ctrl+S would then
        // save the empty buffer over the original file, destroying its content.
        FilePath = "";
        Language = LanguageDetector.Detect(filePath);
        Editor = new CodeEditorControl { Dock = DockStyle.Fill, Language = Language };
    }

    /// <summary>
    /// Reads the file at <paramref name="path"/> into the editor, auto-detecting encoding and language.
    /// </summary>
    /// <param name="path">Path to the file to load, on this tab's own <see cref="IFileSystem"/>.</param>
    public async Task<bool> LoadFileAsync(string path, CancellationToken ct = default)
    {
        try
        {
            byte[] bytes;
            var stream = await _fs.OpenReadAsync(path, ct).ConfigureAwait(true);
            await using (stream.ConfigureAwait(true))
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct).ConfigureAwait(true);
                bytes = ms.ToArray();
            }

            var encoding = TextEncodingDetector.Detect(bytes, out var preambleLength);
            var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);

            // Only commit path/language/encoding once the read+decode above has actually
            // succeeded - assigning FilePath up front left a tab pointing at a file whose load
            // failed (locked, permission denied, huge enough to throw OutOfMemoryException) with
            // an empty buffer still showing. A subsequent Ctrl+S on that tab would silently
            // truncate the real file on disk to nothing.
            FilePath = path;
            Language = LanguageDetector.Detect(path);
            Encoding = encoding;
            IsReadOnly = !_fs.Capabilities.HasFlag(FileSystemCapabilities.Writable);

            Editor.LoadText(text);
            Editor.Language = Language;
            return true;
        }
        catch (Exception ex)
        {
            var L = LocalizationService.Current;
            StyledMessageBox.Show(L.GetString("Edit.ErrorLoading", ex.Message),
                L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error);
            LogService.Error($"Error loading file {path}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Saves the editor content, optionally to a new path. No-op (with an error dialog, not a
    /// silent discard) when <see cref="IsReadOnly"/> - refusing up front is what
    /// <see cref="LoadFileAsync"/>'s own doc comment means by "Save refuses outright instead of
    /// attempting (and failing) a write": the alternative is calling <see cref="IFileSystem.CopyFromStreamAsync"/>
    /// on a provider that lacks <see cref="FileSystemCapabilities.Writable"/> and surfacing its
    /// <see cref="NotSupportedException"/> as a generic save-failed message instead of this clear one.
    ///
    /// <para>Scope note: this class does not disable keystroke input for a read-only file - the
    /// canvas has no such mode today, and adding one is a separate, larger UI change. The
    /// correctness guarantee this class DOES make is that a read-only file's content can never be
    /// silently overwritten; the user can still type, and will find out it can't be saved.</para>
    /// </summary>
    /// <param name="path">Destination path, or <c>null</c>/<see cref="string.Empty"/> to save to <see cref="FilePath"/>.</param>
    /// <returns><c>true</c> if the save actually completed; <c>false</c> on refusal (read-only,
    /// empty path) or failure (shown to the user via a dialog either way). Callers that close a tab
    /// or the window after "save and continue" must check this - previously the return type was
    /// <c>void</c>/<c>Task</c>, so a failed save (network drop, auth expiry, disk full) still let
    /// the caller proceed to discard the buffer as though the save had succeeded.</returns>
    public async Task<bool> SaveFileAsync(string? path = null, CancellationToken ct = default)
    {
        var savePath = path ?? FilePath;
        if (string.IsNullOrEmpty(savePath))
            return false;

        var L = LocalizationService.Current;

        if (IsReadOnly)
        {
            StyledMessageBox.Show(L.GetString("Edit.ErrorSaving", L.GetString("Edit.ReadOnlyFile")),
                L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error);
            return false;
        }

        try
        {
            if (_fs.Capabilities.HasFlag(FileSystemCapabilities.NativePaths))
            {
                // Write-then-replace so a crash, power loss, or full disk mid-write can't leave the
                // file truncated/corrupted - the same pattern every other user-data write in the
                // project already uses (SettingsService.Save, CredentialStore.Save,
                // Archives/RewritingArchiveWriter, ZipUpdateSession).
                //
                // TempFileNaming.NextTo (Guid-suffixed), not a fixed "path + .tmp": a predictable
                // name has two problems a fixed one doesn't - two editor windows saving the same
                // file collide on the same temp name, and a failed File.Move (destination read-only
                // or locked by another process) used to leave that fixed-name file behind forever,
                // with no cleanup on the failure path at all.
                var tempPath = Utils.TempFileNaming.NextTo(savePath, "save");
                try
                {
                    File.WriteAllText(tempPath, Editor.Text, Encoding);
                    File.Move(tempPath, savePath, overwrite: true);
                }
                catch
                {
                    try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
                    throw;
                }
            }
            else
            {
                // Same sidecar-then-rename shape as MaterializedFile.WriteBackAsync, for the same
                // reason: CopyFromStreamAsync on a remote provider is one direct PUT/STOR with no
                // atomic in-place replace, so writing straight over savePath would leave a
                // truncated file there if the upload failed or was interrupted partway.
                // Prepend BOM/preamble for encodings that use one (UTF-8 with BOM, UTF-16).
                // Encoding.GetBytes alone doesn't include the preamble — without this, saving
                // a BOM-bearing file to a remote FS silently strips the BOM, breaking round-trip.
                var preamble = Encoding.GetPreamble();
                var body = Encoding.GetBytes(Editor.Text);
                byte[] bytes;
                if (preamble.Length > 0)
                {
                    bytes = new byte[preamble.Length + body.Length];
                    Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
                    Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
                }
                else
                {
                    bytes = body;
                }
                var sidecar = savePath + ".cc-save-" + Guid.NewGuid().ToString("N");
                try
                {
                    using (var ms = new MemoryStream(bytes))
                        await _fs.CopyFromStreamAsync(sidecar, ms, ct).ConfigureAwait(true);

                    var uploaded = await _fs.GetFileInfoAsync(sidecar, ct).ConfigureAwait(true);
                    if (uploaded == null || uploaded.Size != bytes.LongLength)
                        throw new IOException($"Save of \"{savePath}\" did not complete: uploaded size did not match.");

                    await _fs.MoveAsync(sidecar, savePath, overwrite: true, CancellationToken.None).ConfigureAwait(true);
                }
                catch
                {
                    // Same reasoning as MaterializedFile.WriteBackAsync's identical guard: some
                    // providers' MoveAsync(overwrite: true) deletes the destination before renaming
                    // (SFTP has no atomic overwrite in the base protocol - see
                    // SftpFileSystem.MoveAsync), so if the rename step itself is what failed, the
                    // origin is already gone and this sidecar - fully uploaded and verified above -
                    // is the only surviving copy. Only delete it when the origin is confirmed still
                    // present; otherwise keep it and say where it is.
                    FileEntry? originStillThere;
                    try { originStillThere = await _fs.GetFileInfoAsync(savePath, CancellationToken.None).ConfigureAwait(true); }
                    catch { originStillThere = null; }

                    if (originStillThere is not null)
                    {
                        try { await _fs.DeleteAsync(sidecar, recursive: false, CancellationToken.None).ConfigureAwait(true); }
                        catch { /* best-effort cleanup of the sidecar; the original exception is what matters */ }
                        throw;
                    }

                    throw new IOException(
                        $"Save of \"{savePath}\" failed after the original file was already removed. " +
                        $"Your changes were NOT lost - they are sitting at \"{sidecar}\" and must be " +
                        $"renamed back to \"{savePath}\" manually.");
                }
            }

            FilePath = savePath;
            IsModified = false;
            return true;
        }
        catch (Exception ex)
        {
            StyledMessageBox.Show(L.GetString("Edit.ErrorSaving", ex.Message),
                L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error);
            return false;
        }
    }

    /// <summary>Applies the current theme to the editor control.</summary>
    public void ApplyTheme()
    {
        Editor.ApplyTheme();
    }

    /// <summary>No-op until the syntax-highlighting milestone wires real tokenizing into Editor.Language.</summary>
    public void ApplySyntaxHighlighting()
    {
        Editor.Language = Language;
    }

    /// <summary>Returns the current caret position as a 1-based (line, column) tuple.</summary>
    public (int Line, int Column) GetCursorPosition() => Editor.GetCursorPosition();

    /// <summary>Disposes the underlying editor control and releases resources.</summary>
    public void Dispose()
    {
        Editor.Dispose();
    }
}
