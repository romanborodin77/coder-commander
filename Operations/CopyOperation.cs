using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.FileSystem;

namespace CoderCommander.Operations;

/// <summary>
/// Операция копирования файлов/каталогов (потоковое копирование с буферизацией ~1 МБ).
/// File/directory copy operation (streaming copy with ~1 MB buffering, ph0.2).
/// </summary>
public sealed class CopyOperation : TransferOperation
{
    /// <summary>
    /// Создаёт операцию копирования.
    /// Creates a copy operation.
    /// </summary>
    /// <param name="fs">Файловая система-источник/назначение. / Source/destination file system.</param>
    /// <param name="sources">Исходные пути (файлы или каталоги). / Source paths (files or directories).</param>
    /// <param name="destDir">Целевой каталог. / Destination directory.</param>
    /// <param name="policy">Политика перезаписи (по умолчанию Overwrite). / Overwrite policy (default Overwrite).</param>
    /// <param name="onConflict">Колбэк разрешения конфликта (необязательный). / Conflict callback (optional).</param>
    /// <param name="progress">Приёмник прогресса. / Progress sink.</param>
    /// <param name="options">Настройки операции. / Operation options.</param>
    public CopyOperation(
        IFileSystem fs, IEnumerable<string> sources, string destDir,
        OverwritePolicy policy = OverwritePolicy.Overwrite,
        Func<string, string, OverwritePolicy>? onConflict = null,
        IProgress<OperationProgress>? progress = null,
        TransferOptions? options = null)
        : base(fs, sources, destDir, policy, onConflict, progress, options) { }

    /// <inheritdoc/>
    protected override bool IsMove => false;
}
