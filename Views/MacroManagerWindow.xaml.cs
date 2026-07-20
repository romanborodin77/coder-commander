using System.Windows;
using System.Windows.Input;
using CoderCommander.Services;
using CoderCommander.ViewModels;

namespace CoderCommander.Views;

/// <summary>
/// Окно управления макросами: создание, редактирование шагов, выполнение.
/// Macro manager window: create, edit steps, execute.
/// </summary>
public partial class MacroManagerWindow : Window
{
    private readonly MacroManagerViewModel _vm;

    /// <summary>
    /// Конструктор окна макросов. / Macro window constructor.
    /// </summary>
    public MacroManagerWindow()
    {
        InitializeComponent();
        _vm = new MacroManagerViewModel();
        DataContext = _vm;
    }

    /// <summary>
    /// Перетаскивание окна за заголовок. / Drag window by title bar.
    /// </summary>
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Button) return;
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    /// <summary>
    /// Закрыть без сохранения. / Close without saving.
    /// </summary>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Отменить и закрыть. / Cancel and close.
    /// </summary>
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Сохранить и закрыть. / Save and close.
    /// </summary>
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _vm.SaveChangesCommand.Execute(null);
        Close();
    }
}
