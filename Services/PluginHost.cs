using System.Collections.Concurrent;
using CoderCommander.FileSystem;
using CoderCommander.Services;
using CoderCommander.ViewModels;
using CoderCommander.Models;

namespace CoderCommander.Services;

/// <summary>
/// Реализация хоста плагинов: делегирует регистрацию расширений в соответствующие сервисы.
/// Plugin host implementation: delegates extension registration to the corresponding services.
/// </summary>
public sealed class PluginHost : IPluginHost
{
    /// <summary>
    /// Зарегистрированные плагинами файловые системы (имя → IFileSystem).
    /// Filesystems registered by plugins (name → IFileSystem).
    /// </summary>
    public static ConcurrentDictionary<string, IFileSystem> RegisteredFileSystems { get; } = new();

    private readonly CommandEngine _commands;
    private readonly MainViewModel _mainVm;

    /// <summary>
    /// Создаёт экземпляр хоста плагинов.
    /// Creates a plugin host instance.
    /// </summary>
    /// <param name="commands">Движок быстрых команд. / Quick command engine.</param>
    /// <param name="mainVm">Главная ViewModel приложения. / Main application ViewModel.</param>
    public PluginHost(CommandEngine commands, MainViewModel mainVm)
    {
        _commands = commands;
        _mainVm = mainVm;
    }

    /// <inheritdoc />
    public void RegisterFileSystem(string name, IFileSystem fs)
    {
        RegisteredFileSystems[name] = fs;
        LogService.Info($"Plugin registered filesystem: {name}", nameof(PluginHost));
    }

    /// <inheritdoc />
    public void RegisterContentProvider(string name, IContentProvider provider)
    {
        ContentProviderRegistry.Instance.Register(provider);
        LogService.Info($"Plugin registered content provider: {name}", nameof(PluginHost));
    }

    /// <inheritdoc />
    public void RegisterCommand(string name, QuickCommand command)
    {
        _commands.Register(command);
        LogService.Info($"Plugin registered command: {name}", nameof(PluginHost));
    }

    /// <inheritdoc />
    public void RegisterSyntaxHighlighter(string extension, string language)
    {
        SyntaxHighlighter.RegisterExtension(extension, language);
        LogService.Info($"Plugin registered syntax highlighter: {extension} -> {language}", nameof(PluginHost));
    }

    /// <inheritdoc />
    public void Log(string message)
    {
        LogService.Debug(message, "Plugin");
    }

    /// <inheritdoc />
    public MainViewModel MainViewModel => _mainVm;
}
