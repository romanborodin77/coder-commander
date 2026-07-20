using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Operations;
using CoderCommander.Services;

namespace CoderCommander.ViewModels;

/// <summary>
/// Минимальная точка входа (ViewModel-команда) для расчёта и проверки контрольных сумм
/// по выделенным файлам (ph1.2, exp.yml). Без полноценного UI-диалога — механизм
/// полностью рабочий и собираемый; UI подключается позже.
/// Minimal entry point (ViewModel command) to calculate/verify checksums for selected
/// files (ph1.2). No full UI dialog yet — the mechanism is fully functional and buildable;
/// the UI is wired later.
/// </summary>
public partial class ChecksumViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;

    /// <summary>Выбранный алгоритм. / Selected algorithm.</summary>
    [ObservableProperty]
    private ChecksumAlgorithm _algorithm = ChecksumAlgorithm.SHA256;

    /// <summary>Статус/сообщение для UI. / Status message for UI.</summary>
    [ObservableProperty]
    private string _status = LocalizationService.Current.GetString("Checksum.Ready");

    /// <summary>Результаты расчёта/проверки. / Calculation/verification results.</summary>
    public ObservableCollection<ChecksumResult> Results { get; } = new();

    /// <summary>Путь экспорта sum-файла (необязательно). / Export sum-file path (optional).</summary>
    [ObservableProperty]
    private string? _exportPath;

    /// <summary>Число несовпадений при проверке. / Mismatch count on verify.</summary>
    [ObservableProperty]
    private int _mismatchCount;

    /// <summary>
    /// Рассчитывает контрольные суммы для набора файлов (опционально экспортирует в sum-файл).
    /// Calculates checksums for a set of files (optionally exporting to a sum-file).
    /// </summary>
    /// <param name="files">Выделенные файлы. / Selected files.</param>
    [RelayCommand]
    private async Task CalculateAsync(IEnumerable<string> files)
    {
        var list = files as IReadOnlyList<string> ?? files.ToList();
        if (list.Count == 0) { Status = LocalizationService.Current.GetString("Checksum.NoFiles"); return; }

        Results.Clear();
        _cts = new CancellationTokenSource();
        Status = string.Format(LocalizationService.Current.GetString("Checksum.Calculating"), Algorithm);
        var op = new ChecksumOperation(Algorithm, list, progress: null, exportPath: ExportPath);
        await op.ExecuteAsync(_cts.Token);

        foreach (var r in op.Results) Results.Add(r);
        Status = ExportPath != null
            ? string.Format(LocalizationService.Current.GetString("Checksum.DoneExport"), op.Results.Count, ExportPath)
            : string.Format(LocalizationService.Current.GetString("Checksum.Done"), op.Results.Count);
    }

    /// <summary>
    /// Проверяет файлы по sum-файлу (пересчёт + сравнение).
    /// Verifies files against a sum-file (recompute + compare).
    /// </summary>
    /// <param name="sumFile">Путь к sum-файлу. / Path to the sum-file.</param>
    [RelayCommand]
    private async Task VerifyAsync(string sumFile)
    {
        if (string.IsNullOrWhiteSpace(sumFile) || !File.Exists(sumFile))
        { Status = LocalizationService.Current.GetString("Checksum.SumNotFound"); return; }

        Results.Clear();
        MismatchCount = 0;
        _cts = new CancellationTokenSource();
        Status = string.Format(LocalizationService.Current.GetString("Checksum.Verifying"), Path.GetFileName(sumFile));
        var op = new ChecksumOperation(Algorithm, new[] { sumFile }, verifyPath: sumFile);
        await op.ExecuteAsync(_cts.Token);

        foreach (var r in op.Results) Results.Add(r);
        MismatchCount = op.Mismatches.Count;
        Status = op.Mismatches.Count == 0
            ? string.Format(LocalizationService.Current.GetString("Checksum.AllMatch"), op.Results.Count)
            : string.Format(LocalizationService.Current.GetString("Checksum.Mismatches"), op.Mismatches.Count, op.Results.Count);
    }

    /// <summary>Отменяет текущую операцию. / Cancels the current operation.</summary>
    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}
