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
public sealed partial class NetworkBrowseForm : ThemedForm
{
    /// <summary>Raised when a share is double-clicked. EventArgs = UNC path (e.g. <c>\\NAS1\Public</c>).</summary>
    public event EventHandler<string>? NavigateRequested;

    public NetworkBrowseForm()
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        // Set here rather than in the designer: ThemedForm.Resizable is this app's own property,
        // applied in OnLoad rather than a real FormBorderStyle the designer could round-trip.
        Resizable = true;

        _tree.BeforeExpand += OnBeforeExpand;
        _tree.NodeMouseDoubleClick += OnNodeDoubleClick;
        _closeBtn.Click += (_, _) => Close();

        Load += (_, _) => _ = PopulateRootAsync();
    }

    private async Task PopulateRootAsync()
    {
        try
        {
            _statusLabel.Text = LocalizationService.Current.GetString("Network.Scanning");
            _tree.Enabled = false;

            var servers = await Task.Run(NetworkBrowser.EnumerateServers).ConfigureAwait(true);

            if (IsDisposed) return;

            _tree.Nodes.Clear();
            foreach (var server in servers)
            {
                var node = new TreeNode(server.Name)
                {
                    Tag = server
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
        catch (Exception ex)
        {
            LogService.Error($"PopulateRootAsync failed: {ex.Message}", ex);
        }
    }

    private async void OnBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        if (e.Node?.Tag is not NetworkBrowser.NetResource { IsServer: true } server) return;
        if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0] is { Tag: null })
        {
            e.Node.Nodes.Clear();
            try
            {
                var shares = await Task.Run(() => NetworkBrowser.EnumerateShares(server.UncPath)).ConfigureAwait(true);
                if (IsDisposed) return;
                foreach (var share in shares)
                {
                    var shareNode = new TreeNode(share.Name) { Tag = share };
                    e.Node.Nodes.Add(shareNode);
                }
                if (shares.Count == 0)
                    e.Node.Nodes.Add(new TreeNode(LocalizationService.Current.GetString("Network.NoShares")));
            }
            catch (Exception ex)
            {
                LogService.Warning($"Network share enumeration failed for {server.UncPath}: {ex.Message}");
                if (!IsDisposed)
                    e.Node.Nodes.Add(new TreeNode(LocalizationService.Current.GetString("Network.NoShares")));
            }
        }
    }

    private void OnNodeDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Node.Tag is not NetworkBrowser.NetResource share || share.IsServer) return;
        NavigateRequested?.Invoke(this, share.UncPath);
        Close();
    }

}
