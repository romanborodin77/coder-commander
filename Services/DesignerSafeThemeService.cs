using System;

namespace CoderCommander.Services;

/// <summary>
/// Provides a designer-safe access to the active theme palette. When running inside a
/// visual designer this returns a built-in default <see cref="ThemePalette"/> instance
/// instead of touching ThemeService.Current which may trigger settings I/O.
/// </summary>
internal static class DesignerSafeThemeService
{
    private static ThemePalette? s_default;

    public static ThemePalette Current => DesignTime.IsActive ? (s_default ??= new ThemePalette()) : ThemeService.Current;
}
