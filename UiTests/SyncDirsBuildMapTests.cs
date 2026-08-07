using System.Diagnostics;
using System.Reflection;
using CoderCommander.WinForms;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the unbounded-traversal fix in <see cref="SyncDirsForm"/>'s directory
/// scanner: BuildMap pushed every enumerated subdirectory onto its traversal stack with no check
/// for FileAttributes.ReparsePoint, so a directory junction pointing back at an ancestor (a
/// pattern real tools produce - OneDrive, npm bin symlinks, WSL app-data junctions) was walked as
/// an ordinary folder forever, growing the result map and the stack without bound. Uses a real
/// directory junction (via mklink /J, which - unlike symlinks - needs no elevated privileges on
/// Windows) rather than mocking, since the bug is specifically about FileSystemInfo.Attributes
/// behavior that's not worth faking. BuildMap is private static - invoked via reflection.
/// </summary>
public class SyncDirsBuildMapTests
{
    private static readonly MethodInfo BuildMap = typeof(SyncDirsForm)
        .GetMethod("BuildMap", BindingFlags.NonPublic | BindingFlags.Static)!;

    private string _root = "";
    private string _junctionPath = "";

    [SetUp]
    public void CreateTempRoot()
    {
        _root = Directory.CreateTempSubdirectory("cc_syncdirs_junction_test_").FullName;
        _junctionPath = Path.Combine(_root, "Loop");
    }

    [TearDown]
    public void DeleteTempRoot()
    {
        // The junction must be unlinked (non-recursive delete) before removing _root - a
        // recursive delete starting from _root hit UnauthorizedAccessException trying to clean
        // up through the self-referencing link itself rather than just detaching it.
        if (Directory.Exists(_junctionPath)) Directory.Delete(_junctionPath, recursive: false);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void BuildMap_SelfReferencingJunction_DoesNotRecurseIntoIt()
    {
        var junctionPath = _junctionPath;
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junctionPath}\" \"{_root}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using (var proc = Process.Start(psi)!)
            proc.WaitForExit(5000);

        Assert.That(Directory.Exists(junctionPath), Is.True, "Test setup: junction creation must succeed (mklink /J)");

        // Bound the call itself so a regression fails the test instead of hanging the whole
        // suite - but on this OS/config, Windows' own MAX_PATH limit (~260 chars without long-path
        // support) already terminates the unbounded pre-fix traversal within well under a
        // second, so timing alone can't distinguish fixed from broken. What can: whether the
        // second level ("Loop\Loop") ever gets recorded at all - pre-fix, BuildMap pushes every
        // subdirectory unconditionally and does record it (verified: present before the fix,
        // absent after); post-fix, the reparse-point guard means it's never pushed for traversal
        // in the first place, so it must never appear no matter how many levels the OS would
        // otherwise tolerate.
        var task = Task.Run(() => (System.Collections.IDictionary)BuildMap.Invoke(null, new object?[] { _root, true })!);
        var completed = task.Wait(TimeSpan.FromSeconds(10));
        Assert.That(completed, Is.True, "BuildMap must terminate within a reasonable time");

        var map = task.Result;
        Assert.That(map.Contains("Loop"), Is.True, "The junction itself must still be listed as an entry");
        Assert.That(map.Contains(Path.Combine("Loop", "Loop")), Is.False,
            "Must never descend through the reparse point into a second level - that's the unbounded recursion this guards against");
    }
}
