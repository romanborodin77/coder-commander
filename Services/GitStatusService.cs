using System.Diagnostics;
using System.Text;

namespace CoderCommander.Services;

/// <summary>Working-tree git status for a file or directory, as shown by <c>git status --porcelain</c>.</summary>
public enum GitFileStatus
{
    /// <summary>Not in a git repository, or unchanged relative to HEAD/index.</summary>
    None,
    /// <summary>Modified in the working tree or index.</summary>
    Modified,
    /// <summary>Newly added to the index (staged, not yet committed).</summary>
    Added,
    /// <summary>Deleted in the working tree or index.</summary>
    Deleted,
    /// <summary>Renamed relative to HEAD (staged).</summary>
    Renamed,
    /// <summary>Not tracked by git at all.</summary>
    Untracked,
    /// <summary>Merge conflict.</summary>
    Conflicted
}

/// <summary>
/// A single <c>git status</c> snapshot for one repository, resolved lazily against arbitrary
/// absolute paths under that repository's working tree.
/// </summary>
public sealed class GitStatusSnapshot
{
    private readonly string _repoRoot;
    private readonly Dictionary<string, GitFileStatus> _byRelativePath;

    internal GitStatusSnapshot(string repoRoot, Dictionary<string, GitFileStatus> byRelativePath)
    {
        _repoRoot = repoRoot;
        _byRelativePath = byRelativePath;
    }

    /// <summary>
    /// Resolves the status for <paramref name="fullPath"/>. For a directory, this is the "worst"
    /// status (conflicted takes priority) among everything git reports underneath it - the same
    /// way a modified/added file makes its containing folder show as changed in most editors.
    /// </summary>
    public GitFileStatus Resolve(string fullPath, bool isDirectory)
    {
        var relative = Path.GetRelativePath(_repoRoot, fullPath).Replace('\\', '/');

        if (!isDirectory)
            return _byRelativePath.TryGetValue(relative, out var status) ? status : GitFileStatus.None;

        var prefix = relative + "/";
        var best = GitFileStatus.None;
        foreach (var (path, status) in _byRelativePath)
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (status == GitFileStatus.Conflicted)
                return GitFileStatus.Conflicted;
            if (best == GitFileStatus.None)
                best = status;
        }
        return best;
    }
}

/// <summary>
/// Runs <c>git status --porcelain</c> for a directory's repository and parses the result. Never
/// throws for the common "not a git repo"/"git not installed" cases - both simply mean no status
/// to show, not an error worth surfacing to the user.
/// </summary>
public static class GitStatusService
{
    private static bool? _gitAvailable;

    /// <summary>
    /// Returns a status snapshot for the repository containing <paramref name="directory"/>, or
    /// null when the directory isn't inside a git working tree, git isn't installed, or the
    /// command otherwise failed. Runs the actual process synchronously - callers are expected to
    /// invoke this from a background thread (e.g. via <see cref="Task.Run{TResult}(Func{TResult})"/>).
    /// </summary>
    public static GitStatusSnapshot? GetStatus(string directory)
    {
        if (_gitAvailable == false)
            return null;

        var repoRoot = FindRepoRoot(directory);
        if (repoRoot == null)
            return null;

        string output;
        try
        {
            output = RunGit(repoRoot, "status --porcelain=v1 --untracked-files=all");
            _gitAvailable = true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // git isn't installed / isn't on PATH - stop trying for the rest of this session.
            _gitAvailable = false;
            return null;
        }
        catch (Exception ex)
        {
            LogService.Warning($"git status failed for {repoRoot}: {ex.Message}");
            return null;
        }

        var map = new Dictionary<string, GitFileStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length < 4) continue;

            var indexState = line[0];
            var workState = line[1];
            var pathPart = line[3..];

            // Renames report as "old -> new"; only the new path matters for display.
            var arrow = pathPart.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
                pathPart = pathPart[(arrow + 4)..];
            pathPart = pathPart.Trim('"');

            map[pathPart] = Classify(indexState, workState);
        }

        return new GitStatusSnapshot(repoRoot, map);
    }

    private static GitFileStatus Classify(char index, char work)
    {
        if (index == 'U' || work == 'U') return GitFileStatus.Conflicted;
        if (index == '?' && work == '?') return GitFileStatus.Untracked;
        if (index == 'A') return GitFileStatus.Added;
        if (index == 'D' || work == 'D') return GitFileStatus.Deleted;
        if (index == 'R') return GitFileStatus.Renamed;
        return GitFileStatus.Modified;
    }

    /// <summary>Walks upward from <paramref name="startDir"/> looking for a <c>.git</c> entry
    /// (directory for a normal repo, file for a worktree/submodule) - the nearest ancestor one
    /// found defines the repository root, matching git's own resolution.</summary>
    private static string? FindRepoRoot(string startDir)
    {
        try
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // inaccessible ancestor directory - treat as "not in a repo" rather than throwing
        }
        return null;
    }

    private static string RunGit(string workingDirectory, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git");

        // Read stdout and stderr concurrently. Blocking on ReadToEnd() for one stream while the
        // other pipe's OS buffer fills deadlocks forever: the child blocks writing to the full
        // pipe, waiting for a reader that never comes because we're still stuck reading the other
        // one - classic .NET Process I/O deadlock. Both were redirected but stderr was never read
        // at all, so any git warning (e.g. "detected dubious ownership") large enough to fill its
        // pipe buffer would hang here, and WaitForExit(10_000) would never even get a chance to run.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(10_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException($"git {arguments} timed out after 10s");
        }

        Task.WaitAll(stdoutTask, stderrTask);
        return stdoutTask.Result;
    }
}
