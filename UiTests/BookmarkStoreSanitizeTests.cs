using CoderCommander.WinForms;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the persistence-corruption fix in <see cref="BookmarkStore"/>: a bookmark
/// name containing <c>|</c> or a newline used to be written verbatim into the pipe-delimited
/// <c>bookmarks.txt</c>, shifting Load()'s <c>Split('|', 2)</c> so part of the name was read back
/// as the path (or fragmenting the entry across multiple malformed lines) on every subsequent
/// load. <see cref="BookmarkStore.Instance"/> is a real AppData-backed singleton, so this test
/// captures and restores the user's actual bookmarks around the test rather than touching a fake
/// copy.
/// </summary>
public class BookmarkStoreSanitizeTests
{
    private List<BookmarkEntry> _originalItems = new();

    [SetUp]
    public void CaptureOriginalItems()
    {
        _originalItems = BookmarkStore.Instance.Items
            .Select(b => new BookmarkEntry { Name = b.Name, Path = b.Path, Created = b.Created })
            .ToList();
    }

    [TearDown]
    public void RestoreOriginalItems()
    {
        var items = BookmarkStore.Instance.Items;
        items.Clear();
        items.AddRange(_originalItems);
        BookmarkStore.Instance.Save();
    }

    [Test]
    public void Add_NameContainingPipeAndNewline_SurvivesSaveLoadRoundTripIntact()
    {
        var testPath = @"C:\cc_bookmark_sanitize_test_" + Guid.NewGuid();
        BookmarkStore.Instance.Add("Foo|Bar\r\nBaz", testPath);

        var added = BookmarkStore.Instance.Items.Single(b => b.Path == testPath);
        Assert.That(added.Name, Does.Not.Contain("|"));
        Assert.That(added.Name, Does.Not.Contain("\r"));
        Assert.That(added.Name, Does.Not.Contain("\n"));

        // Round-trip through the real Save/Load path (the actual persistence format) and confirm
        // the path survives intact - before the fix, an embedded "|" shifted Split('|', 2) so the
        // reloaded Path would no longer equal what was added.
        BookmarkStore.Instance.Load();
        var reloaded = BookmarkStore.Instance.Items.SingleOrDefault(b => b.Path == testPath);
        Assert.That(reloaded, Is.Not.Null, "Entry must survive a save/load round trip unfragmented");
        Assert.That(reloaded!.Name, Is.EqualTo(added.Name));
        Assert.That(reloaded.Path, Is.EqualTo(testPath));
    }
}
