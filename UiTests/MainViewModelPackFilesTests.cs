using CoderCommander.FileSystem;
using CoderCommander.Models;
using CoderCommander.ViewModels;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the wrong-error-message fix in <see cref="MainViewModel.PackFiles"/>:
/// pressing Pack while browsing inside an archive used to raise "Archive.SameArchiveTransfer" -
/// a message written for a different rejection (copying within the same archive) - instead of a
/// dedicated key, unlike the identically-shaped guards in Wipe()/CalculateFolderSize().
/// </summary>
public class MainViewModelPackFilesTests
{
    [Test]
    public void PackFiles_InsideArchive_RaisesDedicatedPackUnsupportedKey()
    {
        using var vm = new MainViewModel();

        // ZipArchiveFileSystem does no I/O at construction time - the path never needs to exist
        // for PanelViewModel.IsInsideArchive's type check (`_fs is ZipArchiveFileSystem`) or for
        // PackFiles' early-return path, neither of which touches the archive itself.
        vm.LeftPanel.CurrentFileSystem = new ZipArchiveFileSystem(@"C:\fake.zip");
        vm.SetActivePanel(vm.LeftPanel);

        var entry = new FileEntry(@"C:\fake.zip|inner.txt", isDirectory: false);
        var item = new FileSystemItem(entry) { IsSelected = true };
        vm.LeftPanel.Items.Add(item);

        string? rejectedKey = null;
        vm.OperationRejected += (_, key) => rejectedKey = key;

        vm.PackFiles();

        Assert.That(rejectedKey, Is.EqualTo("Archive.PackUnsupported"));
    }
}
