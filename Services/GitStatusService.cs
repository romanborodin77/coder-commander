using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

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
    // -1/0/1 (unknown/false/true) rather than bool?, mutated via Interlocked - both panels can
    // navigate into a git repo concurrently on their own background threads, and this was
    // previously a plain unlocked bool? field: one thread's non-atomic read of a partially-written
    // value (or a torn read racing ResolveGitExecutable's own two-field write below) could see
    // "resolved" with a null executable, throw, and permanently disable git status for the whole
    // session on a machine where git is actually installed.
    private static int _gitAvailable = -1;

    /// <summary>Absolute path to git.exe, resolved once per process. <see cref="Lazy{T}"/>'s
    /// default thread-safety mode (ExecutionAndPublication) makes this correctly single-flight on
    /// its own - concurrent callers block on the same in-progress resolution rather than each
    /// racing to write <c>_gitExecutable</c>/<c>_gitExecutableResolved</c> as two separate,
    /// unsynchronized fields the way this used to.</summary>
    private static readonly Lazy<string?> GitExecutableLazy = new(FindGitExecutable, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Returns a status snapshot for the repository containing <paramref name="directory"/>, or
    /// null when the directory isn't inside a git working tree, git isn't installed, or the
    /// command otherwise failed. Runs the actual process synchronously - callers are expected to
    /// invoke this from a background thread (e.g. via <see cref="Task.Run{TResult}(Func{TResult})"/>).
    /// </summary>
    public static GitStatusSnapshot? GetStatus(string directory)
    {
        if (Volatile.Read(ref _gitAvailable) == 0)
            return null;

        var repoRoot = FindRepoRoot(directory);
        if (repoRoot == null)
            return null;

        string output;
        try
        {
            output = RunGit(repoRoot, "status --porcelain=v1 --untracked-files=all");
            Interlocked.Exchange(ref _gitAvailable, 1);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // git isn't installed / isn't on PATH - stop trying for the rest of this session.
            Interlocked.Exchange(ref _gitAvailable, 0);
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
            pathPart = UnescapeGitPath(pathPart);

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

    /// <summary>Unescapes a git porcelain v1 path. Git C-quotes paths containing special
    /// characters (spaces, unicode, etc.) wrapping them in double quotes and using backslash
    /// escapes (e.g. <c>\"</c> for a literal quote, <c>\\</c> for a backslash, <c>\NNN</c> for
    /// octal byte values). Unquoted paths are returned as-is.</summary>
    private static string UnescapeGitPath(string pathPart)
    {
        if (pathPart.Length < 2 || pathPart[0] != '"')
            return pathPart;

        // Strip surrounding quotes.
        var inner = pathPart.Substring(1, pathPart.Length - 2);
        var sb = new StringBuilder(inner.Length);
        for (int i = 0; i < inner.Length; i++)
        {
            if (inner[i] != '\\' || i + 1 >= inner.Length)
            {
                sb.Append(inner[i]);
                continue;
            }

            var next = inner[i + 1];
            switch (next)
            {
                case '"': sb.Append('"'); i++; break;
                case '\\': sb.Append('\\'); i++; break;
                case 't': sb.Append('\t'); i++; break;
                case 'b': sb.Append('\b'); i++; break;
                case 'n': sb.Append('\n'); i++; break;
                default: sb.Append('\\'); break;
            }
        }
        return sb.ToString();
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
        var gitExecutable = ResolveGitExecutable()
            ?? throw new InvalidOperationException("git.exe not found in any known install location");

        var psi = new ProcessStartInfo
        {
            FileName = gitExecutable,
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

    /// <summary>
    /// Absolute path to <c>git.exe</c>, resolved once per process and cached (git doesn't get
    /// installed/uninstalled mid-session) via <see cref="GitExecutableLazy"/>. Resolves through
    /// the registry key Git for Windows' installer writes and a couple of known Program Files
    /// locations - never through a bare "git" <see cref="ProcessStartInfo.FileName"/>, which
    /// Win32's <c>CreateProcess</c> would otherwise resolve by searching the launch directory,
    /// System32, Windows, then every <c>%PATH%</c> entry in order. That search order is exactly
    /// the risk <c>Terminal/Shells/ShellCatalog.cs</c> already documents and avoids for every
    /// built-in shell: a poisoned or user-writable PATH/launch-directory entry could substitute a
    /// different binary for "git" - and this runs on effectively every panel navigation into a
    /// git repository, not a one-off action.
    /// </summary>
    private static string? ResolveGitExecutable() => GitExecutableLazy.Value;

    private static string? FindGitExecutable()
    {
        var installPath = ReadGitInstallPathFromRegistry();
        if (!string.IsNullOrEmpty(installPath))
        {
            var candidate = Path.Combine(installPath, "cmd", "git.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        foreach (var programFilesVar in new[] { "ProgramFiles", "ProgramFiles(x86)" })
        {
            var programFiles = Environment.GetEnvironmentVariable(programFilesVar);
            if (string.IsNullOrEmpty(programFiles))
                continue;

            var candidate = Path.Combine(programFiles, "Git", "cmd", "git.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>Same registry key <c>Terminal/Shells/ShellCatalog.ReadGitInstallPathFromRegistry</c>
    /// reads for Git Bash discovery - kept as an independent copy rather than a shared call because
    /// <c>Services</c> must not depend on <c>Terminal.Shells</c> (wrong direction for this project's
    /// layering).</summary>
    private static string? ReadGitInstallPathFromRegistry()
    {
        string[] keyPaths = [@"SOFTWARE\GitForWindows", @"SOFTWARE\WOW6432Node\GitForWindows"];

        foreach (var keyPath in keyPaths)
        {
            var fromMachine = TryReadInstallPath(RegistryHive.LocalMachine, keyPath);
            if (fromMachine != null)
                return fromMachine;

            var fromUser = TryReadInstallPath(RegistryHive.CurrentUser, keyPath);
            if (fromUser != null)
                return fromUser;
        }

        return null;
    }

    private static string? TryReadInstallPath(RegistryHive hive, string keyPath)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(keyPath);
            return key?.GetValue("InstallPath") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Registry access can legitimately fail in locked-down/sandboxed environments -
            // treat as "not found", not a discovery failure.
            return null;
        }
    }
}
