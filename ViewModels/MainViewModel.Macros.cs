using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Services;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Частичная ViewModel: команда «Менеджер макросов» (ph8.2).
/// Partial ViewModel: the "Macro Manager" command (ph8.2).
/// Открывает окно управления макросами.
/// Opens the macro manager window.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Открывает окно управления макросами.
    /// Opens the macro manager window.
    /// </summary>
    [RelayCommand]
    private void OpenMacroManager()
    {
        ActivePanel.SaveFocus();
        var win = new MacroManagerWindow { Owner = Application.Current.MainWindow };
        win.ShowDialog();
        ActivePanel.RestoreFocus();
    }
}
