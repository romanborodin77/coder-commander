using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CoderCommander.Services;

namespace CoderCommander.Views;

/// <summary>
/// Универсальный стильзованный диалог, заменяющий стандартный MessageBox.
/// Поддерживает 4 типа иконок (Info/Warning/Error/Question) и 4 комбинации кнопок.
/// Universal styled dialog replacing the standard MessageBox.
/// Supports 4 icon types (Info/Warning/Error/Question) and 4 button combinations.
/// </summary>
public partial class StyledMessageBoxWindow : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;

    public StyledMessageBoxWindow() => InitializeComponent();

    /// <summary>
    /// Показывает стильзованный диалог (статический метод, аналог MessageBox.Show).
    /// Shows a styled dialog (static method, analogous to MessageBox.Show).
    /// </summary>
    /// <param name="message">Текст сообщения / Message text.</param>
    /// <param name="caption">Заголовок окна / Window caption.</param>
    /// <param name="buttons">Комбинация кнопок / Button combination.</param>
    /// <param name="image">Тип иконки / Icon type.</param>
    /// <returns>Результат нажатой кнопки / Result of the pressed button.</returns>
    public static MessageBoxResult Show(string message, string caption = "",
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        var owner = Application.Current.Windows.Count > 0
            ? Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow
            : null;

        var win = new StyledMessageBoxWindow();
        win.TitleText.Text = string.IsNullOrEmpty(caption) ? "Coder Commander" : caption;
        win.MessageText.Text = message;
        win.Title = string.IsNullOrEmpty(caption) ? "Coder Commander" : caption;
        win.SetupButtons(buttons);
        win.SetupIcon(image);
        win.Owner = owner;
        win.ShowDialog();
        return win._result;
    }

    /// <summary>
    /// Настраивает кнопки в соответствии с типом диалога.
    /// Sets up buttons according to the dialog type.
    /// </summary>
    private void SetupButtons(MessageBoxButton buttons)
    {
        switch (buttons)
        {
            case MessageBoxButton.OK:
                AddButton(LocalizationService.Current.GetString("MsgBox.OK"), MessageBoxResult.OK, isDefault: true);
                break;
            case MessageBoxButton.OKCancel:
                AddButton(LocalizationService.Current.GetString("MsgBox.OK"), MessageBoxResult.OK, isDefault: true);
                AddButton(LocalizationService.Current.GetString("MsgBox.Cancel"), MessageBoxResult.Cancel);
                break;
            case MessageBoxButton.YesNo:
                AddButton(LocalizationService.Current.GetString("MsgBox.Yes"), MessageBoxResult.Yes, isDefault: true);
                AddButton(LocalizationService.Current.GetString("MsgBox.No"), MessageBoxResult.No);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton(LocalizationService.Current.GetString("MsgBox.Yes"), MessageBoxResult.Yes, isDefault: true);
                AddButton(LocalizationService.Current.GetString("MsgBox.No"), MessageBoxResult.No);
                AddButton(LocalizationService.Current.GetString("MsgBox.Cancel"), MessageBoxResult.Cancel);
                break;
        }
    }

    /// <summary>
    /// Добавляет кнопку с указанным текстом и результатом.
    /// Adds a button with the specified text and result.
    /// </summary>
    private void AddButton(string text, MessageBoxResult result, bool isDefault = false)
    {
        var btn = new System.Windows.Controls.Button
        {
            Content = text,
            MinWidth = 88,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = isDefault,
            IsCancel = result == MessageBoxResult.Cancel,
        };

        if (result == MessageBoxResult.Cancel || result == MessageBoxResult.No)
        {
            btn.SetResourceReference(FrameworkElement.StyleProperty, "DefaultButtonStyle");
        }
        else
        {
            btn.SetResourceReference(FrameworkElement.StyleProperty, "AccentButtonStyle");
        }

        btn.Click += (_, _) => { _result = result; Close(); };
        ButtonPanel.Children.Add(btn);
    }

    /// <summary>
    /// Настраивает иконку диалога (Info/Warning/Error/Question).
    /// Sets up the dialog icon (Info/Warning/Error/Question).
    /// </summary>
    private void SetupIcon(MessageBoxImage image)
    {
        switch (image)
        {
            case MessageBoxImage.Information:
                IconText.Text = "\uE946";
                IconText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "AccentBrush");
                IconBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "AccentDimBrush");
                break;
            case MessageBoxImage.Warning:
                IconText.Text = "\uE7BA";
                IconText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "WarnBrush");
                IconBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "WarnBrush");
                IconBorder.Background = OpacityBrush(0.12);
                break;
            case MessageBoxImage.Error:
                IconText.Text = "\uEA39";
                IconText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ErrBrush");
                IconBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "ErrBrush");
                IconBorder.Background = OpacityBrush(0.12);
                break;
            case MessageBoxImage.Question:
                IconText.Text = "\uE897";
                IconText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "AccentBrush");
                IconBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "AccentDimBrush");
                break;
            default:
                IconBorder.Visibility = Visibility.Collapsed;
                break;
        }
    }

    /// <summary>
    /// Создаёт полупрозрачную кисть заданного цвета (для фона иконок).
    /// Creates a semi-transparent brush of the given color (for icon backgrounds).
    /// </summary>
    private static SolidColorBrush OpacityBrush(double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)(opacity * 255), 228, 166, 21));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Перетаскивание окна за titlebar. / Drag the window by the titlebar.
    /// </summary>
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    /// <summary>
    /// Закрыть окно через X (результат — None). / Close via X button (result = None).
    /// </summary>
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.None;
        Close();
    }
}
