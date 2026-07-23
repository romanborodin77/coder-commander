using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Operations;
using CoderCommander.Services;

namespace CoderCommander.ViewModels;

/// <summary>
/// Частичная ViewModel: команды «Разделить / Соединить файл» (ph1.3, exp.yml).
/// Partial ViewModel: "Split / Combine file" commands (ph1.3).
/// Точка интеграции: контекстное меню панели (FilePanel.xaml) и меню «Сервис»
/// в MainWindow.xaml биндятся на <c>SplitCommand</c> / <c>CombineCommand</c>.
/// Integration points: the panel context menu (FilePanel.xaml) and the "Tools"
/// menu in MainWindow.xaml bind to SplitCommand / CombineCommand.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Разбивает выбранные файлы активной панели на тома (TC-стиль .001/.002 …).
    /// Splits the selected files of the active panel into volumes (TC-style .001/.002 …).
    /// Запрашивает размер тома (например «100M») и пишет summary «size=…».
    /// Prompts for the volume size (e.g. "100M") and writes the "size=…" summary.
    /// </summary>
    [RelayCommand]
    private async Task SplitAsync()
    {
        var items = ActivePanel.GetSelectionOrCurrent()
            .Where(i => !i.IsParent && !i.IsDirectory)
            .ToList();
        if (items.Count == 0) { StatusText = L10n("Split.NoFiles"); return; }

        // Виртуальная ФС — не поддерживается.
        if (ActivePanel.VirtualFileSystem is not null)
        {
            StatusText = L10n("Split.VirtualNotSupported");
            return;
        }

        var sizeText = Prompt(L10n("Split.SizeTitle"), L10n("Split.SizePrompt"), "100M");
        if (sizeText is null) return;
        var volSize = ParseVolumeSize(sizeText);
        if (volSize is null or <= 0) { StatusText = L10n("Split.BadSize"); return; }

        var files = items.Select(i => i.FullPath).ToList();
        var op = new SplitOperation(files, volSize);

        var title = L10n("Split.Started");
        var progressVm = new OperationDialogViewModel(op, title, ActivePanel.CurrentPath, "");
        var progressDlg = new Views.OperationDialogWindow
        {
            DataContext = progressVm,
            Owner = Application.Current.MainWindow
        };

        IsBusy = true;
        try
        {
            var executeTask = Task.Run(() => op.ExecuteAsync(progressVm.CancellationToken));
            progressDlg.ShowDialog();
            try { await executeTask; } catch (OperationCanceledException) { }

            if (op.State == OperationState.Completed)
                StatusText = string.Format(L10n("Split.Done"), op.VolumesCreated);
            else if (op.State == OperationState.Canceled)
                StatusText = L10n("CrossVfs.Cancelled");
            else if (op.State == OperationState.Failed)
                StatusText = string.Format(L10n("Status.Error"), op.LastError?.Message ?? "Unknown");

            await ActivePanel.RefreshAsync();
        }
        catch (Exception ex) { StatusText = string.Format(L10n("Status.Error"), ex.Message); }
        finally { progressVm.Dispose(); IsBusy = false; ProgressValue = 0; ProgressText = ""; }
    }

    /// <summary>
    /// Склеивает тома (.001/.002 …) обратно в исходный файл по summary «size=…».
    /// Combines volumes (.001/.002 …) back into the source file using the "size=…" summary.
    /// </summary>
    [RelayCommand]
    private async Task CombineAsync()
    {
        var items = ActivePanel.GetSelectionOrCurrent()
            .Where(i => !i.IsParent && !i.IsDirectory)
            .ToList();

        // Для склейки достаточно выбрать summary (.sum) или первый том (.001).
        var target = items.Select(i => i.FullPath).FirstOrDefault(f =>
                        f.EndsWith(".sum", StringComparison.OrdinalIgnoreCase)
                     || VolumeNameRegex().IsMatch(Path.GetFileName(f)));
        if (target is null) { StatusText = L10n("Combine.NoFile"); return; }

        // Виртуальная ФС — не поддерживается.
        if (ActivePanel.VirtualFileSystem is not null)
        {
            StatusText = L10n("Combine.VirtualNotSupported");
            return;
        }

        var op = new CombineOperation(target);

        var title = L10n("Combine.Started");
        var progressVm = new OperationDialogViewModel(op, title, Path.GetDirectoryName(target) ?? "", op.OutputPath ?? "");
        var progressDlg = new Views.OperationDialogWindow
        {
            DataContext = progressVm,
            Owner = Application.Current.MainWindow
        };

        IsBusy = true;
        try
        {
            var executeTask = Task.Run(() => op.ExecuteAsync(progressVm.CancellationToken));
            progressDlg.ShowDialog();
            try { await executeTask; } catch (OperationCanceledException) { }

            if (op.State == OperationState.Completed)
                StatusText = string.Format(L10n("Combine.Done"), op.OutputPath, op.VolumesCombined);
            else if (op.State == OperationState.Canceled)
                StatusText = L10n("CrossVfs.Cancelled");
            else if (op.State == OperationState.Failed)
                StatusText = string.Format(L10n("Status.Error"), op.LastError?.Message ?? "Unknown");

            await ActivePanel.RefreshAsync();
        }
        catch (Exception ex) { StatusText = string.Format(L10n("Status.Error"), ex.Message); }
        finally { progressVm.Dispose(); IsBusy = false; ProgressValue = 0; ProgressText = ""; }
    }

    /// <summary>
    /// Разбирает строку размера тома: поддерживает суффиксы K/M/G/T (и B), десятичные доли.
    /// Parses a volume-size string: supports K/M/G/T (and B) suffixes and decimal fractions.
    /// Примеры: «100M» → 104857600, «1.5G» → 1610612736, «500K» → 512000.
    /// </summary>
    private static long? ParseVolumeSize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().ToUpperInvariant();
        long mult = 1;
        if (text.Length > 0)
        {
            var last = text[^1];
            if (last is 'B') { mult = 1; text = text.Substring(0, text.Length - 1); }
            else if (last is 'K') { mult = 1024L; text = text.Substring(0, text.Length - 1); }
            else if (last is 'M') { mult = 1024L * 1024; text = text.Substring(0, text.Length - 1); }
            else if (last is 'G') { mult = 1024L * 1024 * 1024; text = text.Substring(0, text.Length - 1); }
            else if (last is 'T') { mult = 1024L * 1024 * 1024 * 1024; text = text.Substring(0, text.Length - 1); }
        }
        if (!double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0)
            return null;
        return (long)(value * mult);
    }

    private static System.Text.RegularExpressions.Regex VolumeNameRegex()
        => new System.Text.RegularExpressions.Regex(@"^.+\.[0-9]+$", System.Text.RegularExpressions.RegexOptions.Compiled);
}
