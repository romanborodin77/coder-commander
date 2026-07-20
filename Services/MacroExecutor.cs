using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.Models;

namespace CoderCommander.Services;

/// <summary>
/// Исполнитель макросов: выполняет шаги последовательно, логируя результат.
/// Macro executor: runs steps sequentially and logs the results.
/// </summary>
public sealed class MacroExecutor
{
    private readonly CommandEngine _engine;

    /// <summary>
    /// Создаёт исполнитель с указанным движком команд.
    /// Creates an executor with the specified command engine.
    /// </summary>
    /// <param name="engine">Движок команд (CommandEngine). / Command engine.</param>
    public MacroExecutor(CommandEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Выполняет все шаги макроса. / Executes all macro steps.
    /// </summary>
    /// <param name="macro">Макрос для выполнения. / Macro to execute.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Суммарное сообщение о выполнении. / Aggregated execution message.</returns>
    public async Task<string> ExecuteAsync(MacroItem macro, CancellationToken ct = default)
    {
        if (macro.Steps.Count == 0)
            return LocalizationService.Current.GetString("Macro.NoSteps");

        var results = new List<string>();
        var sorted = new List<MacroStep>(macro.Steps);
        sorted.Sort((a, b) => a.Order.CompareTo(b.Order));

        foreach (var step in sorted)
        {
            ct.ThrowIfCancellationRequested();
            var result = await ExecuteStepAsync(step, ct);
            results.Add(result);
        }

        return string.Format(
            LocalizationService.Current.GetString("Macro.ExecuteDone"),
            sorted.Count) + (results.Count > 0 ? ": " + string.Join("; ", results) : "");
    }

    /// <summary>
    /// Выполняет один шаг макроса. / Executes a single macro step.
    /// </summary>
    /// <param name="step">Шаг макроса. / Macro step.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения шага. / Step execution result.</returns>
    public async Task<string> ExecuteStepAsync(MacroStep step, CancellationToken ct = default)
    {
        var cmd = _engine.FindByName(step.CommandName);
        if (cmd is null)
            return string.Format(LocalizationService.Current.GetString("Macro.NoCommand"), step.CommandName);

        try
        {
            return await _engine.RunAsync(cmd, ct);
        }
        catch (Exception ex)
        {
            return string.Format(LocalizationService.Current.GetString("Macro.ExecuteError"), ex.Message);
        }
    }
}
