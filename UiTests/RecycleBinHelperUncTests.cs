using System.Diagnostics;
using CoderCommander.FileSystem;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the silent-permanent-deletion bug fixed in
/// <see cref="RecycleBinHelper.MoveToRecycleBin(IReadOnlyList{string})"/>: the Recycle Bin
/// doesn't exist for network locations, so SHFileOperationW's FOF_ALLOWUNDO silently fell back to
/// permanent deletion for a UNC path - with FOF_NOCONFIRMATION suppressing the only warning that
/// would have said so - and this method still returned true (success). DeleteOperation already
/// has a robust fallback (confirm-or-skip permanent deletion) for when this method returns false;
/// it just never used to be told.
/// </summary>
public class RecycleBinHelperUncTests
{
    [Test]
    public void MoveToRecycleBin_UncPath_ReturnsFalseWithoutAttemptingTheShellCall()
    {
        // The pre-fix code also happens to return false for an unreachable UNC path (a network
        // timeout eventually fails the shell call the same way) - a plain True/False assertion
        // alone doesn't distinguish "rejected up front" from "timed out and then failed", so this
        // also checks that it returns near-instantly rather than after a multi-second network
        // round-trip (verified pre-fix: ~2s for this exact path; post-fix: single-digit ms).
        var sw = Stopwatch.StartNew();
        var result = RecycleBinHelper.MoveToRecycleBin(new[] { @"\\nonexistent-test-server\share\file.txt" });
        sw.Stop();

        Assert.That(result, Is.False);
        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)),
            $"A UNC path must be rejected up front, not attempted via the shell call (took {sw.Elapsed})");
    }

    [Test]
    public void MoveToRecycleBin_MixedLocalAndUncPaths_ReturnsFalseIfAnyIsUnc()
    {
        var localTemp = Path.Combine(Path.GetTempPath(), $"cc_recyclebin_unc_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(localTemp, "x");
        try
        {
            var result = RecycleBinHelper.MoveToRecycleBin(new[] { localTemp, @"\\nonexistent-test-server\share\file.txt" });
            Assert.That(result, Is.False);
            Assert.That(File.Exists(localTemp), Is.True, "The local file must not be touched when the batch is rejected up front");
        }
        finally
        {
            if (File.Exists(localTemp)) File.Delete(localTemp);
        }
    }
}
