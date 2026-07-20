using System.Windows.Controls;
using System.Windows.Input;
using CoderCommander.ViewModels;

namespace CoderCommander.Views;

/// <summary>
/// Панель управления Git: просмотр изменений, лог, переключение веток, коммиты, diff.
/// Git management panel: view changes, log, branch switching, commits, diff.
/// </summary>
public partial class GitPanel : UserControl
{
    /// <summary>
    /// Конструктор, инициализирующий XAML-компоненты.
    /// Constructor that initializes the XAML components.
    /// </summary>
    public GitPanel() => InitializeComponent();

    /// <summary>
    /// Обработчик смены выбранной ветки в ComboBox. Выполняет checkout ветки,
    /// игнорируя событие во время перезагрузки данных или если ветка не изменилась.
    /// Handles branch selection change in ComboBox. Performs checkout,
    /// ignoring events during data reload or if the branch hasn't changed.
    /// </summary>
    /// <param name="sender">Источник события (ComboBox). / Event source (ComboBox).</param>
    /// <param name="e">Данные события выбора. / Selection changed event data.</param>
    private void Branch_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is string branch && DataContext is GitViewModel vm)
        {
            //Игнорируем смену ветки в время перезагрузки данных или если ветка не изменилась
            if (vm.IsReloading || vm.Branch == branch) return;
            _ = vm.CheckoutAsync(branch);
        }
    }

    /// <summary>
    /// Обработчик двойного клика по изменённому файлу — открывает diff.
    /// Handles double-click on a changed file — opens the diff view.
    /// </summary>
    /// <param name="sender">Источник события. / Event source.</param>
    /// <param name="e">Данные события мыши. / Mouse event data.</param>
    private void File_Dbl(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is GitViewModel vm) _ = vm.ShowDiffAsync();
    }

    /// <summary>
    /// Обработчик нажатия клавиш в поле сообщения коммита.
    /// Ctrl+Enter запускает коммит.
    /// Handles key presses in the commit message field.
    /// Ctrl+Enter triggers the commit.
    /// </summary>
    /// <param name="sender">Источник события. / Event source.</param>
    /// <param name="e">Данные события клавиатуры. / Keyboard event data.</param>
    private void Commit_KD(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && DataContext is GitViewModel vm)
        {
            e.Handled = true;
            _ = vm.CommitAsync();
        }
    }
}
