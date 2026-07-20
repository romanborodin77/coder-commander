using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.Models;

namespace CoderCommander.Services;

/// <summary>
/// Сервис для выполнения Git-операций через CLI.
/// Service for performing Git operations via CLI.
/// </summary>
public sealed class GitService
{
    private readonly IProcessService _proc;

    /// <summary>
    /// Создаёт экземпляр GitService.
    /// Creates an instance of GitService.
    /// </summary>
    /// <param name="proc">Сервис запуска процессов. / Process service.</param>
    public GitService(IProcessService proc) => _proc = proc;

    /// <summary>
    /// Проверяет, является ли указанный путь Git-репозиторием (находится внутри рабочего дерева).
    /// Checks whether the given path is inside a Git repository (inside a working tree).
    /// </summary>
    /// <param name="path">Путь для проверки. / Path to check.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>true, если путь — внутри Git-репозитория. / true if the path is inside a Git repository.</returns>
    public async Task<bool> IsRepositoryAsync(string path, CancellationToken ct = default)
    {
        var r = await _proc.RunAsync("git", new[] { "rev-parse", "--is-inside-work-tree" }, path, ct);
        return r.Success && r.StdOut.Trim() == "true";
    }

    /// <summary>
    /// Возвращает статус Git-репозитория: ветка, количество коммитов вперёд/назад и изменения в файлах.
    /// Returns the Git repository status: branch, ahead/behind counts, and file changes.
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Статус репозитория или null в случае ошибки. / Repository status or null on failure.</returns>
    public async Task<GitStatus?> GetStatusAsync(string path, CancellationToken ct = default)
    {
        var sb = await _proc.RunAsync("git", new[] { "status", "-sb", "--untracked-files=normal" }, path, ct);
        if (!sb.Success) return null;

        string branch = "HEAD";
        int ahead = 0, behind = 0;
        var files = new List<GitFileStatus>();
        foreach (var raw in sb.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.TrimStart();
            if (line.StartsWith("##"))
            {
                var header = line[2..].Trim();
                var sp = header.IndexOf(' ');
                branch = sp >= 0 ? header[..sp] : header;
                var aidx = header.IndexOf("ahead", StringComparison.OrdinalIgnoreCase);
                if (aidx >= 0) int.TryParse(new string(header[(aidx + 6)..].TakeWhile(char.IsDigit).ToArray()), out ahead);
                var bidx = header.IndexOf("behind", StringComparison.OrdinalIgnoreCase);
                if (bidx >= 0) int.TryParse(new string(header[(bidx + 7)..].TakeWhile(char.IsDigit).ToArray()), out behind);
                continue;
            }
            if (line.Length < 2) continue;
            files.Add(new GitFileStatus(line[2..].Trim(), line[0], line[1]));
        }
        return new GitStatus(branch, ahead, behind, files);
    }

    /// <summary>
    /// Возвращает diff неиндексированных изменений (git diff) для указанного файла.
    /// Returns the diff of unstaged changes (git diff) for the specified file.
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="file">Путь к файлу относительно корня репозитория. / File path relative to repo root.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Текст diff. / Diff text.</returns>
    public async Task<string> DiffAsync(string path, string file, CancellationToken ct = default) =>
        (await _proc.RunAsync("git", new[] { "diff", "--no-color", "--", file }, path, ct)).StdOut;

    /// <summary>
    /// Возвращает diff индексированных (staged) изменений (git diff --cached).
    /// Returns the diff of staged changes (git diff --cached).
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="file">Путь к файлу относительно корня репозитория. / File path relative to repo root.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Текст diff. / Diff text.</returns>
    public async Task<string> DiffCachedAsync(string path, string file, CancellationToken ct = default) =>
        (await _proc.RunAsync("git", new[] { "diff", "--no-color", "--cached", "--", file }, path, ct)).StdOut;

    /// <summary>
    /// Возвращает diff между HEAD и рабочим деревом (git diff HEAD).
    /// Returns the diff between HEAD and the working tree (git diff HEAD).
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="file">Путь к файлу относительно корня репозитория. / File path relative to repo root.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Текст diff. / Diff text.</returns>
    public async Task<string> DiffHeadAsync(string path, string file, CancellationToken ct = default) =>
        (await _proc.RunAsync("git", new[] { "diff", "--no-color", "HEAD", "--", file }, path, ct)).StdOut;

    /// <summary>
    /// Возвращает полную информацию о коммите (git show).
    /// Returns the full commit information (git show).
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="hash">Хеш коммита. / Commit hash.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Текст show-вывода. / Show output text.</returns>
    public async Task<string> ShowCommitAsync(string path, string hash, CancellationToken ct = default) =>
        (await _proc.RunAsync("git", new[] { "show", "--no-color", "--stat", "-p", hash }, path, ct)).StdOut;

    /// <summary>
    /// Индексирует указанные файлы (git add).
    /// Stages the specified files (git add).
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="files">Коллекция файлов для индексации. / Collection of files to stage.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>true, если операция успешна. / true if the operation succeeded.</returns>
    public async Task<bool> AddAsync(string path, IEnumerable<string> files, CancellationToken ct = default)
    {
        var args = new List<string> { "add", "--" }.Concat(files);
        return (await _proc.RunAsync("git", args, path, ct)).Success;
    }

    /// <summary>
    /// Снимает индексацию с указанных файлов (git reset HEAD).
    /// Unstages the specified files (git reset HEAD).
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="files">Коллекция файлов для снятия с индексации. / Collection of files to unstage.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>true, если операция успешна. / true if the operation succeeded.</returns>
    public async Task<bool> UnstageAsync(string path, IEnumerable<string> files, CancellationToken ct = default)
    {
        var args = new List<string> { "reset", "-q", "HEAD", "--" }.Concat(files);
        return (await _proc.RunAsync("git", args, path, ct)).Success;
    }

    /// <summary>
    /// Создаёт коммит с указанным сообщением (git commit -m).
    /// Creates a commit with the specified message (git commit -m).
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="message">Сообщение коммита. / Commit message.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения git commit. / Result of git commit.</returns>
    public async Task<ProcessResult> CommitAsync(string path, string message, CancellationToken ct = default) =>
        await _proc.RunAsync("git", new[] { "commit", "-m", message }, path, ct);

    /// <summary>
    /// Изменяет последний коммит (git commit --amend -m).
    /// Amends the last commit (git commit --amend -m).
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="message">Новое сообщение коммита. / New commit message.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения git commit --amend. / Result of git commit --amend.</returns>
    public async Task<ProcessResult> CommitAmendAsync(string path, string message, CancellationToken ct = default) =>
        await _proc.RunAsync("git", new[] { "commit", "--amend", "-m", message }, path, ct);

    /// <summary>
    /// Выполняет git push.
    /// Runs git push.
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения git push. / Result of git push.</returns>
    public async Task<ProcessResult> PushAsync(string path, CancellationToken ct = default) =>
        await _proc.RunAsync("git", new[] { "push" }, path, ct);

    /// <summary>
    /// Выполняет git pull.
    /// Runs git pull.
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения git pull. / Result of git pull.</returns>
    public async Task<ProcessResult> PullAsync(string path, CancellationToken ct = default) =>
        await _proc.RunAsync("git", new[] { "pull" }, path, ct);

    /// <summary>
    /// Выполняет git fetch --all --prune.
    /// Runs git fetch --all --prune.
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения git fetch. / Result of git fetch.</returns>
    public async Task<ProcessResult> FetchAsync(string path, CancellationToken ct = default) =>
        await _proc.RunAsync("git", new[] { "fetch", "--all", "--prune" }, path, ct);

    /// <summary>
    /// Сохраняет изменения в stash (git stash push -u -m).
    /// Saves changes to stash (git stash push -u -m).
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения git stash. / Result of git stash.</returns>
    public async Task<ProcessResult> StashAsync(string path, CancellationToken ct = default) =>
        await _proc.RunAsync("git", new[] { "stash", "push", "-u", "-m", "Coder Commander" }, path, ct);

    /// <summary>
    /// Извлекает последний stash (git stash pop).
    /// Pops the latest stash (git stash pop).
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения git stash pop. / Result of git stash pop.</returns>
    public async Task<ProcessResult> PopStashAsync(string path, CancellationToken ct = default) =>
        await _proc.RunAsync("git", new[] { "stash", "pop" }, path, ct);

    /// <summary>
    /// Создаёт новую ветку и переключается на неё (git checkout -b).
    /// Creates and switches to a new branch (git checkout -b).
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="name">Имя новой ветки. / New branch name.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения git checkout -b. / Result of git checkout -b.</returns>
    public async Task<ProcessResult> CreateBranchAsync(string path, string name, CancellationToken ct = default) =>
        await _proc.RunAsync("git", new[] { "checkout", "-b", name }, path, ct);

    /// <summary>
    /// Удаляет ветку (git branch -d).
    /// Deletes a branch (git branch -d).
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="name">Имя ветки для удаления. / Branch name to delete.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения git branch -d. / Result of git branch -d.</returns>
    public async Task<ProcessResult> DeleteBranchAsync(string path, string name, CancellationToken ct = default) =>
        await _proc.RunAsync("git", new[] { "branch", "-d", name }, path, ct);

    /// <summary>
    /// Переключается на указанную ветку или коммит (git checkout).
    /// Switches to the specified branch or commit (git checkout).
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="target">Цель переключения (ветка, коммит). / Switch target (branch, commit).</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Результат выполнения git checkout. / Result of git checkout.</returns>
    public async Task<ProcessResult> CheckoutAsync(string path, string target, CancellationToken ct = default) =>
        await _proc.RunAsync("git", new[] { "checkout", target }, path, ct);

    /// <summary>
    /// Возвращает список локальных веток (git branch --format).
    /// Returns a list of local branches (git branch --format).
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Список имён веток. / List of branch names.</returns>
    public async Task<List<string>> BranchesAsync(string path, CancellationToken ct = default)
    {
        var r = await _proc.RunAsync("git", new[] { "branch", "--format=%(refname:short)" }, path, ct);
        return r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(b => b.Trim()).ToList();
    }

    /// <summary>
    /// Возвращает историю коммитов (git log) с указанным количеством записей.
    /// Returns the commit history (git log) with the specified count of entries.
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="count">Количество коммитов. / Number of commits.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Список записей лога. / List of log entries.</returns>
    public async Task<List<GitLogEntry>> LogAsync(string path, int count = 30, CancellationToken ct = default)
    {
        var r = await _proc.RunAsync("git", new[] { "log", $"-{count}", "--pretty=format:%H%x1f%an%x1f%ar%x1f%s" }, path, ct);
        var list = new List<GitLogEntry>();
        foreach (var line in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split('\x1f');
            if (p.Length == 4) list.Add(new GitLogEntry(p[0], p[1], p[2], p[3]));
        }
        return list;
    }

    /// <summary>
    /// Отменяет изменения в указанном файле (git checkout -- file).
    /// Discards changes in the specified file (git checkout -- file).
    /// </summary>
    /// <param name="path">Путь к репозиторию. / Path to the repository.</param>
    /// <param name="file">Путь к файлу относительно корня репозитория. / File path relative to repo root.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>true, если операция успешна. / true if the operation succeeded.</returns>
    public async Task<bool> DiscardAsync(string path, string file, CancellationToken ct = default) =>
        (await _proc.RunAsync("git", new[] { "checkout", "--", file }, path, ct)).Success;
}

/// <summary>
/// Представляет статус одного файла в Git-репозитории (индекс и рабочее дерево).
/// Represents the status of a single file in a Git repository (index and working tree).
/// </summary>
/// <param name="Path">Относительный путь к файлу. / Relative file path.</param>
/// <param name="Index">Символ статуса в индексе (git status --short). / Index status character (git status --short).</param>
/// <param name="WorkTree">Символ статуса в рабочем дереве. / Working tree status character.</param>
public sealed record GitFileStatus(string Path, char Index, char WorkTree)
{
    /// <summary>
    /// Возвращает вычисленное состояние файла на основе символов статуса.
    /// Returns the computed file state based on status characters.
    /// </summary>
    public GitState State
    {
        get
        {
            if (Index == '?' && WorkTree == '?') return GitState.Untracked;
            if (Index == 'A') return GitState.Added;
            if (Index == 'M' || WorkTree == 'M') return GitState.Modified;
            if (Index == 'D' || WorkTree == 'D') return GitState.Deleted;
            if (Index == 'R') return GitState.Renamed;
            if (Index == 'C') return GitState.Copied;
            if (Index == 'U') return GitState.Conflicted;
            return GitState.Unchanged;
        }
    }

    /// <summary>
    /// Возвращает короткое строковое представление состояния (M, A, D, ?? и т.д.).
    /// Returns a short string representation of the state (M, A, D, ??, etc.).
    /// </summary>
    public string StateShort => State switch
    {
        GitState.Modified => "M",
        GitState.Added => "A",
        GitState.Deleted => "D",
        GitState.Renamed => "R",
        GitState.Copied => "C",
        GitState.Untracked => "??",
        GitState.Conflicted => "U",
        _ => ""
    };

    /// <summary>
    /// Возвращает true, если файл проиндексирован (staged).
    /// Returns true if the file is staged.
    /// </summary>
    public bool IsStaged => Index is not (' ' or '?');
    /// <summary>
    /// Возвращает true, если файл имеет неиндексированные изменения.
    /// Returns true if the file has unstaged changes.
    /// </summary>
    public bool IsUnstaged => WorkTree is not (' ' or '?');
}

/// <summary>
/// Представляет полный статус Git-репозитория (ветка, количество коммитов вперёд/назад, изменения).
/// Represents the full Git repository status (branch, ahead/behind counts, file changes).
/// </summary>
/// <param name="Branch">Текущая ветка. / Current branch.</param>
/// <param name="Ahead">Количество коммитов впереди удалённой ветки. / Commits ahead of the remote branch.</param>
/// <param name="Behind">Количество коммитов позади удалённой ветки. / Commits behind the remote branch.</param>
/// <param name="Files">Список изменённых файлов. / List of changed files.</param>
public sealed record GitStatus(string Branch, int Ahead, int Behind, IReadOnlyList<GitFileStatus> Files);
/// <summary>
/// Представляет одну запись в истории Git (лога коммитов).
/// Represents a single entry in Git history (commit log).
/// </summary>
/// <param name="Hash">Полный хеш коммита. / Full commit hash.</param>
/// <param name="Author">Имя автора. / Author name.</param>
/// <param name="RelativeDate">Относительная дата (например, "2 days ago"). / Relative date (e.g. "2 days ago").</param>
/// <param name="Subject">Тема (первая строка сообщения коммита). / Subject (first line of commit message).</param>
public sealed record GitLogEntry(string Hash, string Author, string RelativeDate, string Subject)
{
    /// <summary>
    /// Сокращённый хеш (первые 7 символов).
    /// Short hash (first 7 characters).
    /// </summary>
    public string ShortHash => Hash.Length >= 7 ? Hash[..7] : Hash;
}

/// <summary>
/// Состояние файла в Git-репозитории.
/// File state in a Git repository.
/// </summary>
public enum GitState
{
    /// <summary>Без изменений. / Unchanged.</summary>
    Unchanged,
    /// <summary>Изменён. / Modified.</summary>
    Modified,
    /// <summary>Добавлен (проиндексирован). / Added (staged).</summary>
    Added,
    /// <summary>Удалён. / Deleted.</summary>
    Deleted,
    /// <summary>Переименован. / Renamed.</summary>
    Renamed,
    /// <summary>Скопирован. / Copied.</summary>
    Copied,
    /// <summary>Не отслеживается. / Untracked.</summary>
    Untracked,
    /// <summary>Конфликт слияния. / Merge conflict.</summary>
    Conflicted
}
/// <summary>
/// Тип строки в diff-выводе.
/// Kind of a line in diff output.
/// </summary>
public enum DiffLineKind
{
    /// <summary>Контекст (неизменённая строка). / Context (unchanged line).</summary>
    Context,
    /// <summary>Добавленная строка (+). / Added line (+).</summary>
    Added,
    /// <summary>Удалённая строка (-). / Removed line (-).</summary>
    Removed,
    /// <summary>Заголовок фрагмента (@@). / Hunk header (@@).</summary>
    Hunk,
    /// <summary>Заголовок diff для файла (diff --git). / File diff header (diff --git).</summary>
    Header,
    /// <summary>Мета-информация (index). / Meta information (index).</summary>
    Meta,
    /// <summary>Имя файла (---/+++). / File name (---/+++).</summary>
    File
}
/// <summary>
/// Представляет одну строку в diff-выводе с указанием её типа.
/// Represents a single line in diff output with its type.
/// </summary>
/// <param name="Text">Текст строки. / Line text.</param>
/// <param name="Kind">Тип строки. / Line kind.</param>
public sealed record DiffLine(string Text, DiffLineKind Kind);

/// <summary>
/// Статический парсер для разбора Git-диффа в список DiffLine.
/// Static parser for parsing Git diff output into a list of DiffLine.
/// </summary>
public static class DiffParser
{
    /// <summary>
    /// Разбирает сырой вывод git diff в список типизированных строк.
    /// Parses raw git diff output into a list of typed lines.
    /// </summary>
    /// <param name="raw">Сырой текст diff. / Raw diff text.</param>
    /// <returns>Список распарсенных строк с типами. / List of parsed lines with kinds.</returns>
    public static List<DiffLine> Parse(string raw)
    {
        var list = new List<DiffLine>();
        if (string.IsNullOrEmpty(raw)) return list;
        foreach (var line in raw.Split('\n'))
        {
            var t = line.TrimEnd('\r');
            if (t.StartsWith("diff ", StringComparison.Ordinal))
                list.Add(new DiffLine(t, DiffLineKind.File));
            else if (t.StartsWith("index ", StringComparison.Ordinal))
                list.Add(new DiffLine(t, DiffLineKind.Meta));
            else if (t.StartsWith("---", StringComparison.Ordinal) || t.StartsWith("+++", StringComparison.Ordinal))
                list.Add(new DiffLine(t, DiffLineKind.File));
            else if (t.StartsWith("@@", StringComparison.Ordinal))
                list.Add(new DiffLine(t, DiffLineKind.Hunk));
            else if (t.Length > 0 && t[0] == '+')
                list.Add(new DiffLine(t, DiffLineKind.Added));
            else if (t.Length > 0 && t[0] == '-')
                list.Add(new DiffLine(t, DiffLineKind.Removed));
            else
                list.Add(new DiffLine(t, DiffLineKind.Context));
        }
        return list;
    }
}
