using System;
using System.Collections.Generic;
using System.Linq;
using CoderCommander.Services;

namespace CoderCommander.FileSystem;

/// <summary>
/// Глобальный реестр провайдеров содержимого (ph4.3 / exp.yml).
/// Global content provider registry (ph4.3).
/// Хранит зарегистрированные <see cref="IContentProvider"/> и предоставляет
/// автоматический выбор провайдера по пути.
/// Stores registered IContentProviders and provides automatic provider selection by path.
///
/// Провайдеры ищутся в порядке регистрации (последний зарегистрированный — первый проверяется).
/// Providers are searched in registration order (last registered checked first).
/// Используйте <see cref="Instance"/> для доступа к глобальному реестру.
/// Use Instance for access to the global registry.
/// </summary>
public sealed class ContentProviderRegistry
{
    /// <summary>Глобальный экземпляр реестра. / Global registry instance.</summary>
    public static ContentProviderRegistry Instance { get; } = new();

    private readonly List<IContentProvider> _providers = new();
    private readonly object _lock = new();

    /// <summary>
    /// Зарегистрированные провайдеры (только для чтения).
    /// Registered providers (read-only).
    /// </summary>
    public IReadOnlyList<IContentProvider> Providers
    {
        get
        {
            lock (_lock) return _providers.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Регистрирует провайдер. Повторная регистрация одного и того же экземпляра игнорируется.
    /// Registers a provider. Duplicate registration of the same instance is ignored.
    /// </summary>
    /// <param name="provider">Провайдер для регистрации. / Provider to register.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="provider"/> равен null.</exception>
    public void Register(IContentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (_lock)
        {
            if (_providers.Contains(provider))
            {
                LogService.Warn(
                    $"Provider '{provider.Name}' already registered, skipping",
                    nameof(ContentProviderRegistry));
                return;
            }
            _providers.Add(provider);
            LogService.Info(
                $"Registered content provider: {provider.Name}",
                nameof(ContentProviderRegistry));
        }
    }

    /// <summary>
    /// Удаляет провайдер из реестра.
    /// Removes a provider from the registry.
    /// </summary>
    /// <param name="provider">Провайдер для удаления. / Provider to remove.</param>
    /// <returns><c>true</c>, если провайдер был найден и удалён.</returns>
    public bool Unregister(IContentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (_lock)
        {
            var removed = _providers.Remove(provider);
            if (removed)
                LogService.Info(
                    $"Unregistered content provider: {provider.Name}",
                    nameof(ContentProviderRegistry));
            return removed;
        }
    }

    /// <summary>
    /// Возвращает первый провайдер, способный обработать указанный путь.
    /// Returns the first provider that can handle the specified path.
    /// Перебор идёт от последнего зарегистрированного к первому
    /// (позволяет переопределять поведение через позднюю регистрацию).
    /// Iterates from last registered to first (allows behavior override via late registration).
    /// </summary>
    /// <param name="path">Путь к файлу или каталогу. / Path to file or directory.</param>
    /// <returns>Подходящий провайдер или <c>null</c>, если ни один не подходит.</returns>
    public IContentProvider? GetProvider(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        lock (_lock)
        {
            for (int i = _providers.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (_providers[i].CanHandle(path))
                        return _providers[i];
                }
                catch (Exception ex)
                {
                    LogService.Warn(
                        $"Provider '{_providers[i].Name}' threw during CanHandle('{path}'): {ex.Message}",
                        nameof(ContentProviderRegistry));
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Регистрирует набор провайдеров по умолчанию (Local + Archive).
    /// Registers default set of providers (Local + Archive).
    /// Вызывается при старте приложения.
    /// Called at application startup.
    /// </summary>
    public void RegisterDefaults()
    {
        Register(new Providers.FileContentProvider());
        Register(new Providers.ArchiveContentProvider());
    }
}
