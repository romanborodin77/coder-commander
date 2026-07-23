using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.FileSystem;
using CoderCommander.Operations;
using CoderCommander.Services;

namespace CoderCommander.Views;

/// <summary>
/// ViewModel окна управления архивами (ph5.1, exp.yml).
/// Archive management window ViewModel (ph5.1).
/// Два режима: «Создать архив» (Create) и «Извлечь архив» (Extract).
/// Two modes: "Create archive" and "Extract archive".
/// Работает через IFileSystem (LocalFileSystem), SharpCompress WriterFactory/ArchiveFactory.
/// Works via IFileSystem (LocalFileSystem), SharpCompress WriterFactory/ArchiveFactory.
/// </summary>
public partial class ArchiveWindowViewModel : ObservableObject
{
    private readonly IFileSystem _fs = LocalFileSystem.Instance;
    private CancellationTokenSource? _cts;

    // ═══════════════════════════════════════
    // Общие свойства / Shared properties
    // ═══════════════════════════════════════

    /// <summary>Текущий индекс вкладки (0=Создать, 1=Извлечь). / Current tab index (0=Create, 1=Extract).</summary>
    [ObservableProperty] private int _selectedTabIndex;

    /// <summary>Статус/сообщение для UI. / Status message for UI.</summary>
    [ObservableProperty] private string _statusText = "";

    /// <summary>Прогресс (0-100). / Progress (0-100).</summary>
    [ObservableProperty] private double _progressValue;

    /// <summary>Идёт операция. / Operation is running.</summary>
    [ObservableProperty] private bool _isRunning;

    /// <summary>Флаг режима: true=создание, false=извлечение. / Mode flag: true=create, false=extract.</summary>
    [ObservableProperty] private bool _isCreateMode = true;

    /// <summary>Флаг режима: true=извлечение. / Mode flag: true=extract.</summary>
    [ObservableProperty] private bool _isExtractMode;

    // ═══════════════════════════════════════
    // Режим «Создать архив» / Create mode
    // ═══════════════════════════════════════

    /// <summary>Путь к создаваемому архиву. / Output archive path.</summary>
    [ObservableProperty] private string _outputPath = "";

    /// <summary>Индекс формата (0=ZIP). / Format index (0=ZIP).</summary>
    [ObservableProperty] private int _selectedFormatIndex;

    /// <summary>Индекс уровня сжатия (0=None, 1=Fast, 2=Optimal, 3=Best). / Compression level index.</summary>
    [ObservableProperty] private int _selectedCompressionIndex = 2;


    /// <summary>Файлы для архивации. / Files to archive.</summary>
    public ObservableCollection<string> CreateFiles { get; } = new();

    /// <summary>Пароль для шифрования создаваемого архива. / Password for encrypting the archive being created.</summary>
    [ObservableProperty] private string _password = "";

    /// <summary>Число файлов для архивации. / Number of files to archive.</summary>
    public int CreateFileCount => CreateFiles.Count;

    /// <summary>
    /// Отображаемое число файлов для архивации (форматированная строка).
    /// Display file count for archive (formatted string).
    /// </summary>
    public string CreateFileCountDisplay =>
        string.Format(L10n("Archive.FilesForArchive"), CreateFiles.Count);

    // ═══════════════════════════════════════
    // Режим «Извлечь архив» / Extract mode
    // ═══════════════════════════════════════

    /// <summary>Путь к архиву для извлечения. / Archive path to extract.</summary>
    [ObservableProperty] private string _extractArchivePath = "";

    /// <summary>Каталог назначения для извлечения. / Extraction destination directory.</summary>
    [ObservableProperty] private string _extractOutputPath = "";

    /// <summary>Пароль для расшифровки архива. / Password for decrypting the archive.</summary>
    [ObservableProperty] private string _extractPassword = "";


    /// <summary>Записи архива для отображения. / Archive entries for display.</summary>
    public ObservableCollection<ArchiveEntryItem> ExtractEntries { get; } = new();

    /// <summary>Число записей архива. / Number of archive entries.</summary>
    public int ExtractEntryCount => ExtractEntries.Count;

    /// <summary>
    /// Отображаемое число записей архива (форматированная строка).
    /// Display entry count in archive (formatted string).
    /// </summary>
    public string ExtractEntryCountDisplay =>
        string.Format(L10n("Archive.EntriesInArchive"), ExtractEntries.Count);

    /// <summary>
    /// Конструктор ViewModel. / Creates ViewModel.
    /// </summary>
    /// <param name="startMode">Режим: "create" или "extract". / Mode: "create" or "extract".</param>
    /// <param name="files">Файлы для архивации (режим create) или null. / Files to archive (create mode) or null.</param>
    /// <param name="archivePath">Путь к архиву (режим extract) или null. / Archive path (extract mode) or null.</param>
    public ArchiveWindowViewModel(string startMode = "create",
        IReadOnlyList<string>? files = null,
        string? archivePath = null)
    {
        if (startMode == "extract" && !string.IsNullOrEmpty(archivePath))
        {
            SelectedTabIndex = 1;
            IsCreateMode = false;
            IsExtractMode = true;
            ExtractArchivePath = archivePath;
            ExtractOutputPath = Path.GetDirectoryName(archivePath) ?? "";

            // Загружаем содержимое архива. / Load archive contents.
            _ = LoadArchiveContentsAsync();
        }
        else
        {
            SelectedTabIndex = 0;
            IsCreateMode = true;
            IsExtractMode = false;

            if (files is { Count: > 0 })
            {
                foreach (var f in files)
                    CreateFiles.Add(f);

                // Автоматически определяем базовый каталог и имя архива.
                // Auto-detect base directory and archive name.
                var baseDir = DetermineBaseDirectory(files);
                var defaultName = Path.GetFileName(baseDir);
                OutputPath = Path.Combine(baseDir, $"{defaultName}.zip");

                OnPropertyChanged(nameof(CreateFileCount));
                OnPropertyChanged(nameof(CreateFileCountDisplay));
            }
        }

        StatusText = L10n("Archive.Ready");
    }

    // ═══════════════════════════════════════
    // Команды / Commands
    // ═══════════════════════════════════════

    /// <summary>Очищает список файлов для архивации. / Clears file list for archiving.</summary>
    [RelayCommand]
    private void ClearCreateFiles()
    {
        CreateFiles.Clear();
        OnPropertyChanged(nameof(CreateFileCount));
        OnPropertyChanged(nameof(CreateFileCountDisplay));
    }

    /// <summary>Выбирает путь назначения для создаваемого архива. / Browses for output archive path.</summary>
    [RelayCommand]
    private void BrowseOutputPath()
    {
        var ext = SelectedFormatIndex switch
        {
            0 => "*.zip",
            1 => "*.7z",
            2 => "*.tar",
            3 => "*.tar.gz",
            4 => "*.tar.bz2",
            5 => "*.gz",
            6 => "*.bz2",
            _ => "*.zip"
        };
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = L10n("Archive.SelectOutputPath"),
            Filter = $"{L10n("Archive.ArchiveFiles")} ({ext})|{ext}|{L10n("Archive.AllFiles")}|*.*",
            FileName = Path.GetFileName(OutputPath),
            InitialDirectory = Path.GetDirectoryName(OutputPath) ?? ""
        };
        if (dlg.ShowDialog() == true)
            OutputPath = dlg.FileName;
    }

    /// <summary>Выбирает путь для извлечения архива. / Browses for extraction output directory.</summary>
    [RelayCommand]
    private void BrowseExtractOutput()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = L10n("Archive.SelectExtractPath"),
            InitialDirectory = ExtractOutputPath
        };
        if (dlg.ShowDialog() == true)
            ExtractOutputPath = dlg.FolderName;
    }

    /// <summary>Отмечает все записи для извлечения. / Selects all entries for extraction.</summary>
    [RelayCommand]
    private void SelectAllEntries()
    {
        foreach (var entry in ExtractEntries)
            entry.IsSelected = true;
    }

    /// <summary>Снимает отметки со всех записей. / Deselects all entries.</summary>
    [RelayCommand]
    private void SelectNoneEntries()
    {
        foreach (var entry in ExtractEntries)
            entry.IsSelected = false;
    }

    /// <summary>
    /// Создаёт архив. / Creates archive.
    /// </summary>
    [RelayCommand]
    private async Task CreateArchiveAsync()
    {
        if (CreateFiles.Count == 0)
        {
            StatusText = L10n("Archive.NoFiles");
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            StatusText = L10n("Archive.NoOutputPath");
            return;
        }

        var format = SelectedFormatIndex switch
        {
            0 => ArchiveCreateOperation.ArchiveFormat.Zip,
            1 => ArchiveCreateOperation.ArchiveFormat.SevenZip,
            2 => ArchiveCreateOperation.ArchiveFormat.Tar,
            3 => ArchiveCreateOperation.ArchiveFormat.TarGz,
            4 => ArchiveCreateOperation.ArchiveFormat.TarBz2,
            5 => ArchiveCreateOperation.ArchiveFormat.GZip,
            6 => ArchiveCreateOperation.ArchiveFormat.BZip2,
            _ => ArchiveCreateOperation.ArchiveFormat.Zip
        };

        // Фильтруем несуществующие файлы. / Filter out non-existent files.
        var existingFiles = CreateFiles.Where(File.Exists).ToList();
        if (existingFiles.Count == 0)
        {
            StatusText = L10n("Archive.NoFiles");
            return;
        }
        if (existingFiles.Count < CreateFiles.Count)
        {
            StatusText = string.Format(L10n("Archive.SomeFilesMissing"), CreateFiles.Count - existingFiles.Count);
        }

        var compressionLevel = SelectedCompressionIndex switch
        {
            0 => ArchiveCreateOperation.CompressionLevel.None,
            1 => ArchiveCreateOperation.CompressionLevel.BestSpeed,
            2 => ArchiveCreateOperation.CompressionLevel.Optimal,
            3 => ArchiveCreateOperation.CompressionLevel.BestCompression,
            _ => ArchiveCreateOperation.CompressionLevel.Optimal
        };

        var baseDir = DetermineBaseDirectory(existingFiles);

        IsRunning = true;
        ProgressValue = 0;
        StatusText = L10n("Archive.Creating");
        _cts = new CancellationTokenSource();

        try
        {
            var op = new ArchiveCreateOperation(
                existingFiles,
                OutputPath,
                baseDir,
                format,
                compressionLevel,
                string.IsNullOrWhiteSpace(Password) ? null : Password,
                
                new Progress<OperationProgress>(p =>
                {
                    ProgressValue = p.Percent;
                    StatusText = p.ToString();
                }));

            await op.ExecuteAsync(_cts.Token).ConfigureAwait(false);

            StatusText = string.Format(L10n("Archive.CreateDone"), OutputPath);
            ProgressValue = 100;
            OnOperationCompleted?.Invoke();
        }
        catch (OperationCanceledException)
        {
            StatusText = L10n("Archive.Cancelled");
        }
        catch (Exception ex)
        {
            StatusText = string.Format(L10n("Status.Error"), ex.Message);
            LogService.Error($"Archive create failed: {ex.Message}",
                nameof(ArchiveWindowViewModel), ex);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Извлекает архив. / Extracts archive.
    /// </summary>
    [RelayCommand]
    private async Task ExtractArchiveAsync()
    {
        if (string.IsNullOrWhiteSpace(ExtractArchivePath) || !File.Exists(ExtractArchivePath))
        {
            StatusText = L10n("Archive.NoArchive");
            return;
        }

        if (string.IsNullOrWhiteSpace(ExtractOutputPath))
        {
            StatusText = L10n("Archive.NoExtractPath");
            return;
        }

        // Собираем отмеченные записи. / Collect selected entries.
        var selectedEntries = ExtractEntries
            .Where(e => e.IsSelected)
            .Select(e => e.Key)
            .ToList();

        bool extractAll = selectedEntries.Count == 0 || selectedEntries.Count == ExtractEntries.Count;

        IsRunning = true;
        ProgressValue = 0;
        StatusText = L10n("Archive.Extracting");
        _cts = new CancellationTokenSource();

        try
        {
            var op = new ArchiveExtractOperation(
                ExtractArchivePath,
                ExtractOutputPath,
                extractAll ? null : selectedEntries,
                overwrite: true,
                string.IsNullOrWhiteSpace(ExtractPassword) ? null : ExtractPassword,
                
                new Progress<OperationProgress>(p =>
                {
                    ProgressValue = p.Percent;
                    StatusText = p.ToString();
                }));

            await op.ExecuteAsync(_cts.Token).ConfigureAwait(false);

            StatusText = string.Format(L10n("Archive.ExtractDone"), ExtractOutputPath);
            ProgressValue = 100;
            OnOperationCompleted?.Invoke();
        }
        catch (OperationCanceledException)
        {
            StatusText = L10n("Archive.Cancelled");
        }
        catch (Exception ex)
        {
            StatusText = string.Format(L10n("Status.Error"), ex.Message);
            LogService.Error($"Archive extract failed: {ex.Message}",
                nameof(ArchiveWindowViewModel), ex);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>Отменяет текущую операцию или закрывает окно, если операции нет. / Cancels running operation or closes the window if none.</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (IsRunning)
        {
            _cts?.Cancel();
        }
        else
        {
            OnCloseRequested?.Invoke();
        }
    }

    /// <summary>Событие закрытия окна из ViewModel. / Window close request event from ViewModel.</summary>
    public event Action? OnCloseRequested;

    /// <summary>Событие завершения операции (для обновления панели). / Operation completed event (to refresh panel).</summary>
    public event Action? OnOperationCompleted;

    // ═══════════════════════════════════════
    // Вспомогательные методы / Helpers
    // ═══════════════════════════════════════

    /// <summary>
    /// Загружает содержимое архива для отображения в списке.
    /// Loads archive contents for display in the list.
    /// </summary>
    private async Task LoadArchiveContentsAsync()
    {
        if (string.IsNullOrWhiteSpace(ExtractArchivePath) || !File.Exists(ExtractArchivePath))
            return;

        StatusText = L10n("Archive.Loading");
        ExtractEntries.Clear();

        try
        {
            await foreach (var entry in ArchiveHelper.EnumerateEntriesAsync(ExtractArchivePath, password: string.IsNullOrWhiteSpace(ExtractPassword) ? null : ExtractPassword).ConfigureAwait(false))
            {
                ExtractEntries.Add(new ArchiveEntryItem
                {
                    Key = entry.Key,
                    Size = entry.Size,
                    CompressedSize = entry.CompressedSize,
                    LastModified = entry.LastModified.DateTime,
                    IsSelected = true
                });
            }

            OnPropertyChanged(nameof(ExtractEntryCount));
            OnPropertyChanged(nameof(ExtractEntryCountDisplay));
            StatusText = string.Format(L10n("Archive.EntriesLoaded"), ExtractEntries.Count);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(L10n("Status.Error"), ex.Message);
            LogService.Error($"Archive load failed: {ex.Message}",
                nameof(ArchiveWindowViewModel), ex);
        }
    }

    /// <summary>
    /// Определяет базовый каталог из списка файлов (наибольший общий путь).
    /// Determines base directory from file list (longest common path).
    /// </summary>
    private static string DetermineBaseDirectory(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        var dir = Path.GetDirectoryName(files[0]);
        if (string.IsNullOrEmpty(dir)) return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        while (dir is not null && files.Any(f => !f.StartsWith(dir, StringComparison.OrdinalIgnoreCase)))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? Path.GetDirectoryName(files[0]) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    private static string L10n(string key) => LocalizationService.Current.GetString(key);

    // ═══════════════════════════════════════
    // Свойства с уведомлением / Notifying properties
    // ═══════════════════════════════════════

    partial void OnSelectedTabIndexChanged(int value)
    {
        IsCreateMode = value == 0;
        IsExtractMode = value == 1;
    }

    /// <summary>
    /// Обновляет расширение имени архива при смене формата.
    /// Updates archive file extension when format changes.
    /// </summary>
    partial void OnSelectedFormatIndexChanged(int value)
    {
        if (string.IsNullOrWhiteSpace(OutputPath)) return;

        var dir = Path.GetDirectoryName(OutputPath) ?? "";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(OutputPath);

        // Для двойных расширений (.tar.gz и т.д.) — убираем составное расширение.
        // For compound extensions (.tar.gz etc.) — strip compound extension.
        var fileName = Path.GetFileName(OutputPath);
        var lower = fileName.ToLowerInvariant();
        if (lower.EndsWith(".tar.gz") || lower.EndsWith(".tar.bz2"))
        {
            var idx = fileName.LastIndexOf('.');
            if (idx > 0)
            {
                var idx2 = fileName.LastIndexOf('.', idx - 1);
                if (idx2 > 0)
                    nameWithoutExt = fileName[..idx2];
            }
        }

        var ext = value switch
        {
            0 => ".zip",
            1 => ".7z",
            2 => ".tar",
            3 => ".tar.gz",
            4 => ".tar.bz2",
            5 => ".gz",
            6 => ".bz2",
            _ => ".zip"
        };

        OutputPath = Path.Combine(dir, nameWithoutExt + ext);
    }
}

/// <summary>
/// Элемент записи архива для отображения в списке (чекбокс + метаданные).
/// Archive entry item for display in the list (checkbox + metadata).
/// </summary>
public sealed partial class ArchiveEntryItem : ObservableObject
{
    /// <summary>Ключ (путь внутри архива). / Key (path inside archive).</summary>
    public string Key { get; init; } = "";

    /// <summary>Размер распакованный, байты. / Uncompressed size, bytes.</summary>
    public long Size { get; init; }

    /// <summary>Размер сжатый, байты. / Compressed size, bytes.</summary>
    public long CompressedSize { get; init; }

    /// <summary>Дата последнего изменения. / Last modification date.</summary>
    public DateTime LastModified { get; init; }

    /// <summary>Отмечен для извлечения. / Marked for extraction.</summary>
    [ObservableProperty] private bool _isSelected = true;

    /// <summary>Отображаемое имя (имя файла). / Display name (file name).</summary>
    public string DisplayName => Path.GetFileName(Key);

    /// <summary>Строка размера для отображения. / Size display string.</summary>
    public string SizeDisplay => FormatSize(Size);

    /// <summary>Строка сжатого размера. / Compressed size string.</summary>
    public string CompressedDisplay => FormatSize(CompressedSize);

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        >= 1L << 20 => $"{bytes / (1024.0 * 1024):F2} MB",
        >= 1L << 10 => $"{bytes / 1024.0:F2} KB",
        _ => $"{bytes} B"
    };
}

/// <summary>
/// Окно управления архивами (ph5.1, exp.yml). Модальное, кастомным заголовком (WindowStyle=None).
/// Archive management window (ph5.1). Modal, custom titlebar (WindowStyle=None).
/// Связывается <see cref="ArchiveWindowViewModel"/> через DataContext.
/// Binds ArchiveWindowViewModel via DataContext.
/// </summary>
public partial class ArchiveWindow : Window
{
    private readonly ArchiveWindowViewModel _vm;

    /// <summary>ViewModel окна (для внешних подписок). / Window ViewModel (for external subscriptions).</summary>
    internal ArchiveWindowViewModel ViewModel => _vm;

    /// <summary>
    /// Конструктор окна: создаёт ViewModel, устанавливает DataContext.
    /// Window constructor: creates ViewModel, sets DataContext.
    /// </summary>
    /// <param name="mode">Режим: "create" или "extract". / Mode: "create" or "extract".</param>
    /// <param name="files">Файлы для архивации. / Files to archive.</param>
    /// <param name="archivePath">Путь к архиву для извлечения. / Archive path to extract.</param>
    public ArchiveWindow(string mode = "create",
        IReadOnlyList<string>? files = null,
        string? archivePath = null)
    {
        InitializeComponent();
        _vm = new ArchiveWindowViewModel(mode, files, archivePath);
        _vm.OnCloseRequested += () => Close();
        DataContext = _vm;

        ArchivePasswordBox.PasswordChanged += (s, e) => _vm.Password = ArchivePasswordBox.Password;
        ExtractPasswordBox.PasswordChanged += (s, e) => _vm.ExtractPassword = ExtractPasswordBox.Password;
    }

    #region Заголовок окна / Window chrome handlers

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else
                DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    #endregion
}
