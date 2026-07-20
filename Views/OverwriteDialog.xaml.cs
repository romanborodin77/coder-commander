using System.IO;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Services;
using OverwritePolicy = CoderCommander.Operations.OverwritePolicy;

namespace CoderCommander.Views;

/// <summary>
/// Модальный диалог разрешения конфликта перезаписи файла.
/// Показывает информацию об источнике/назначении и предоставляет варианты действий.
/// Modal dialog for resolving file overwrite conflicts.
/// Shows source/destination info and provides action options.
/// </summary>
public partial class OverwriteDialog : Window
{
    /// <summary>
    /// Результат выбора пользователя. / User's chosen policy.
    /// </summary>
    public OverwritePolicy Result { get; private set; } = OverwritePolicy.Skip;

    /// <summary>
    /// Флаг «применить ко всем». / "Apply to all" flag.
    /// </summary>
    public bool ApplyToAll { get; private set; }

    /// <summary>
    /// Создаёт диалог перезаписи. / Creates the overwrite dialog.
    /// </summary>
    /// <param name="fileName">Имя файла. / File name.</param>
    /// <param name="sourcePath">Путь источника. / Source path.</param>
    /// <param name="destPath">Путь назначения. / Destination path.</param>
    public OverwriteDialog(string fileName, string sourcePath, string destPath)
    {
        InitializeComponent();

        var sourceSizeInfo = "";
        var sourceDateInfo = "";
        var destSizeInfo = "";
        var destDateInfo = "";

        try
        {
            if (File.Exists(sourcePath))
            {
                var fi = new FileInfo(sourcePath);
                sourceSizeInfo = string.Format(LocalizationService.Current.GetString("OpDlg.Overwrite.SizeInfo"), FormatSize(fi.Length));
                sourceDateInfo = string.Format(LocalizationService.Current.GetString("OpDlg.Overwrite.DateInfo"), fi.LastWriteTime.ToString("g"));
            }
        }
        catch { }

        try
        {
            if (File.Exists(destPath))
            {
                var fi = new FileInfo(destPath);
                destSizeInfo = string.Format(LocalizationService.Current.GetString("OpDlg.Overwrite.SizeInfo"), FormatSize(fi.Length));
                destDateInfo = string.Format(LocalizationService.Current.GetString("OpDlg.Overwrite.DateInfo"), fi.LastWriteTime.ToString("g"));
            }
        }
        catch { }

        var vm = new OverwriteDialogViewModel(fileName, sourcePath, destPath,
            sourceSizeInfo, sourceDateInfo, destSizeInfo, destDateInfo, this);
        DataContext = vm;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    /// <summary>
    /// Обработчик нажатия левой кнопки мыши на заголовке: перетаскивание окна.
    /// Handles left mouse button press on title bar: drag-move.
    /// </summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    /// <summary>
    /// Обработчик кнопки «Закрыть»: закрывает диалог с результатом Skip.
    /// Handles the "Close" button: closes dialog with Skip result.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Result = OverwritePolicy.Skip;
        DialogResult = false;
    }

    /// <summary>
    /// Устанавливает результат и закрывает диалог. / Sets the result and closes dialog.
    /// </summary>
    internal void SetResult(OverwritePolicy policy, bool applyToAll)
    {
        Result = policy;
        ApplyToAll = applyToAll;
        DialogResult = true;
    }
}

/// <summary>
/// ViewModel диалога перезаписи. / Overwrite dialog ViewModel.
/// </summary>
public partial class OverwriteDialogViewModel : ObservableObject
{
    private readonly OverwriteDialog _window;

    /// <summary>Имя файла. / File name.</summary>
    [ObservableProperty] private string _fileName;

    /// <summary>Информация об источнике. / Source info.</summary>
    [ObservableProperty] private string _sourceInfo;

    /// <summary>Информация о назначении. / Destination info.</summary>
    [ObservableProperty] private string _destInfo;

    /// <summary>Размер источника. / Source size info.</summary>
    [ObservableProperty] private string _sourceSizeInfo = "";

    /// <summary>Дата источника. / Source date info.</summary>
    [ObservableProperty] private string _sourceDateInfo = "";

    /// <summary>Размер назначения. / Destination size info.</summary>
    [ObservableProperty] private string _destSizeInfo = "";

    /// <summary>Дата назначения. / Destination date info.</summary>
    [ObservableProperty] private string _destDateInfo = "";

    /// <summary>Флаг «применить ко всем». / Apply to all flag.</summary>
    [ObservableProperty] private bool _applyToAll;

    /// <summary>
    /// Конструктор VM. / ViewModel constructor.
    /// </summary>
    public OverwriteDialogViewModel(string fileName, string sourceInfo, string destInfo,
        string sourceSizeInfo, string sourceDateInfo, string destSizeInfo, string destDateInfo,
        OverwriteDialog window)
    {
        _fileName = fileName;
        _sourceInfo = sourceInfo;
        _destInfo = destInfo;
        _sourceSizeInfo = sourceSizeInfo;
        _sourceDateInfo = sourceDateInfo;
        _destSizeInfo = destSizeInfo;
        _destDateInfo = destDateInfo;
        _window = window;
    }

    /// <summary>Команда «Пропустить». / Skip command.</summary>
    [RelayCommand]
    private void Skip() => _window.SetResult(OverwritePolicy.Skip, ApplyToAll);

    /// <summary>Команда «Пропустить все». / Skip all command.</summary>
    [RelayCommand]
    private void SkipAll() => _window.SetResult(OverwritePolicy.Skip, true);

    /// <summary>Команда «Перезаписать». / Overwrite command.</summary>
    [RelayCommand]
    private void Overwrite() => _window.SetResult(OverwritePolicy.Overwrite, ApplyToAll);

    /// <summary>Команда «Перезаписать все». / Overwrite all command.</summary>
    [RelayCommand]
    private void OverwriteAll() => _window.SetResult(OverwritePolicy.Overwrite, true);

    /// <summary>Команда «Перезаписать, если старше». / Overwrite if older command.</summary>
    [RelayCommand]
    private void OverwriteOlder() => _window.SetResult(OverwritePolicy.OverwriteOlder, ApplyToAll);

    /// <summary>Команда «Авто-переименование». / Auto-rename command.</summary>
    [RelayCommand]
    private void AutoRename() => _window.SetResult(OverwritePolicy.AutoRename, ApplyToAll);

    /// <summary>Команда «Прервать». / Abort command.</summary>
    [RelayCommand]
    private void Abort() => _window.SetResult(OverwritePolicy.Skip, false);
}
