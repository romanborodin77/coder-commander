using System.Reflection;
using CoderCommander.FileSystem;
using CoderCommander.Models;
using CoderCommander.ViewModels;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the bug fixed in PanelViewModel's private FileComparer: sorting by a
/// non-Name column (Size, Modified, Extension) had no tie-breaker for entries whose primary key
/// is equal - List&lt;T&gt;.Sort (introsort) isn't a stable sort, so those rows could visibly swap
/// position on every re-sort (toggling DirectoriesFirst, a FileSystemWatcher-triggered
/// RefreshAsync, etc.) with no user-visible cause. Uses reflection since FileComparer is internal
/// to PanelViewModel.cs.
/// </summary>
public class FileComparerTieBreakerTests
{
    private static IComparer<FileSystemItem> CreateComparer(bool dirsFirst, string column, bool descending)
    {
        var type = typeof(PanelViewModel).Assembly.GetType("CoderCommander.ViewModels.FileComparer");
        Assert.That(type, Is.Not.Null, "PanelViewModel.cs must still define a FileComparer type");
        return (IComparer<FileSystemItem>)Activator.CreateInstance(type!, dirsFirst, column, descending)!;
    }

    [Test]
    public void Compare_EqualSize_TieBreaksByNameAscending()
    {
        var b = new FileSystemItem(new FileEntry(@"C:\dir\b.txt", isDirectory: false, size: 100));
        var a = new FileSystemItem(new FileEntry(@"C:\dir\a.txt", isDirectory: false, size: 100));

        var comparer = CreateComparer(dirsFirst: true, column: "Size", descending: false);

        Assert.That(comparer.Compare(b, a), Is.GreaterThan(0), "b.txt must sort after a.txt when their Size ties");
        Assert.That(comparer.Compare(a, b), Is.LessThan(0), "a.txt must sort before b.txt when their Size ties");
    }

    [Test]
    public void Compare_EqualSizeDescendingSort_TieStillBreaksByNameAscending()
    {
        var b = new FileSystemItem(new FileEntry(@"C:\dir\b.txt", isDirectory: false, size: 100));
        var a = new FileSystemItem(new FileEntry(@"C:\dir\a.txt", isDirectory: false, size: 100));

        var comparer = CreateComparer(dirsFirst: true, column: "Size", descending: true);

        Assert.That(comparer.Compare(a, b), Is.LessThan(0),
            "Even with the primary column sorted descending, the name tie-break must stay ascending so ties settle consistently");
    }
}
