namespace CoderCommander.Operations;

/// <summary>
/// Политика разрешения конфликта имён при копировании/переносе (ph0.3, exp.yml).
/// Conflict-resolution policy for copy/move when the destination exists (ph0.3).
/// Отличается от устаревшего <c>Services.OverwritePolicy</c> из CopyService тем, что
/// добавляет семантические правила (OverwriteOlder/OverwriteSmaller) и AutoRename.
/// Distinct from the legacy Services.OverwritePolicy in CopyService: adds semantic
/// rules (OverwriteOlder/OverwriteSmaller) and AutoRename.
/// </summary>
public enum OverwritePolicy
{
    /// <summary>Пропустить существующий файл. / Skip the existing destination.</summary>
    Skip,

    /// <summary>Всегда перезаписывать. / Always overwrite.</summary>
    Overwrite,

    /// <summary>Перезаписать, только если источник новее. / Overwrite only if the source is newer.</summary>
    OverwriteOlder,

    /// <summary>Перезаписать, только если источник меньше размером. / Overwrite only if the source is smaller.</summary>
    OverwriteSmaller,

    /// <summary>Автоматически переименовать новый файл. / Auto-rename the incoming file.</summary>
    AutoRename,

    /// <summary>Спросить пользователя при конфликте. / Ask the user on conflict.</summary>
    Ask
}
