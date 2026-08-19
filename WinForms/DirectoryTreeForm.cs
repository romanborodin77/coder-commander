using CoderCommander.FileSystem;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Directory tree navigation window with lazy-loading nodes. Reads through
/// <see cref="IFileSystem"/> (not <c>System.IO</c> directly) - opened with
/// <c>ActivePanel.CurrentPath</c>, which can legitimately be an archive or remote-connection path,
/// this used to silently show an empty tree (or throw) the moment the active panel was browsing
/// anything other than a real local drive: <c>Directory.Exists</c>/<c>Directory.GetDirectories</c>
/// don't understand an archive's <c>archive.zip|inner/dir</c> syntax or a connection's
/// <c>sftp://host/dir</c> syntax at all.
/// </summary>
public class DirectoryTreeForm : ThemedForm
{
    private readonly TreeView _tree;
    private readonly Button _closeBtn;
    private readonly IFileSystem _fs;

    /// <summary>Raised when a node is double-clicked (navigate to that folder).</summary>
    public event EventHandler<string>? NavigateRequested;

    /// <param name="rootPath">The root directory to start the tree from.</param>
    /// <param name="fs">The file system <paramref name="rootPath"/> (and everything under it)
    /// lives on - normally the active panel's own <c>CurrentFileSystem</c>, so the tree matches
    /// exactly what that panel is browsing, archive/connection included.</param>
    public DirectoryTreeForm(string rootPath, IFileSystem fs)
    {
        _fs = fs;
        var L = LocalizationService.Current;
        Text = L.GetString("DirTree.Title");
        ClientSize = new Size(480, 520);
        Resizable = true;
        MinimumSize = new Size(320, 300);

        var p = ThemeService.Current;

        _tree = new TreeView
        {
            Dock = DockStyle.Fill,
            Font = p.GridFont,
            BackColor = p.PanelBackground,
            ForeColor = p.Foreground,
            BorderStyle = BorderStyle.None,
            ShowLines = true,
            ShowPlusMinus = true,
            ShowRootLines = true
        };
        _tree.BeforeExpand += OnBeforeExpand;
        _tree.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Node.Tag is string path)
                NavigateRequested?.Invoke(this, path);
        };

        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = p.HeaderBackground, Tag = ThemeRole.HeaderBackground, Padding = new Padding(16, 8, 16, 8) };
        _closeBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Close"));
        _closeBtn.Dock = DockStyle.Right;
        _closeBtn.Click += (_, _) => Close();
        bottomPanel.Controls.Add(_closeBtn);

        // Dock=Fill must be added before Dock=Bottom/Top/Left/Right siblings - WinForms lays
        // out docked children from the last-added index down to the first, so adding Fill after
        // Bottom here left _tree's layout extending under bottomPanel (invisible only because
        // bottomPanel is opaque and added-first = frontmost z-order).
        Controls.Add(_tree);
        Controls.Add(bottomPanel);

        CancelButton = _closeBtn;
        Load += (_, _) => _ = PopulateRootAsync(rootPath);
    }

    /// <summary>Populates the tree with the root node and its immediate children.</summary>
    private async Task PopulateRootAsync(string rootPath)
    {
        _tree.Nodes.Clear();
        var root = await CreateNodeAsync(rootPath).ConfigureAwait(true);
        if (root != null)
        {
            _tree.Nodes.Add(root);
            root.Expand();
        }
    }

    /// <summary>Creates a <see cref="TreeNode"/> for a directory path, with lazy-loaded children.</summary>
    private async Task<TreeNode?> CreateNodeAsync(string path)
    {
        if (!await _fs.ExistsAsync(path).ConfigureAwait(true)) return null;
        var name = VfsPath.GetName(path);
        var node = new TreeNode(string.IsNullOrEmpty(name) ? path : name) { Tag = path };
        await LoadChildDirsAsync(node, path).ConfigureAwait(true);
        return node;
    }

    /// <summary>Reconciles a node's children against the file system (excluding hidden
    /// directories): adds folders that are new, removes ones that no longer exist, and leaves
    /// nodes for folders that still exist - along with any descendants the user had already
    /// expanded under them - completely untouched. Deliberately not a Nodes.Clear()-and-rebuild:
    /// that reads identically for a freshly-created, still-empty parent (the common case, from
    /// <see cref="CreateNodeAsync"/>), but on a re-expand it would destroy the whole
    /// previously-expanded subtree underneath, since <see cref="TreeNodeCollection.Clear"/>
    /// removes descendants along with their parent - collapsing e.g. A\B\C\D back to just
    /// "A → B" on every re-expand of A.</summary>
    private async Task LoadChildDirsAsync(TreeNode parent, string path)
    {
        try
        {
            var entries = await _fs.EnumerateAsync(path, includeHidden: false, CancellationToken.None).ConfigureAwait(true);

            var onDisk = new List<(string Dir, string Name)>();
            var onDiskPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (!entry.IsDirectory) continue;
                onDisk.Add((entry.FullPath, entry.Name));
                onDiskPaths.Add(entry.FullPath);
            }

            for (var i = parent.Nodes.Count - 1; i >= 0; i--)
            {
                // Keep only nodes that still exist. This also removes the dummy "..." placeholder
                // (Tag is null, so it never matches the "still exists" check) - it must go too, or
                // it would linger alongside the real children added below.
                if (parent.Nodes[i].Tag is string existingPath && onDiskPaths.Contains(existingPath))
                    continue;
                parent.Nodes.RemoveAt(i);
            }

            var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TreeNode node in parent.Nodes)
                if (node.Tag is string p) existingPaths.Add(p);

            foreach (var (dir, name) in onDisk)
            {
                if (existingPaths.Contains(dir)) continue;
                var child = new TreeNode(name) { Tag = dir };
                // Add a dummy node so the + appears
                child.Nodes.Add(new TreeNode("..."));
                parent.Nodes.Add(child);
            }
        }
        catch (Exception ex)
        {
            LogService.Warning($"Failed to populate tree: {ex.Message}");
        }
    }

    /// <summary>Reconciles a node's children against disk on every expand - not just the first
    /// time (when the only child is still the dummy "..." placeholder). Re-scanning
    /// unconditionally means a folder created/deleted outside the app while this dialog is open
    /// shows up the next time its parent is re-expanded, instead of staying stuck on whatever was
    /// there the first time the node was ever opened for the rest of the dialog's lifetime -
    /// without losing the user's already-expanded descendants (see <see cref="LoadChildDirsAsync"/>).</summary>
    private async void OnBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        if (e.Node?.Tag is string path)
        {
            await LoadChildDirsAsync(e.Node, path).ConfigureAwait(true);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tree?.Dispose();
            _closeBtn?.Dispose();
        }
        base.Dispose(disposing);
    }
}
