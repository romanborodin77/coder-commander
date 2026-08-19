namespace CoderCommander.Utils;

/// <summary>
/// Shared human-readable formatting helpers. Kept dependency-free (no WinForms/ViewModels
/// references) so Models, Operations, ViewModels and WinForms can all call it without inverting
/// the project's layering - previously each of those layers carried its own near-identical copy
/// of <see cref="FormatSize"/>.
/// </summary>
public static class FormatUtils
{
    /// <summary>Formats a byte count into a human-readable string (e.g. "1.5 MB").</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 0) return "—";
        if (bytes == 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
        double size = bytes;
        var i = 0;
        while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
        return $"{size:0.##} {units[i]}";
    }
}
