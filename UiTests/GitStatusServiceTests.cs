using CoderCommander.Services;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the stdout/stderr deadlock pattern fixed in
/// <see cref="GitStatusService"/>'s private RunGit helper: stderr was redirected but never read,
/// so any git output large enough to fill its OS pipe buffer would hang stdout's ReadToEnd()
/// forever, before WaitForExit(10_000) ever got a chance to apply. Forcing git to overflow that
/// buffer isn't practical to do deterministically through the real git binary, so this instead
/// exercises the fixed code path end-to-end against this repository and asserts it completes
/// well within the timeout - guarding the async-read refactor against reintroducing a hang on
/// the ordinary path.
/// </summary>
public class GitStatusServiceTests
{
    [Test]
    public void GetStatus_OnThisRepository_CompletesWithoutHanging()
    {
        var repoDir = FindRepoRoot();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var snapshot = GitStatusService.GetStatus(repoDir);
        sw.Stop();

        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(9)),
            "GetStatus must not hang anywhere close to RunGit's 10s timeout on an ordinary repo");
        Assert.That(snapshot, Is.Not.Null, "This test runs inside a git working tree - GetStatus should find it");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate the repository root from the test output directory");
    }
}
