using CoderCommander.FileSystem;
using CoderCommander.Services;
using CoderCommander.ViewModels;

namespace CoderCommander.Models;

/// <summary>
/// Интерфейс хоста, предоставляемый плагину для регистрации расширений и доступа к сервисам.
/// Host interface provided to plugins for registering extensions and accessing services.
/// </summary>
public interface IPluginHost
{
    /// <summary>
    /// Регистрирует пользовательскую файловую систему (VFS).
    /// Registers a custom virtual filesystem (VFS).
    /// </summary>
    void RegisterFileSystem(string name, IFileSystem fs);

    /// <summary>
    /// Регистрирует пользовательский провайдер содержимого.
    /// Registers a custom content provider.
    /// </summary>
    void RegisterContentProvider(string name, IContentProvider provider);

    /// <summary>
    /// Регистрирует быструю команду в палитре (Ctrl+P).
    /// Registers a quick command in the palette (Ctrl+P).
    /// </summary>
    void RegisterCommand(string name, QuickCommand command);

    /// <summary>
    /// Регистрирует подсветку синтаксиса для расширения файла.
    /// Registers syntax highlighting for a file extension.
    /// </summary>
    void RegisterSyntaxHighlighter(string extension, string language);

    /// <summary>
    /// Записывает сообщение в лог приложения.
    /// Writes a message to the application log.
    /// </summary>
    void Log(string message);

    /// <summary>Главная ViewModel приложения. / Main application ViewModel.</summary>
    MainViewModel MainViewModel { get; }
}
