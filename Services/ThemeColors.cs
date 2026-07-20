using System.Windows.Media;

namespace CoderCommander.Services;

/// <summary>
/// Singleton-кисти для ControlTemplate (GradientStop.Color).
/// Singleton brushes for ControlTemplate (GradientStop.Color).
///
/// ПРИНЦИП / PRINCIPLE:
/// Кисти создаются в КОДЕ (SolidColorBrush, НЕ frozen) и используются через x:Static в XAML.
/// Метод Update() меняет ТОЛЬКО .Color — объект кисти остаётся тем же → UCE-хэндл валиден.
///
/// Brushes are created in CODE (SolidColorBrush, NOT frozen) and used via x:Static in XAML.
/// Update() changes ONLY .Color — the brush object stays the same → UCE handle remains valid.
/// </summary>
public static class ThemeColors
{
    public static SolidColorBrush AccentBrush { get; } = new(Color.FromRgb(0x00, 0x78, 0xD4));
    public static SolidColorBrush AccentSecondaryBrush { get; } = new(Color.FromRgb(0x4E, 0xC9, 0xB0));
    public static SolidColorBrush BorderBrush { get; } = new(Color.FromRgb(0x2B, 0x2B, 0x2B));
    public static SolidColorBrush SurfaceHoverBrush { get; } = new(Color.FromRgb(0x2D, 0x2D, 0x2D));
    public static SolidColorBrush FgLightBrush { get; } = new(Color.FromRgb(0xD4, 0xD4, 0xD4));
    public static SolidColorBrush FgDimBrush { get; } = new(Color.FromRgb(0x9D, 0xA0, 0xA6));
    public static SolidColorBrush FgMutedBrush { get; } = new(Color.FromRgb(0x87, 0x87, 0x87));
    public static SolidColorBrush AccentDimBrush { get; } = new(Color.FromArgb(0x33, 0x00, 0x78, 0xD4));
    public static SolidColorBrush BgHeaderBrush { get; } = new(Color.FromRgb(0x25, 0x25, 0x26));
    public static SolidColorBrush SurfaceBrush { get; } = new(Color.FromRgb(0x25, 0x25, 0x26));
    public static SolidColorBrush SelectionBrush { get; } = new(Color.FromArgb(0x33, 0x78, 0xD4, 0x55));
    public static SolidColorBrush SelectionActiveBrush { get; } = new(Color.FromArgb(0x55, 0x78, 0xD4, 0x88));

    public static void Update(ThemeMode theme)
    {
        if (theme == ThemeMode.Light)
        {
            AccentBrush.Color = Color.FromRgb(0x00, 0x66, 0xBF);
            AccentSecondaryBrush.Color = Color.FromRgb(0x26, 0x7F, 0x99);
            BorderBrush.Color = Color.FromRgb(0xD4, 0xD4, 0xD4);
            SurfaceHoverBrush.Color = Color.FromRgb(0xE8, 0xE8, 0xE8);
            FgLightBrush.Color = Color.FromRgb(0x1F, 0x1F, 0x1F);
            FgDimBrush.Color = Color.FromRgb(0x5A, 0x5A, 0x5A);
            FgMutedBrush.Color = Color.FromRgb(0x6B, 0x72, 0x80);
            AccentDimBrush.Color = Color.FromArgb(0x22, 0x00, 0x66, 0xBF);
            BgHeaderBrush.Color = Color.FromRgb(0xF3, 0xF3, 0xF3);
            SurfaceBrush.Color = Color.FromRgb(0xF3, 0xF3, 0xF3);
            SelectionBrush.Color = Color.FromArgb(0x33, 0x66, 0xBF, 0x44);
            SelectionActiveBrush.Color = Color.FromArgb(0x55, 0x66, 0xBF, 0x77);
        }
        else
        {
            AccentBrush.Color = Color.FromRgb(0x00, 0x78, 0xD4);
            AccentSecondaryBrush.Color = Color.FromRgb(0x4E, 0xC9, 0xB0);
            BorderBrush.Color = Color.FromRgb(0x2B, 0x2B, 0x2B);
            SurfaceHoverBrush.Color = Color.FromRgb(0x2D, 0x2D, 0x2D);
            FgLightBrush.Color = Color.FromRgb(0xD4, 0xD4, 0xD4);
            FgDimBrush.Color = Color.FromRgb(0x9D, 0xA0, 0xA6);
            FgMutedBrush.Color = Color.FromRgb(0x87, 0x87, 0x87);
            AccentDimBrush.Color = Color.FromArgb(0x33, 0x00, 0x78, 0xD4);
            BgHeaderBrush.Color = Color.FromRgb(0x25, 0x25, 0x26);
            SurfaceBrush.Color = Color.FromRgb(0x25, 0x25, 0x26);
            SelectionBrush.Color = Color.FromArgb(0x33, 0x78, 0xD4, 0x55);
            SelectionActiveBrush.Color = Color.FromArgb(0x55, 0x78, 0xD4, 0x88);
        }
    }
}
