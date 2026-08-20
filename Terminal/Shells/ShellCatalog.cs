using System.Diagnostics;
using Microsoft.Win32;
using CoderCommander.Services;

namespace CoderCommander.Terminal.Shells;

/// <summary>
/// Discovers every shell this machine can actually run: cmd.exe, Windows PowerShell 5.1, pwsh
/// (PowerShell 7+), Git Bash, and one entry per installed WSL distribution. Memoized for the
/// process lifetime (shells don't get installed/uninstalled mid-session) and cheap enough to
/// pre-warm at startup so the shell-picker dialog never blocks on it.
/// <para>
/// Every built-in shell resolves through an absolute, known install location - never through
/// <c>%PATH%</c> - so a poisoned or user-writable PATH entry can't substitute a different binary
/// for "PowerShell". <see cref="PathResolver"/> (PATH-based lookup) is reserved for the
/// user-configured custom-shell escape hatch, where the user is explicitly choosing what to run.
/// </para>
/// </summary>
internal static class ShellCatalog
{
    private static IReadOnlyList<ShellDescriptor>? _cached;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<IReadOnlyList<ShellDescriptor>> DiscoverAsync(CancellationToken ct = default)
    {
        if (_cached != null)
            return _cached;

        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached != null)
                return _cached;

            var result = new List<ShellDescriptor>();

            AddIfFound(result, FindCmd());
            AddIfFound(result, FindWindowsPowerShell());
            AddIfFound(result, FindPwsh());
            AddIfFound(result, FindGitBash());

            try
            {
                result.AddRange(await FindWslDistrosAsync(ct).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                LogService.Warning($"ShellCatalog: WSL discovery failed: {ex.Message}");
            }

            _cached = result;
            LogService.Info($"ShellCatalog: discovered {result.Count} shell(s): {string.Join(", ", result.Select(s => s.Id))}");
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Test/diagnostic hook - forces the next <see cref="DiscoverAsync"/> to re-scan.</summary>
    internal static void ResetCacheForTests() => _cached = null;

    private static void AddIfFound(List<ShellDescriptor> list, ShellDescriptor? descriptor)
    {
        if (descriptor != null)
            list.Add(descriptor);
    }

    private static ShellDescriptor? FindCmd()
    {
        var comspec = Environment.GetEnvironmentVariable("ComSpec");
        var path = !string.IsNullOrEmpty(comspec) && File.Exists(comspec)
            ? comspec
            : Path.Combine(Environment.SystemDirectory, "cmd.exe");

        if (!File.Exists(path))
            return null;

        // No arguments - with a real pseudo console, cmd.exe is interactive by default. The old
        // pipe-based terminal passed "/K" to force that; with ConPTY it would be redundant at
        // best.
        return new ShellDescriptor(ShellIds.Cmd, "Terminal.Shell.Cmd", null, path, Array.Empty<string>(), ShellFamily.Cmd);
    }

    private static ShellDescriptor? FindWindowsPowerShell()
    {
        var path = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(path)
            ? new ShellDescriptor(ShellIds.WindowsPowerShell, "Terminal.Shell.WindowsPowerShell", null, path, Array.Empty<string>(), ShellFamily.WindowsPowerShell)
            : null;
    }

    private static ShellDescriptor? FindPwsh()
    {
        var candidates = new List<string>();
        CollectPwshCandidates(Environment.GetEnvironmentVariable("ProgramFiles"), candidates);
        CollectPwshCandidates(Environment.GetEnvironmentVariable("ProgramFiles(x86)"), candidates);

        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(localAppData))
        {
            var exe = Path.Combine(localAppData, "Microsoft", "WindowsApps", "pwsh.exe");
            if (File.Exists(exe))
                candidates.Add(exe);
        }

        if (candidates.Count == 0)
            return null;

        // Prefer the highest version directory ("PowerShell\7", "PowerShell\7-preview", ...).
        var best = candidates.OrderByDescending(ParseLeadingVersionNumber).First();
        return new ShellDescriptor(ShellIds.PowerShellCore, "Terminal.Shell.PowerShellCore", null, best, Array.Empty<string>(), ShellFamily.PowerShellCore);
    }

    private static void CollectPwshCandidates(string? programFilesRoot, List<string> candidates)
    {
        if (string.IsNullOrEmpty(programFilesRoot))
            return;

        var psRoot = Path.Combine(programFilesRoot, "PowerShell");
        if (!Directory.Exists(psRoot))
            return;

        foreach (var dir in Directory.EnumerateDirectories(psRoot))
        {
            var exe = Path.Combine(dir, "pwsh.exe");
            if (File.Exists(exe))
                candidates.Add(exe);
        }
    }

    private static int ParseLeadingVersionNumber(string pwshExePath)
    {
        var dirName = Path.GetFileName(Path.GetDirectoryName(pwshExePath) ?? "");
        var digits = new string(dirName.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : 0;
    }

    private static ShellDescriptor? FindGitBash()
    {
        var installPath = ReadGitInstallPathFromRegistry();
        string? bashPath = null;

        if (!string.IsNullOrEmpty(installPath))
        {
            var candidate = Path.Combine(installPath, "bin", "bash.exe");
            if (File.Exists(candidate))
                bashPath = candidate;
        }

        if (bashPath == null)
        {
            var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
            if (!string.IsNullOrEmpty(programFiles))
            {
                var candidate = Path.Combine(programFiles, "Git", "bin", "bash.exe");
                if (File.Exists(candidate))
                    bashPath = candidate;
            }
        }

        if (bashPath == null)
            return null;

        return new ShellDescriptor(ShellIds.GitBash, "Terminal.Shell.GitBash", null, bashPath, ["--login", "-i"], ShellFamily.Bash);
    }

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

    private static async Task<IReadOnlyList<ShellDescriptor>> FindWslDistrosAsync(CancellationToken ct)
    {
        var wslExe = Path.Combine(Environment.SystemDirectory, "wsl.exe");
        if (!File.Exists(wslExe))
            return Array.Empty<ShellDescriptor>();

        byte[] rawOutput;
        try
        {
            rawOutput = await RunCaptureStdoutAsync(wslExe, "--list --quiet", TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Older Windows 10 builds don't support --quiet; retry without it. WslListParser
            // handles both quiet and non-quiet output (including "(Default)" suffix).
            try
            {
                rawOutput = await RunCaptureStdoutAsync(wslExe, "--list", TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            }
            catch (Exception ex2)
            {
                LogService.Warning($"ShellCatalog: \"wsl.exe --list\" failed: {ex.Message}; fallback also failed: {ex2.Message}");
                return Array.Empty<ShellDescriptor>();
            }
        }

        var distros = WslListParser.Parse(rawOutput);
        var result = new List<ShellDescriptor>(distros.Count);
        foreach (var distro in distros)
        {
            result.Add(new ShellDescriptor(
                ShellIds.WslPrefix + distro, "Terminal.Shell.Wsl", distro, wslExe, ["-d", distro], ShellFamily.Wsl));
        }
        return result;
    }

    /// <summary>Runs a process and captures its raw stdout bytes (not text - the caller decides
    /// how to decode, since <c>wsl.exe</c>'s UTF-16LE output would otherwise get silently
    /// mis-decoded by whatever text encoding .NET's Process defaults to for redirected output).</summary>
    private static async Task<byte[]> RunCaptureStdoutAsync(string executable, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var ms = new MemoryStream();
        var copyTask = process.StandardOutput.BaseStream.CopyToAsync(ms, ct);
        var drainStderrTask = process.StandardError.BaseStream.CopyToAsync(Stream.Null, ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            await copyTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            await drainStderrTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* best effort */ }

            // copyTask/drainStderrTask were started against the outer ct, not timeoutCts - a
            // *timeout* (CancelAfter) only unblocks the WaitAsync wrappers above, it doesn't
            // actually cancel either task. Left running after this method returns and the `using`
            // above disposes ms, a late write from copyTask throws ObjectDisposedException as an
            // unobserved task exception. Killing the process just above makes both pipes reach EOF
            // shortly after, so give them a short grace period to actually finish first.
            try
            {
                using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await Task.WhenAll(copyTask, drainStderrTask).WaitAsync(grace.Token).ConfigureAwait(false);
            }
            catch { /* best effort - if they're still stuck, there's nothing more to do here */ }

            throw;
        }

        return ms.ToArray();
    }
}
