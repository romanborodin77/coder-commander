using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Bookmark manager: add/remove folder bookmarks.
/// </summary>
public class BookmarkEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
}

public sealed class BookmarkStore
{
    public static BookmarkStore Instance { get; } = new();
    public List<BookmarkEntry> Items { get; } = [];

    public void Add(string name, string path)
    {
        if (Items.Any(b => b.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
        Items.Add(new BookmarkEntry { Name = name, Path = path, Created = DateTime.Now });
        Save();
    }

    public void Remove(BookmarkEntry entry)
    {
        Items.Remove(entry);
        Save();
    }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CoderCommander", "bookmarks.txt");

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath) ?? FilePath;
            Directory.CreateDirectory(dir);
            File.WriteAllLines(FilePath, Items.Select(b => $"{b.Name}|{b.Path}"));
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to save bookmarks", ex);
        }
    }

    public void Load()
    {
        Items.Clear();
        try
        {
            if (File.Exists(FilePath))
            {
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var parts = line.Split('|', 2);
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
/// Bookmark management form.
/// </summary>
public class BookmarksForm : ThemedForm
{
    private readonly ListView _listView;
    private readonly Button _addBtn;
    private readonly Button _removeBtn;
    private readonly Button _closeBtn;

    /// <summary>Raised when a bookmark is double-clicked (navigate to it).</summary>
    public event EventHandler<string>? BookmarkActivated;

    public BookmarksForm()
    {
        var L = LocalizationService.Current;
        Text = L.GetString("Bookmark.Title");
        ClientSize = new Size(600, 400);
        Resizable = true;
        MinimumSize = new Size(400, 280);

        BookmarkStore.Instance.Load();

        _listView = UiHelpers.CreateListView(
            (L.GetString("Bookmark.Col.Name"), 150),
            (L.GetString("Bookmark.Col.Path"), 400));
        _listView.Dock = DockStyle.Fill;
        _listView.DoubleClick += OnBookmarkDoubleClick;

        var p = ThemeService.Current;
        var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = p.HeaderBackground, Tag = ThemeRole.HeaderBackground, Padding = new Padding(16, 8, 16, 8) };

        _addBtn = ThemedForm.CreateThemedButton(L.GetString("Bookmark.Add"), accent: true);
        _addBtn.Margin = new Padding(0, 0, 8, 0);
        _addBtn.Click += (_, _) => AddBookmark();

        _removeBtn = ThemedForm.CreateThemedButton(L.GetString("Bookmark.Remove"));
        _removeBtn.Margin = new Padding(0);
        _removeBtn.Click += (_, _) => RemoveSelected();

        // Two same-side Dock.Left buttons stack from the last-added control outward (outermost
        // = leftmost), which had silently rendered these as "Remove Add" instead of "Add
        // Remove" - a FlowLayoutPanel makes the visual order match the add order directly, and
        // its Margin actually renders (Dock.Left/Right ignore it entirely).
        var leftGroup = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        leftGroup.Controls.Add(_addBtn);
        leftGroup.Controls.Add(_removeBtn);

        _closeBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Close"));
        _closeBtn.Dock = DockStyle.Right;
        _closeBtn.Click += (_, _) => Close();

        btnPanel.Controls.Add(_closeBtn);
        btnPanel.Controls.Add(leftGroup);

        // Dock=Fill must be added before Dock=Bottom/Top/Left/Right siblings (see
        // WinForms/DirectoryTreeForm.cs for the full explanation).
        Controls.Add(_listView);
        Controls.Add(btnPanel);

        CancelButton = _closeBtn;
        Load += (_, _) => RefreshList();
    }

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

    private void RefreshList()
    {
        var L = LocalizationService.Current;
        _listView.BeginUpdate();
        _listView.Items.Clear();

        if (BookmarkStore.Instance.Items.Count == 0)
        {
            var empty = new ListViewItem(L.GetString("Bookmark.Empty")) { ForeColor = ThemeService.Current.DimForeground };
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

    private void AddBookmark()
    {
        var L = LocalizationService.Current;
        using var dlg = new InputDialogForm(L.GetString("Input.AddBookmark"), L.GetString("Input.BookmarkName"));
        if (dlg.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(dlg.Value))
        {
            using var pathDlg = new InputDialogForm(L.GetString("Input.AddBookmark"), L.GetString("Input.BookmarkPath"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            if (pathDlg.ShowDialog() == DialogResult.OK && Directory.Exists(pathDlg.Value))
            {
                BookmarkStore.Instance.Add(dlg.Value, pathDlg.Value);
                RefreshList();
            }
        }
    }

    private void RemoveSelected()
    {
        if (_listView.SelectedItems.Count > 0 && _listView.SelectedItems[0].Tag is BookmarkEntry b)
        {
            BookmarkStore.Instance.Remove(b);
            RefreshList();
        }
    }
}
