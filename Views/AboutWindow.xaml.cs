using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CoderCommander.Views;

/// <summary>
/// Окно «О программе» с анимированным SVG-логотипом { } и интерактивными tech badges.
/// "About" window with animated SVG { } logo and interactive tech badges.
/// </summary>
public partial class AboutWindow : Window
{
    /// <summary>
    /// Создаёт окно «О программе» и запускает анимацию появления при загрузке.
    /// Creates the About window and plays the entry animation on load.
    /// </summary>
    public AboutWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => PlayEntryAnimation();
    }

    /// <summary>
    /// Проигрывает анимацию появления: fade-in + масштабирование логотипа.
    /// Plays the entry animation: fade-in + logo scale-up.
    /// </summary>
    private void PlayEntryAnimation()
    {
        var sb = new Storyboard();
        var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(400)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var scale = new DoubleAnimation(0.85, 1.0, new Duration(TimeSpan.FromMilliseconds(500)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

        Storyboard.SetTarget(fade, this);
        Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
        Storyboard.SetTarget(scale, LogoScale);
        Storyboard.SetTargetProperty(scale, new PropertyPath(ScaleTransform.ScaleXProperty));

        sb.Children.Add(fade);
        sb.Children.Add(scale);
        sb.Begin();
        Opacity = 0;
    }

    /// <summary>
    /// Перетаскивание окна за titlebar. / Drag the window by the titlebar.
    /// </summary>
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    /// <summary>Закрыть окно. / Close the window.</summary>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Hover на логотипе — увеличение масштаба до 1.08.
    /// Logo hover — scale up to 1.08.
    /// </summary>
    private void Logo_MouseEnter(object sender, MouseEventArgs e)
    {
        var anim = new DoubleAnimation(1.08, new Duration(TimeSpan.FromMilliseconds(200)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    /// <summary>
    /// Hover покинул логотип — возврат масштаба к 1.0.
    /// Logo hover exit — scale back to 1.0.
    /// </summary>
    private void Logo_MouseLeave(object sender, MouseEventArgs e)
    {
        var anim = new DoubleAnimation(1.0, new Duration(TimeSpan.FromMilliseconds(300)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }
}
