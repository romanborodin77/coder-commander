using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Models;
using CoderCommander.Services;
using Microsoft.Win32;

namespace CoderCommander.ViewModels;

/// <summary>
/// ViewModel для диалога «Открыть как» (ph5.5): выбор приложения из списка
/// ассоциированных программ для открытия файла.
/// ViewModel for "Open With" dialog (ph5.5): selecting an application from the list
/// of associated programs to open a file.
/// </summary>
public partial class OpenWithViewModel : ObservableObject
{
    /// <summary>
    /// Путь к файлу, для которого подбирается приложение.
    /// Path to the file for which the application is being chosen.
    /// </summary>
    [ObservableProperty]
    private string _selectedFile = string.Empty;

    /// <summary>
    /// Список доступных приложений.
    /// List of available applications.
    /// </summary>
    public ObservableCollection<OpenWithApp> AvailableApps { get; } = [];

    /// <summary>
    /// Результат выбора: путь к приложению или null при отмене.
    /// Selection result: path to the application or null if cancelled.
    /// </summary>
    public string? ResultAppPath { get; private set; }

    /// <summary>
    /// Флаг: установить выбранное приложение по умолчанию для данного типа файла.
    /// Flag: set the selected application as default for this file type.
    /// </summary>
    [ObservableProperty]
    private bool _setAsDefault;

    /// <summary>
    /// Загружает список ассоциированных приложений для указанного файла.
    /// Loads the list of associated applications for the specified file.
    /// </summary>
    /// <param name="filePath">Путь к файлу. / Path to the file.</param>
    public void LoadApps(string filePath)
    {
        SelectedFile = filePath;
        AvailableApps.Clear();

        foreach (var app in OpenWithService.GetAssociatedApps(filePath))
        {
            AvailableApps.Add(app);
        }

        // Автоматически выбираем первое приложение
        // Auto-select the first application
        if (AvailableApps.Count > 0)
            SelectedApp = AvailableApps[0];
    }

    [ObservableProperty]
    private OpenWithApp? _selectedApp;

    /// <summary>
    /// Открывает файл выбранным приложением и закрывает диалог.
    /// Opens the file with the selected application and closes the dialog.
    /// </summary>
    [RelayCommand]
    private void OpenWithSelected(Window window)
    {
        if (SelectedApp is null) return;

        ResultAppPath = SelectedApp.Path;

        if (SetAsDefault)
        {
            OpenWithService.AddRecentApp(SelectedApp.Path);
        }

        window.DialogResult = true;
        window.Close();
    }

    /// <summary>
    /// Открывает диалог выбора .exe файла вручную.
    /// Opens a dialog to manually select an .exe file.
    /// </summary>
    [RelayCommand]
    private void BrowseForApp()
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Current.GetString("OpenWith.BrowseTitle"),
            Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
            FilterIndex = 1
        };

        if (dialog.ShowDialog() == true)
        {
            var app = new OpenWithApp
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
                Path = dialog.FileName
            };

            AvailableApps.Add(app);
            SelectedApp = app;
        }
    }

    /// <summary>
    /// Закрывает диалог без выбора.
    /// Closes the dialog without selection.
    /// </summary>
    [RelayCommand]
    private void Cancel(Window window)
    {
        ResultAppPath = null;
        window.DialogResult = false;
        window.Close();
    }
}
