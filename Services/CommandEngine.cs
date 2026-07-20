using System.Collections.ObjectModel;

namespace CoderCommander.Services;

/// <summary>
/// Представляет одну быструю команду, доступную в палитре (Ctrl+P).
/// Represents a single quick command available in the palette (Ctrl+P).
/// </summary>
public sealed class QuickCommand
{
    /// <summary>
    /// Название команды. / Command name.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Описание команды. / Command description.
    /// </summary>
    public string Description { get; }
    /// <summary>
    /// Асинхронный делегат выполнения команды. / Async execution delegate.
    /// </summary>
    public Func<CancellationToken, Task<string>> Execute { get; }

    /// <summary>
    /// Создаёт новую быструю команду.
    /// Creates a new quick command.
    /// </summary>
    /// <param name="name">Название. / Name.</param>
    /// <param name="description">Описание. / Description.</param>
    /// <param name="execute">Функция выполнения. / Execution function.</param>
    public QuickCommand(string name, string description, Func<CancellationToken, Task<string>> execute)
        => (Name, Description, Execute) = (name, description, execute);

    /// <summary>
    /// Возвращает название команды как строковое представление.
    /// Returns the command name as the string representation.
    /// </summary>
    public override string ToString() => Name;
}

/// <summary>
/// Движок быстрых команд: регистрация, поиск и выполнение.
/// Quick command engine: registration, search, and execution.
/// </summary>
public sealed class CommandEngine
{
    private readonly List<QuickCommand> _commands = new();

    /// <summary>
    /// Доступный список зарегистрированных команд (только для чтения).
    /// Read-only list of registered commands.
    /// </summary>
    public IReadOnlyList<QuickCommand> Commands => _commands;

    /// <summary>
    /// Регистрирует новую быструю команду.
    /// Registers a new quick command.
    /// </summary>
    /// <param name="cmd">Команда для регистрации. / Command to register.</param>
    public void Register(QuickCommand cmd) => _commands.Add(cmd);

    /// <summary>
    /// Асинхронно выполняет указанную быструю команду.
    /// Asynchronously executes the specified quick command.
    /// </summary>
    /// <param name="cmd">Команда. / Command.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения (или сообщение об ошибке). / Execution result (or error message).</returns>
    public async Task<string> RunAsync(QuickCommand cmd, CancellationToken ct = default)
    {
        try { return await cmd.Execute(ct); }
        catch (Exception e) { return "Ошибка: " + e.Message; }
    }

    /// <summary>
    /// Ищет команду по точному имени (регистронезависимо).
    /// Finds a command by exact name (case-insensitive).
    /// </summary>
    /// <param name="name">Имя команды. / Command name.</param>
    /// <returns>Найденная команда или null. / Found command or null.</returns>
    public QuickCommand? FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _commands.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ищет команды по имени и описанию (регистронезависимо).
    /// Searches commands by name and description (case-insensitive).
    /// </summary>
    /// <param name="query">Строка поиска. / Search query.</param>
    /// <returns>Список подходящих команд. / List of matching commands.</returns>
    public List<QuickCommand> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return _commands.ToList();
        return _commands.Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                                 || c.Description.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
