using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.FileSystem;

namespace CoderCommander.Operations;

/// <summary>
/// Операция переноса файлов/каталогов. Внутри тома — через Rename (атомарно и быстро),
/// между томами — потоковое копирование + удаление источника.
/// Move operation. Within a volume — via Rename (atomic, fast); across volumes — streaming
/// copy + source deletion (ph0.2).
/// </summary>
public sealed class MoveOperation : TransferOperation
{
    /// <summary>
    /// Создаёт операцию переноса.
    /// Creates a move operation.
    /// </summary>
    /// <param name="fs">Файловая система. / File system.</param>
    /// <param name="sources">Исходные пути. / Source paths.</param>
    /// <param name="destDir">Целевой каталог. / Destination directory.</param>
    /// <param name="policy">Политика перезаписи (по умолчанию Overwrite). / Overwrite policy.</param>
    /// <param name="onConflict">Колбэк разрешения конфликта. / Conflict callback.</param>
    /// <param name="progress">Приёмник прогресса. / Progress sink.</param>
    /// <param name="options">Настройки операции. / Operation options.</param>
    public MoveOperation(
        IFileSystem fs, IEnumerable<string> sources, string destDir,
        OverwritePolicy policy = OverwritePolicy.Overwrite,
        Func<string, string, OverwritePolicy>? onConflict = null,
        IProgress<OperationProgress>? progress = null,
        TransferOptions? options = null)
        : base(fs, sources, destDir, policy, onConflict, progress, options) { }

    /// <inheritdoc/>
    protected override bool IsMove => true;
}
