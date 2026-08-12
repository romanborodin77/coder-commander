using System.Text;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Represents a single editor tab: file identity/encoding plus a <see cref="CodeEditorControl"/>.
/// </summary>
public sealed class EditorTab : IDisposable
{
    /// <summary>Gets or sets the full file-system path associated with this tab. Empty for new/unsaved files.</summary>
    public string FilePath { get; set; }
    /// <summary>Gets the display name of the file (without directory), or a localized "New file" placeholder when <see cref="FilePath"/> is empty.</summary>
    public string FileName => string.IsNullOrEmpty(FilePath) ? LocalizationService.Current.GetString("Edit.NewFile") : Path.GetFileName(FilePath);
    /// <summary>Gets or sets the language identifier used for syntax highlighting.</summary>
    public LanguageId Language { get; set; }
    /// <summary>Defaults to UTF-8 without a BOM for new/unsaved files, matching most editors' convention.</summary>
    public Encoding Encoding { get; set; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    /// <summary>Gets the underlying code editor control bound to this tab.</summary>
    public CodeEditorControl Editor { get; }
    /// <summary>Gets the tab display name, prefixed with <c>*</c> when the file has unsaved modifications.</summary>
    public string DisplayName => IsModified ? $"*{FileName}" : FileName;

    /// <summary>Gets or sets the modified flag. Setting to <c>false</c> resets the clean-state marker used by undo.</summary>
    public bool IsModified
    {
        get => Editor.Modified;
        set => Editor.Modified = value;
    }

    /// <summary>
    /// Initializes a new editor tab, detecting the language from <paramref name="filePath"/>.
    /// </summary>
    /// <param name="filePath">Optional file path for language detection and initial file identity.</param>
    public EditorTab(string? filePath = null)
    {
        FilePath = filePath ?? "";
        Language = LanguageDetector.Detect(filePath);
        Editor = new CodeEditorControl { Dock = DockStyle.Fill, Language = Language };
    }

    /// <summary>
    /// Reads the file at <paramref name="path"/> into the editor, auto-detecting encoding and language.
    /// </summary>
    /// <param name="path">Absolute path to the file to load.</param>
    public void LoadFile(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
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

            Editor.LoadText(text);
            Editor.Language = Language;
        }
        catch (Exception ex)
        {
            var L = LocalizationService.Current;
            StyledMessageBox.Show(L.GetString("Edit.ErrorLoading", ex.Message),
                L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error);
            LogService.Error($"Error loading file {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves the editor content to disk, optionally to a new path.
    /// </summary>
    /// <param name="path">Destination path, or <c>null</c>/<see cref="string.Empty"/> to save to <see cref="FilePath"/>.</param>
    public void SaveFile(string? path = null)
    {
        var savePath = path ?? FilePath;
        if (string.IsNullOrEmpty(savePath))
            return;

        try
        {
            // Write-then-replace so a crash, power loss, or full disk mid-write can't leave the
            // file truncated/corrupted - the same pattern every other user-data write in the
            // project already uses (SettingsService.Save, CredentialStore.Save,
            // Archives/RewritingArchiveWriter, ZipUpdateSession).
            var tempPath = savePath + ".tmp";
            File.WriteAllText(tempPath, Editor.Text, Encoding);
            File.Move(tempPath, savePath, overwrite: true);
            FilePath = savePath;
            IsModified = false;
        }
        catch (Exception ex)
        {
            var L = LocalizationService.Current;
            StyledMessageBox.Show(L.GetString("Edit.ErrorSaving", ex.Message),
                L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error);
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
