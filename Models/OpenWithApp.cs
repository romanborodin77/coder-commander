using System.Windows.Media;

namespace CoderCommander.Models;

/// <summary>
/// Модель приложения, ассоциированного с типом файла (для диалога «Открыть как»).
/// Model of an application associated with a file type (for "Open With" dialog).
/// </summary>
public sealed class OpenWithApp
{
    /// <summary>
    /// Отображаемое имя приложения (например, "Notepad++").
    /// Display name of the application (e.g. "Notepad++").
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Полный путь к исполняемому файлу (.exe).
    /// Full path to the executable (.exe).
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Иконка приложения (извлекается из .exe через ExtractAssociatedIcon).
    /// Application icon (extracted from .exe via ExtractAssociatedIcon).
    /// </summary>
    public ImageSource? Icon { get; init; }

    public override string ToString() => Name;
}
