using CoderCommander.FileSystem;
using CoderCommander.ViewModels;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the bug fixed in <see cref="PanelViewModel.RefreshAsync"/>: every call
/// rebuilt _allItems as brand-new FileSystemItem instances, so both checkbox selection and the
/// cursor (SelectedItem) silently reset - including on a refresh the user never asked for, like a
/// FileSystemWatcher debounce firing because an antivirus or sync client touched the folder. A
/// subsequent F8 (Delete) after an unnoticed background refresh could act on a single cursor file
/// instead of the dozens the user actually selected.
/// </summary>
public class PanelViewModelRefreshPreservesSelectionTests
{
    private string _dir = "";

    [SetUp]
    public void CreateFixtureFiles()
    {
        _dir = Directory.CreateTempSubdirectory("cc_panel_refresh_select_test_").FullName;
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "b.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "c.txt"), "x");
    }

    [TearDown]
    public void DeleteFixtureFiles()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Test]
    public async Task RefreshAsync_PreservesCheckboxSelectionAndCursorAcrossTheRebuild()
    {
        var vm = new PanelViewModel(new LocalFileSystem());
        await vm.NavigateAsync(_dir);

        var a = vm.Items.Single(i => i.Name == "a.txt");
        var b = vm.Items.Single(i => i.Name == "b.txt");
        a.IsSelected = true;
        b.IsSelected = true;
        vm.NotifySelectionChanged();
        vm.SelectedItem = b;
        Assert.That(vm.SelectedCount, Is.EqualTo(2));

        await vm.RefreshAsync();

        var newA = vm.Items.Single(i => i.Name == "a.txt");
        var newB = vm.Items.Single(i => i.Name == "b.txt");
        var newC = vm.Items.Single(i => i.Name == "c.txt");

        Assert.That(newA.IsSelected, Is.True, "a.txt's checkbox selection must survive a refresh");
        Assert.That(newB.IsSelected, Is.True, "b.txt's checkbox selection must survive a refresh");
        Assert.That(newC.IsSelected, Is.False);
        Assert.That(vm.SelectedCount, Is.EqualTo(2));
        Assert.That(vm.SelectedItem?.Name, Is.EqualTo("b.txt"), "The cursor item must still point at b.txt (a new instance), not be reset");
    }
}
