using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Directory tree navigation window with lazy-loading nodes.
/// </summary>
public class DirectoryTreeForm : ThemedForm
{
    private readonly TreeView _tree;
    private readonly Button _closeBtn;

    /// <summary>Raised when a node is double-clicked (navigate to that folder).</summary>
    public event EventHandler<string>? NavigateRequested;

    public DirectoryTreeForm(string rootPath)
    {
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
        Load += (_, _) => PopulateRoot(rootPath);
    }

    private void PopulateRoot(string rootPath)
    {
        _tree.Nodes.Clear();
        var root = CreateNode(rootPath);
        if (root != null)
        {
            _tree.Nodes.Add(root);
            root.Expand();
        }
    }

    private static TreeNode? CreateNode(string path)
    {
        if (!Directory.Exists(path)) return null;
        var di = new DirectoryInfo(path);
        var node = new TreeNode(di.Name == "" ? path : di.Name) { Tag = path };
        LoadChildDirs(node, path);
        return node;
    }

    private static void LoadChildDirs(TreeNode parent, string path)
    {
        parent.Nodes.Clear();
        try
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                var di = new DirectoryInfo(dir);
                if ((di.Attributes & FileAttributes.Hidden) != 0) continue;
                var child = new TreeNode(di.Name) { Tag = dir };
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

    private void OnBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        if (e.Node?.Tag is string path && e.Node.Nodes.Count > 0 && e.Node.Nodes[0].Text == "...")
        {
            LoadChildDirs(e.Node, path);
        }
    }
}
