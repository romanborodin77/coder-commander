using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Controls;
using CoderCommander.Converters;
using CoderCommander.Operations;
using CoderCommander.Services;

namespace CoderCommander.Views;

/// <summary>
/// Частичное определение окна сравнения: ленивая загрузка бинарного (hex) режима и
/// навигация по отличиям (ph3.2, exp.yml). / Partial DiffWindow: lazy binary (hex)
/// mode loading and difference navigation (ph3.2, exp.yml).
/// </summary>
public partial class DiffWindow
{
    private bool _binaryLoaded;

    /// <summary>
    /// Подключает обработчик переключения вкладок и, если оба файла бинарные,
    /// сразу открывает hex-сравнение. / Wires tab switching and, if both files are
    /// binary, immediately opens the hex comparison.
    /// </summary>
    private void WireBinaryEvents()
    {
        DiffTabControl.SelectionChanged += DiffTabControl_SelectionChanged;
        if (_vm.AreBothBinary)
        {
            DiffTabControl.SelectedItem = BinaryTab;
            _ = LoadBinaryAsync();
        }
    }

    private void DiffTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(DiffTabControl.SelectedItem, BinaryTab))
            _ = LoadBinaryAsync();
    }

    /// <summary>
    /// Выполняет потоковое бинарное сравнение (один раз) и инициализирует hex-вьювер.
    /// Runs the streaming binary comparison (once) and initializes the hex viewer.
    /// </summary>
    private async Task LoadBinaryAsync()
    {
        if (_binaryLoaded) return;
        _binaryLoaded = true;
        try
        {
            var result = await BinaryDiffHelper.CompareAsync(_vm.LeftPath, _vm.RightPath);
            HexViewerControl.Initialize(_vm.LeftPath, _vm.RightPath, result.Ranges);
            _vm.BinarySummary = BuildSummary(result);
            HexViewerControl.Summary = _vm.BinarySummary;
        }
        catch (Exception ex)
        {
            _vm.BinarySummary = string.Format(LocalizationService.Current.GetString("Diff.BinaryCompareError"), ex.Message);
            HexViewerControl.Summary = _vm.BinarySummary;
        }
    }

    /// <summary>Формирует русскоязычную сводку бинарного сравнения. / Builds the RU summary of the binary comparison.</summary>
    private static string BuildSummary(BinaryDiffResult r)
    {
        if (r.Identical) return LocalizationService.Current.GetString("Hex.Identical");
        var conv = new FileSizeConverter();
        var ls = (string?)conv.Convert(r.LeftSize, typeof(string), null, CultureInfo.CurrentCulture) ?? "";
        var rs = (string?)conv.Convert(r.RightSize, typeof(string), null, CultureInfo.CurrentCulture) ?? "";
        return string.Format(LocalizationService.Current.GetString("Hex.Status"), ls, rs, r.DiffBytes, r.RangeCount);
    }
}
