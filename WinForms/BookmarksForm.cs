using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Represents a saved folder bookmark with a display name and path.
/// </summary>
public class BookmarkEntry
{
    /// <summary>Display name for the bookmark.</summary>
    public string Name { get; set; } = "";

    /// <summary>Filesystem path of the bookmarked folder.</summary>
    public string Path { get; set; } = "";

    /// <summary>Timestamp when the bookmark was created.</summary>
    public DateTime Created { get; set; } = DateTime.Now;
}

/// <summary>
/// Singleton persistence store for <see cref="BookmarkEntry"/> items,
/// backed by a tab-delimited text file in <see cref="DataDirectory.Root"/>.
/// </summary>
public sealed class BookmarkStore
{
    /// <summary>Shared singleton instance.</summary>
    public static BookmarkStore Instance { get; } = new();

    /// <summary>The current list of bookmarks.</summary>
    public List<BookmarkEntry> Items { get; } = [];

    /// <summary>Adds a bookmark if no entry with the same path (case-insensitive) exists.</summary>
    /// <param name="name">Display name.</param>
    /// <param name="path">Filesystem path.</param>
    public void Add(string name, string path)
    {
        if (Items.Any(b => b.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
        Items.Add(new BookmarkEntry { Name = SanitizeField(name), Path = SanitizeField(path), Created = DateTime.Now });
        Save();
    }

    /// <summary>Strips characters that would corrupt the tab-delimited persistence format
    /// (<c>Save</c>/<c>Load</c> below) - an embedded TAB/newline shifts <c>Load</c>'s
    /// <c>Split</c> or fragments one bookmark into multiple malformed lines in the file.
    /// Both <see cref="BookmarkEntry.Name"/> and <see cref="BookmarkEntry.Path"/> are sanitized:
    /// VFS paths (archive entries like <c>archive.zip|inner/path</c>, remote URLs) can contain
    /// <c>|</c> and other characters that were unsafe with the old pipe-delimited format.</summary>
    private static string SanitizeField(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>Removes the given bookmark entry and persists the change.</summary>
    public void Remove(BookmarkEntry entry)
    {
        Items.Remove(entry);
        Save();
    }

    /// <summary>Full path to the bookmarks persistence file in DataDirectory.Root.</summary>
    private static string FilePath => Path.Combine(DataDirectory.Root, "bookmarks.txt");

    /// <summary>Persists all bookmarks to the text file.</summary>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath) ?? FilePath;
            Directory.CreateDirectory(dir);

            // Write-then-replace, same pattern as SettingsService.Save - a crash mid-write must
            // not leave bookmarks.txt truncated.
            var tempPath = FilePath + ".tmp";
            File.WriteAllLines(tempPath, Items.Select(b => $"{b.Name}\t{b.Path}"));
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to save bookmarks", ex);
        }
    }

    /// <summary>Loads bookmarks from the persistence file, replacing any in-memory entries.</summary>
    public void Load()
    {
        Items.Clear();
        try
        {
            if (File.Exists(FilePath))
            {
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    // Try tab delimiter first (current format), fall back to pipe (legacy).
                    var parts = line.Split('\t', 2);
                    if (parts.Length == 2)
                    {
                        Items.Add(new BookmarkEntry { Name = parts[0], Path = parts[1] });
                        continue;
                    }
                    parts = line.Split('|', 2);
                    if (parts.Length == 2)
                        Items.Add(new BookmarkEntry { Name = parts[0], Path = parts[1] });
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Warning($"Failed to load bookmarks: {ex.Message}");
        }
    }
}

/// <summary>
/// Bookmark management form: add, remove, and navigate to folder bookmarks.
/// </summary>
public sealed partial class BookmarksForm : ThemedForm
{
    private readonly Func<string, Task<bool>> _pathExists;

    /// <summary>Raised when a bookmark is double-clicked (navigate to it).</summary>
    public event EventHandler<string>? BookmarkActivated;

    /// <summary>Initializes the bookmarks dialog and loads persisted bookmarks.</summary>
    /// <param name="pathExists">Validates a hand-typed bookmark path before it's saved - normally
    /// <c>MainViewModel.PathExistsAsync</c>, which (unlike a bare <c>Directory.Exists</c>) also
    /// accepts an archive or connection path, so a folder inside either can actually be
    /// bookmarked.</param>
    public BookmarksForm(Func<string, Task<bool>> pathExists)
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _pathExists = pathExists;

        var L = LocalizationService.Current;
        // A ColumnHeader is not a Control and cannot carry a LocalizationKey.
        _colName.Text = L.GetString("Bookmark.Col.Name");
        _colPath.Text = L.GetString("Bookmark.Col.Path");

        // Set here rather than in the designer: ThemedForm.Resizable is this app's own property,
        // applied in OnLoad rather than a real FormBorderStyle the designer could round-trip.
        Resizable = true;

        BookmarkStore.Instance.Load();

        _listView.DoubleClick += OnBookmarkDoubleClick;
        _addBtn.Click += (_, _) => AddBookmark();
        _removeBtn.Click += (_, _) => RemoveSelected();
        _closeBtn.Click += (_, _) => Close();

        Load += (_, _) => RefreshList();
    }

    /// <summary>Handles double-click on a bookmark: raises <see cref="BookmarkActivated"/>.</summary>
    private void OnBookmarkDoubleClick(object? sender, EventArgs e)
    {
        if (_listView.SelectedItems.Count > 0)
        {
            if (_listView.SelectedItems[0].Tag is BookmarkEntry b)
            {
                BookmarkActivated?.Invoke(this, b.Path);
                Close();
            }
        }
    }

    /// <summary>Rebuilds the <see cref="ListView"/> from <see cref="BookmarkStore.Items"/>.</summary>
    private void RefreshList()
    {
        var L = LocalizationService.Current;
        _listView.BeginUpdate();
        _listView.Items.Clear();

        if (BookmarkStore.Instance.Items.Count == 0)
        {
            var empty = new ListViewItem(L.GetString("Bookmark.Empty")) { ForeColor = DesignerSafeThemeService.Current.DimForeground };
            _listView.Items.Add(empty);
        }
        else
        {
            foreach (var b in BookmarkStore.Instance.Items)
            {
                var lvi = new ListViewItem(b.Name);
                lvi.SubItems.Add(b.Path);
                lvi.Tag = b;
                _listView.Items.Add(lvi);
            }
        }
        _listView.EndUpdate();
    }

    /// <summary>Prompts for a name and path, then adds a new bookmark. <c>async void</c> - the
    /// established pattern this codebase uses for a UI event handler that needs to await (see
    /// e.g. <c>MainForm.OnArchiveEntered</c>); the button's own <c>Click</c> handler already treats
    /// this as fire-and-forget.</summary>
    private async void AddBookmark()
    {
        try
        {
            var L = LocalizationService.Current;
            using var dlg = new InputDialogForm(L.GetString("Input.AddBookmark"), L.GetString("Input.BookmarkName"));
            if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.Value))
            {
                using var pathDlg = new InputDialogForm(L.GetString("Input.AddBookmark"), L.GetString("Input.BookmarkPath"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                if (pathDlg.ShowDialog(this) == DialogResult.OK)
                {
                    var path = pathDlg.Value;
                    var name = dlg.Value;
                    // _pathExists (not a bare Directory.Exists) accepts an archive/connection path too -
                    // see the constructor's own doc comment. IsDisposed guards against the dialog having
                    // been closed while this await was in flight.
                    if (await _pathExists(path).ConfigureAwait(true) && !IsDisposed)
                    {
                        BookmarkStore.Instance.Add(name, path);
                        RefreshList();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // async void without try/catch would escalate to WinForms ThreadException,
            // bypassing Program.cs's top-level handler.
            LogService.Error($"AddBookmark failed: {ex.Message}");
        }
    }

    /// <summary>Removes the currently selected bookmark from the store.</summary>
    private void RemoveSelected()
    {
        if (_listView.SelectedItems.Count > 0 && _listView.SelectedItems[0].Tag is BookmarkEntry b)
        {
            BookmarkStore.Instance.Remove(b);
            RefreshList();
        }
    }

}
