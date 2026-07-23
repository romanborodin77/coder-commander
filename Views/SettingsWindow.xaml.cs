using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.ViewModels;

namespace CoderCommander.Views;

/// <summary>
/// Окно настроек приложения: внешний вид, редактор, терминал, поведение.
/// Application settings window: appearance, editor, terminal, behavior.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    /// <summary>
    /// Конструктор, инициализирующий XAML-компоненты и создающий ViewModel.
    /// Constructor that initializes the XAML components and creates the ViewModel.
    /// </summary>
    public SettingsWindow()
    {
        InitializeComponent();
        _vm = new SettingsViewModel();
        DataContext = _vm;
    }

    /// <summary>
    /// Обработчик кнопки «Закрыть» — закрывает окно без сохранения.
    /// Handles the "Close" button — closes the window without saving.
    /// </summary>
    /// <param name="sender">Источник события. / Event source.</param>
    /// <param name="e">Данные события маршрутизации. / Routed event data.</param>
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Обработчик перетаскивания окна за заголовок (WindowChrome с CaptionHeight=0
    /// отключает системный drag, поэтому используем DragMove вручную).
    /// Title-bar drag handler: with CaptionHeight=0 the system drag is disabled,
    /// so we invoke DragMove() manually. The close button is excluded.
    /// </summary>
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Не начинаем перетаскивание, если клик пришёлся на кнопку закрытия.
        // Do not start a drag when the close button is the click source.
        if (e.OriginalSource is Button) return;
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    /// <summary>
    /// Обработчик кнопки «Сохранить» — сохраняет настройки в файл, применяет тему и закрывает окно.
    /// Handles the "Save" button — saves settings to file, applies the theme, and closes the window.
    /// </summary>
    /// <param name="sender">Источник события. / Event source.</param>
    /// <param name="e">Данные события маршрутизации. / Routed event data.</param>
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Load();
        _vm.SaveToSettings(s);
        SettingsService.Save(s);

        // Применяем тему
        ((App)Application.Current).ApplyTheme(s.Theme);

        // Применяем язык
        LocalizationService.Current.LoadLanguage(s.Language);

        // Применяем горячие клавиши (ph6.1)
        if (Application.Current.MainWindow is MainWindow mainWindow)
            mainWindow.ReapplyHotkeys();

        // Применяем шрифт панели (ph6.5)
        Application.Current.Resources["PanelFontFamily"] = new System.Windows.Media.FontFamily(s.PanelFontFamily);
        Application.Current.Resources["PanelFontSize"] = s.PanelFontSize;

        StyledMessageBoxWindow.Show(LocalizationService.Current.GetString("Settings.Saved"), LocalizationService.Current.GetString("Settings.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    /// <summary>
    /// Обработчик кнопки «Сброс» — восстанавливает настройки по умолчанию после подтверждения.
    /// Handles the "Reset" button — restores default settings after confirmation.
    /// </summary>
    /// <param name="sender">Источник события. / Event source.</param>
    /// <param name="e">Данные события маршрутизации. / Routed event data.</param>
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var result = StyledMessageBoxWindow.Show(
            LocalizationService.Current.GetString("Settings.ResetConfirm"),
            LocalizationService.Current.GetString("Settings.ResetTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _vm.ResetToDefaults();
        }
    }

    /// <summary>
    /// Обработчик кнопки «Изменить» для горячей клавиши: начинает захват.
    /// Handles the "Change" button for a hotkey: starts key capture.
    /// </summary>
    private void HotkeyChange_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is HotkeyItem item)
        {
            _vm.HotkeysVM.StartCapture(item);
            HotkeyStatusText.Text = LocalizationService.Current.GetString("Hotkey.Capturing");
        }
    }

    /// <summary>
    /// Обработчик кнопки «Сбросить» горячие клавиши.
    /// Handles the "Reset" hotkeys button.
    /// </summary>
    private void HotkeyReset_Click(object sender, RoutedEventArgs e)
    {
        var result = StyledMessageBoxWindow.Show(
            LocalizationService.Current.GetString("Hotkey.ResetConfirm"),
            LocalizationService.Current.GetString("Settings.Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _vm.HotkeysVM.ResetToDefaults();
            HotkeyStatusText.Text = "";
        }
    }

    /// <summary>
    /// Обработчик кнопки «Обзор» для 7-Zip — открывает диалог выбора 7z.exe.
    /// Browse button handler for 7-Zip — opens file dialog to select 7z.exe.
    /// </summary>
    private void BrowseSevenZip_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = LocalizationService.Current.GetString("Settings.ArchSelect7z"),
            Filter = LocalizationService.Current.GetString("Settings.ArchFilter7z"),
            FileName = _vm.SevenZipPath
        };
        if (dlg.ShowDialog() == true)
            _vm.SevenZipPath = dlg.FileName;
    }

    /// <summary>
    /// Обработчик кнопки «Обзор» для WinRAR — открывает диалог выбора WinRAR.exe.
    /// Browse button handler for WinRAR — opens file dialog to select WinRAR.exe.
    /// </summary>
    private void BrowseWinRar_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = LocalizationService.Current.GetString("Settings.ArchSelectRar"),
            Filter = LocalizationService.Current.GetString("Settings.ArchFilterRar"),
            FileName = _vm.WinRarPath
        };
        if (dlg.ShowDialog() == true)
            _vm.WinRarPath = dlg.FileName;
    }

    /// <summary>
    /// Обработчик PreviewKeyDown на окне: перехват клавиш в режиме захвата.
    /// PreviewKeyDown handler: intercepts keys during capture mode.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_vm.HotkeysVM.IsCapturing)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (_vm.HotkeysVM.HandleKeyDown(key, Keyboard.Modifiers))
            {
                HotkeyStatusText.Text = "";
                e.Handled = true;
                return;
            }
        }
        base.OnPreviewKeyDown(e);
    }
}
