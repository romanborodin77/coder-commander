using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Network browser dialog: shows discovered SMB servers and their disk shares in a tree.
/// Double-clicking a share navigates the active panel to it (via <see cref="NavigateRequested"/>).
///
/// <para>Uses <see cref="NetworkBrowser"/>'s WNetEnumResource-based enumeration — the same data
/// source as Windows Explorer's "Network" folder. On modern networks where the master browser
/// protocol is disabled, the list may be empty even though Explorer shows some hosts; this is a
/// Windows networking limitation, not an app bug (documented in AGENTS.md).</para>
/// </summary>
public class NetworkBrowseForm : ThemedForm
{
    private readonly TreeView _tree;
    private readonly Button _closeBtn;
    private readonly Label _statusLabel;

    /// <summary>Raised when a share is double-clicked. EventArgs = UNC path (e.g. <c>\\NAS1\Public</c>).</summary>
    public event EventHandler<string>? NavigateRequested;

    public NetworkBrowseForm()
    {
        var L = LocalizationService.Current;
        Text = L.GetString("Network.Title");
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
            ShowRootLines = false
        };
        _tree.BeforeExpand += OnBeforeExpand;
        _tree.NodeMouseDoubleClick += OnNodeDoubleClick;

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = p.DimForeground,
            Font = p.GridFont,
            TextAlign = ContentAlignment.MiddleLeft,
            Tag = ThemeRole.Muted
        };

        _closeBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Close"));
        _closeBtn.Dock = DockStyle.Right;
        _closeBtn.Click += (_, _) => Close();

        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = p.HeaderBackground,
            Tag = ThemeRole.HeaderBackground,
            Padding = new Padding(16, 8, 16, 8)
        };
        bottomPanel.Controls.Add(_statusLabel);
        bottomPanel.Controls.Add(_closeBtn);

        Controls.Add(_tree);
        Controls.Add(bottomPanel);

        CancelButton = _closeBtn;
        Load += (_, _) => _ = PopulateRootAsync();
    }

    private async Task PopulateRootAsync()
    {
        _statusLabel.Text = LocalizationService.Current.GetString("Network.Scanning");
        _tree.Enabled = false;

        var servers = await Task.Run(NetworkBrowser.EnumerateServers).ConfigureAwait(true);

        _tree.Nodes.Clear();
        foreach (var server in servers)
        {
            var node = new TreeNode(server.Name)
            {
                Tag = server,
                ImageKey = "server",
                SelectedImageKey = "server"
            };
            // Add a dummy child so the expand glyph appears; real shares loaded on BeforeExpand.
            if (server.IsServer)
                node.Nodes.Add(new TreeNode());
            _tree.Nodes.Add(node);
        }

        _tree.Enabled = true;
        var L = LocalizationService.Current;
        _statusLabel.Text = servers.Count > 0
            ? L.GetString("Network.FoundServers", servers.Count)
            : L.GetString("Network.Empty");
    }

    private async void OnBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        if (e.Node?.Tag is not NetworkBrowser.NetResource { IsServer: true } server) return;
        if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0] is { Tag: null })
        {
            e.Node.Nodes.Clear();
            var shares = await Task.Run(() => NetworkBrowser.EnumerateShares(server.UncPath)).ConfigureAwait(true);
            foreach (var share in shares)
            {
                var shareNode = new TreeNode(share.Name) { Tag = share };
                e.Node.Nodes.Add(shareNode);
            }
            if (shares.Count == 0)
                e.Node.Nodes.Add(new TreeNode(LocalizationService.Current.GetString("Network.NoShares")));
        }
    }

    private void OnNodeDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Node.Tag is not NetworkBrowser.NetResource share || share.IsServer) return;
        NavigateRequested?.Invoke(this, share.UncPath);
        Close();
    }
}
