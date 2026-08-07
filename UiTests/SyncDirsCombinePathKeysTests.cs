using System.Reflection;
using CoderCommander.WinForms;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the case-sensitivity fix in <see cref="SyncDirsForm"/>'s path-combining
/// step: leftMap/rightMap (built by BuildMap) are keyed with StringComparer.OrdinalIgnoreCase,
/// but the original code combined their keys via the default (ordinal, case-sensitive) Union
/// overload. A path existing on both sides but differing only by case - plausible whenever the
/// two trees were populated independently - produced two distinct strings, each of which found
/// the same underlying file via the dictionaries' own case-insensitive lookups: the same file
/// ended up listed (and counted) twice. CombinePathKeys/BuildMap are private static - invoked via
/// reflection.
/// </summary>
public class SyncDirsCombinePathKeysTests
{
    private static readonly MethodInfo BuildMap = typeof(SyncDirsForm)
        .GetMethod("BuildMap", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo CombinePathKeys = typeof(SyncDirsForm)
        .GetMethod("CombinePathKeys", BindingFlags.NonPublic | BindingFlags.Static)!;

    private string _leftRoot = "";
    private string _rightRoot = "";

    [SetUp]
    public void CreateTempDirs()
    {
        _leftRoot = Directory.CreateTempSubdirectory("cc_syncdirs_left_").FullName;
        _rightRoot = Directory.CreateTempSubdirectory("cc_syncdirs_right_").FullName;
    }

    [TearDown]
    public void DeleteTempDirs()
    {
        if (Directory.Exists(_leftRoot)) Directory.Delete(_leftRoot, recursive: true);
        if (Directory.Exists(_rightRoot)) Directory.Delete(_rightRoot, recursive: true);
    }

    [Test]
    public void CombinePathKeys_SameFileDifferentCaseOnBothSides_YieldsOneCombinedKeyNotTwo()
    {
        File.WriteAllText(Path.Combine(_leftRoot, "Report.txt"), "left content");
        File.WriteAllText(Path.Combine(_rightRoot, "report.txt"), "right content");

        var leftMap = BuildMap.Invoke(null, new object?[] { _leftRoot, true })!;
        var rightMap = BuildMap.Invoke(null, new object?[] { _rightRoot, true })!;

        var combined = ((IEnumerable<string>)CombinePathKeys.Invoke(null, new[] { leftMap, rightMap })!).ToList();

        Assert.That(combined.Count(p => string.Equals(p, "Report.txt", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1),
            "A file present on both sides under different casing must appear exactly once, not once per casing variant");
    }
}
