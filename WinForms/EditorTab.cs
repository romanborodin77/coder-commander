using System.Text;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Represents a single editor tab: file identity/encoding plus a <see cref="CodeEditorControl"/>.
/// </summary>
public sealed class EditorTab : IDisposable
{
    public string FilePath { get; set; }
    public string FileName => string.IsNullOrEmpty(FilePath) ? LocalizationService.Current.GetString("Edit.NewFile") : Path.GetFileName(FilePath);
    public LanguageId Language { get; set; }
    /// <summary>Defaults to UTF-8 without a BOM for new/unsaved files, matching most editors' convention.</summary>
    public Encoding Encoding { get; set; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    public CodeEditorControl Editor { get; }
    public string DisplayName => IsModified ? $"*{FileName}" : FileName;

    public bool IsModified
    {
        get => Editor.Modified;
        set => Editor.Modified = value;
    }

    public EditorTab(string? filePath = null)
    {
        FilePath = filePath ?? "";
        Language = LanguageDetector.Detect(filePath);
        Editor = new CodeEditorControl { Dock = DockStyle.Fill, Language = Language };
    }

    public void LoadFile(string path)
    {
        try
        {
            FilePath = path;
            Language = LanguageDetector.Detect(path);

            var bytes = File.ReadAllBytes(path);
            Encoding = TextEncodingDetector.Detect(bytes, out var preambleLength);
            var text = Encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);

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

    public void SaveFile(string? path = null)
    {
        var savePath = path ?? FilePath;
        if (string.IsNullOrEmpty(savePath))
            return;

        try
        {
            File.WriteAllText(savePath, Editor.Text, Encoding);
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

    public void ApplyTheme()
    {
        Editor.ApplyTheme();
    }

    /// <summary>No-op until the syntax-highlighting milestone wires real tokenizing into Editor.Language.</summary>
    public void ApplySyntaxHighlighting()
    {
        Editor.Language = Language;
    }

    public (int Line, int Column) GetCursorPosition() => Editor.GetCursorPosition();

    public void Dispose()
    {
        Editor.Dispose();
    }
}
