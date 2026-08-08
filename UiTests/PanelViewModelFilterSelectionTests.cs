using CoderCommander.FileSystem;
using CoderCommander.ViewModels;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression tests for two related bugs fixed in <see cref="PanelViewModel.ApplyFilter"/>:
/// filtering never touched selection state, so (a) a dangling cursor item hidden by the filter
/// stayed the GetSelectedOrActive() fallback target - an operation like Delete could silently act
/// on a file the user can't see - and (b) checkbox-selected items hidden by the filter survived
/// untouched by Select All/Deselect All/Invert (which only ever operate on the visible Items) and
/// resurfaced, still selected, once the filter was cleared or widened.
/// </summary>
public class PanelViewModelFilterSelectionTests
{
    private string _dir = "";

    [SetUp]
    public void CreateFixtureFiles()
    {
        _dir = Directory.CreateTempSubdirectory("cc_panel_filter_test_").FullName;
        File.WriteAllText(Path.Combine(_dir, "report.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "important-backup.zip"), "x");
    }

    [TearDown]
    public void DeleteFixtureFiles()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Test]
    public async Task Filter_HidesCursorItem_ClearsSelectedItemInsteadOfLeavingItAsDanglingTarget()
    {
        var vm = new PanelViewModel(new LocalFileSystem());
        await vm.NavigateAsync(_dir);

        var zip = vm.Items.Single(i => i.Name == "important-backup.zip");
        vm.SelectedItem = zip; // cursor on the zip, nothing checkbox-selected

        vm.Filter = "notes"; // hides the zip from view

        Assert.That(vm.SelectedItem, Is.Null,
            "SelectedItem must be cleared once the filter hides it - otherwise GetSelectedOrActive() still targets a file the user can't see");
        Assert.That(vm.GetSelectedOrActive(), Is.Empty,
            "An action like Delete must not silently fall back to a filtered-out file");
    }

    [Test]
    public async Task Filter_HidesCheckboxSelectedItems_DeselectsThemInsteadOfLettingThemResurface()
    {
        var vm = new PanelViewModel(new LocalFileSystem());
        await vm.NavigateAsync(_dir);

        var report = vm.Items.Single(i => i.Name == "report.txt");
        var notes = vm.Items.Single(i => i.Name == "notes.txt");
        report.IsSelected = true;
        notes.IsSelected = true;
        vm.NotifySelectionChanged();
        Assert.That(vm.SelectedCount, Is.EqualTo(2));

        vm.Filter = "zip"; // hides both checkbox-selected files

        Assert.That(report.IsSelected, Is.False, "A checkbox-selected item hidden by the filter must be deselected");
        Assert.That(notes.IsSelected, Is.False);
        Assert.That(vm.SelectedCount, Is.EqualTo(0));

        vm.Filter = ""; // clear the filter

        Assert.That(report.IsSelected, Is.False, "Must not resurface as selected once the filter no longer hides it");
        Assert.That(notes.IsSelected, Is.False);
        Assert.That(vm.SelectedCount, Is.EqualTo(0));
    }
}
