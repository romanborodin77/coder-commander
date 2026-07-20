using System.Windows;
using CoderCommander.Operations;
using CoderCommander.ViewModels;
using CoderCommander.Views;
using OpOverwritePolicy = CoderCommander.Operations.OverwritePolicy;

namespace CoderCommander.Services;

/// <summary>
/// Сервис диалога прогресса операций: создаёт и показывает модальное окно прогресса,
/// связывает IFileOperation с UI через IProgress&lt;OperationProgress&gt;.
/// Operation dialog service: creates and shows the modal progress window,
/// binds IFileOperation to UI via IProgress&lt;OperationProgress&gt;.
/// Singleton, доступен через OperationDialogService.Current.
/// Singleton, accessible via OperationDialogService.Current.
/// </summary>
public sealed class OperationDialogService
{
    /// <summary>Текущий экземпляр синглтона. / Current singleton instance.</summary>
    public static OperationDialogService Current { get; } = new();

    /// <summary>
    /// Показывает диалог прогресса операции и выполняет её.
    /// Окно блокирует вызывающий поток (ShowDialog).
    /// Shows the operation progress dialog and executes the operation.
    /// The window blocks the calling thread (ShowDialog).
    /// </summary>
    /// <param name="operation">Файловая операция. / File operation.</param>
    /// <param name="title">Заголовок операции (Копирование, Перенос и т.д.). / Operation title.</param>
    /// <param name="sourcePath">Путь-источник (для отображения). / Source path (for display).</param>
    /// <param name="destPath">Путь назначения (для отображения). / Destination path (for display).</param>
    /// <returns>Состояние завершения операции. / Operation completion state.</returns>
    public OperationState ShowDialog(IFileOperation operation, string title, string sourcePath, string destPath)
    {
        var vm = new OperationDialogViewModel(operation, title, sourcePath, destPath);
        var window = new OperationDialogWindow { DataContext = vm, Owner = Application.Current.MainWindow };

        // Запускаем операцию в фоне. Task.Run(() => async) возвращает Task<Task>.
        // Start the operation in the background. Task.Run(() => async) returns Task<Task>.
        var outerTask = Task.Run(() => operation.ExecuteAsync(vm.CancellationToken));

        // Показываем диалог (блокирующий).
        // Show the dialog (blocking).
        window.ShowDialog();

        // После закрытия окна — дожидаемся завершения фоновой задачи и пробрасываем ошибки.
        // After window close — await the background task to ensure completion and propagate errors.
        try
        {
            outerTask.Wait();
        }
        catch (AggregateException ae)
        {
            var inner = ae.InnerException;
            if (inner is OperationCanceledException) { /* отмена — штатно / cancellation — expected */ }
            else if (inner is not null) throw inner;
        }

        vm.Dispose();

        return operation.State;
    }

    /// <summary>
    /// Создаёт колбэк разрешения конфликтов перезаписи, показывающий OverwriteDialog.
    /// Creates an overwrite conflict callback that shows OverwriteDialog.
    /// </summary>
    /// <param name="owner">Оwner-окно для модальности (null = MainWindow). / Owner window for modality.</param>
    /// <returns>Колбэк Func&lt;string, OverwritePolicy&gt;. / Callback Func.</returns>
    public Func<string, OpOverwritePolicy> CreateOverwriteCallback(Window? owner = null)
    {
        bool applyToAll = false;
        OpOverwritePolicy globalPolicy = OpOverwritePolicy.Skip;

        return (destPath) =>
        {
            if (applyToAll)
                return globalPolicy;

            OpOverwritePolicy result = OpOverwritePolicy.Skip;
            bool all = false;
            var ev = new System.Threading.ManualResetEventSlim(false);

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var fileName = System.IO.Path.GetFileName(destPath);
                string sourceInfo = "", destInfo = "";

                try
                {
                    if (System.IO.File.Exists(destPath))
                    {
                        var fi = new System.IO.FileInfo(destPath);
                        destInfo = $"{FormatSize(fi.Length)}, {fi.LastWriteTime:dd.MM.yyyy HH:mm}";
                    }
                }
                catch { /* ignore */ }

                var dlg = new OverwriteDialog(fileName, sourceInfo, destInfo)
                {
                    Owner = owner ?? Application.Current.MainWindow
                };

                if (dlg.ShowDialog() == true)
                {
                    result = dlg.Result;
                    all = dlg.ApplyToAll;
                }
            }));

            try { ev.Wait(); } catch { return OpOverwritePolicy.Skip; }
            if (all)
            {
                applyToAll = true;
                globalPolicy = result;
            }

            return result;
        };
    }

    /// <summary>
    /// Форматирует размер файла. / Formats file size.
    /// </summary>
    private static string FormatSize(long bytes)
    {
        var L = LocalizationService.Current;
        if (bytes < 1024) return $"{bytes} {L.GetString("Format.B")}";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} {L.GetString("Format.KB")}";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} {L.GetString("Format.MB")}";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} {L.GetString("Format.GB")}";
    }
}
