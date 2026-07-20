using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CoderCommander.Services;
using CoderCommander.Views;

namespace CoderCommander.Models;

/// <summary>
/// Модель вкладки редактора: файл, содержимое, состояние модификации.
/// Editor tab model: file, content, modification state.
/// </summary>
public partial class EditorTabViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabTitle))]
    [NotifyPropertyChangedFor(nameof(FilePathDisplay))]
    private string _filePath = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabTitle))]
    [NotifyPropertyChangedFor(nameof(FilePathDisplay))]
    private string _fileName = "";

    [ObservableProperty]
    private string _content = "";

    [ObservableProperty]
    private string _originalContent = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabTitle))]
    private bool _isModified;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private string _language = LocalizationService.Current.GetString("Editor.Normal");

    [ObservableProperty]
    private string _encoding = "UTF-8";

    [ObservableProperty]
    private int _lineCount;

    [ObservableProperty]
    private int _caretOffset;

    /// <summary>
    /// Заголовок вкладки: «* filename» если модифицирован, иначе «filename».
    /// Tab title: "* filename" if modified, otherwise "filename".
    /// </summary>
    public string TabTitle => IsModified ? $"* {FileName}" : FileName;

    /// <summary>
    /// Сокращённый путь для отображения (имя родительской папки + имя файла).
    /// Shortened display path (parent folder name + file name).
    /// </summary>
    public string FilePathDisplay
    {
        get
        {
            var dir = Path.GetDirectoryName(FilePath);
            var parentDir = Path.GetFileName(dir);
            return !string.IsNullOrEmpty(parentDir)
                ? $"{parentDir}\\{FileName}"
                : FileName;
        }
    }

    /// <summary>
    /// Обновляет содержимое вкладки, проверяет модификацию и пересчитывает строки.
    /// Updates tab content, checks modification, and recalculates line count.
    /// </summary>
    public void UpdateContent(string newText)
    {
        Content = newText;
        IsModified = Content != OriginalContent;
        LineCount = string.IsNullOrEmpty(newText) ? 0 : newText.Split('\n').Length;
    }

    /// <summary>
    /// Сохраняет текст в файл (UTF-8). Недоступно в режиме «только чтение».
    /// Saves text to file (UTF-8). Not available in read-only mode.
    /// </summary>
    public void Save()
    {
        if (IsReadOnly) return;
        try
        {
            File.WriteAllText(FilePath, Content, System.Text.Encoding.UTF8);
            OriginalContent = Content;
            IsModified = false;
        }
        catch (Exception ex)
        {
            StyledMessageBoxWindow.Show(
                string.Format(LocalizationService.Current.GetString("Editor.SaveError"), ex.Message),
                LocalizationService.Current.GetString("Error.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// true если содержимое отличается от исходного (есть несохранённые изменения).
    /// true if content differs from original (there are unsaved changes).
    /// </summary>
    public bool HasUnsavedChanges()
    {
        if (IsReadOnly) return false;
        return Content != OriginalContent;
    }

    /// <summary>
    /// Определяет язык подсветки синтаксиса по расширению файла.
    /// Detects syntax highlighting language by file extension.
    /// </summary>
    public static string DetectLanguage(string filePath)
    {
        var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
        return ext switch
        {
            ".cs" => "C#",
            ".xaml" => "XAML",
            ".xml" => "XML",
            ".json" => "JSON",
            ".js" => "JavaScript",
            ".ts" => "TypeScript",
            ".py" => "Python",
            ".java" => "Java",
            ".cpp" or ".cc" or ".cxx" or ".h" or ".hpp" => "C++",
            ".css" => "CSS",
            ".html" or ".htm" => "HTML",
            ".sql" => "SQL",
            ".md" => "Markdown",
            ".yaml" or ".yml" => "YAML",
            ".sh" or ".bash" => "Bash",
            ".ps1" => "PowerShell",
            ".dockerfile" => "Dockerfile",
            ".gitignore" or ".gitkeep" => "Git",
            _ => LocalizationService.Current.GetString("Editor.Normal")
        };
    }
}
