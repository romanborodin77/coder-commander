using CoderCommander.Services;
using System.Security.Cryptography;

namespace CoderCommander.WinForms;

/// <summary>
/// Computes MD5, SHA1, SHA256 for selected files.
/// </summary>
public class ChecksumForm : ThemedForm
{
    private readonly ListView _fileList;
    private readonly ListView _resultList;
    private readonly ThemedComboBox _algoCombo;
    private readonly Button _calcBtn;
    private readonly Button _closeBtn;
    private readonly Button _copyBtn;
    private readonly Label _statusLabel;
    private readonly List<string> _files;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChecksumForm"/> class for the specified files
    /// and starts calculation automatically on load.
    /// </summary>
    /// <param name="files">List of absolute file paths to compute checksums for.</param>
    public ChecksumForm(IReadOnlyList<string> files)
    {
        _files = files.ToList();

        var L = LocalizationService.Current;
        Text = L.GetString("Checksum.Title");
        ClientSize = new Size(700, 520);
        Resizable = true;
        MinimumSize = new Size(480, 360);

        var p = ThemeService.Current;

        // Top panel: files + algo
        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 2,
            Height = 200,
            BackColor = p.Background,
            Padding = new Padding(16, 12, 16, 12)
        };
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        _fileList = UiHelpers.CreateListView(
            (L.GetString("Checksum.FileName"), 400),
            (L.GetString("Checksum.FileSize"), 120));
        _fileList.Dock = DockStyle.Fill;
        foreach (var f in _files)
        {
            try
            {
                var fi = new FileInfo(f);
                var lvi = new ListViewItem(fi.Name) { Tag = f };
                lvi.SubItems.Add(UiHelpers.FormatSize(fi.Length));
                _fileList.Items.Add(lvi);
            }
            catch
            {
                var lvi = new ListViewItem(f) { Tag = f };
                lvi.SubItems.Add("—");
                _fileList.Items.Add(lvi);
            }
        }
        topPanel.Controls.Add(_fileList, 1, 0);

        topPanel.Controls.Add(UiHelpers.CreateLabel(L.GetString("Checksum.Algorithm")), 0, 1);

        // Protocol identifiers consumed by the switch in CalculateAsync() - must stay
        // unlocalised, unlike every other user-facing string in this dialog.
        _algoCombo = new ThemedComboBox { Width = 120, Dock = DockStyle.Left };
        _algoCombo.AddItems("MD5", "SHA1", "SHA256");
        _algoCombo.SelectedIndex = 2; // SHA256 default
        topPanel.Controls.Add(_algoCombo, 1, 1);

        // Results
        _resultList = UiHelpers.CreateListView(
            (L.GetString("Checksum.FileName"), 200),
            (L.GetString("Checksum.Algorithm"), 80),
            (L.GetString("Checksum.Hash"), 400));
        _resultList.Dock = DockStyle.Fill;

        // Bottom
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = p.DimForeground,
            Font = p.GridFont,
            TextAlign = ContentAlignment.MiddleLeft,
            Tag = ThemeRole.Muted
        };

        _closeBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Close"));
        _closeBtn.Margin = new Padding(0, 0, 8, 0);
        _closeBtn.Click += (_, _) => Close();

        _copyBtn = ThemedForm.CreateThemedButton(L.GetString("Checksum.CopyToClipboard"));
        _copyBtn.Margin = new Padding(0, 0, 8, 0);
        _copyBtn.Click += (_, _) => CopyHash();

        _calcBtn = ThemedForm.CreateThemedButton(L.GetString("Checksum.Calculate"), accent: true);
        _calcBtn.Margin = new Padding(0);
        _calcBtn.Click += (_, _) => _ = CalculateAsync();

        // Dock.Right ignores Margin entirely, which had collapsed all three gaps - a
        // right-aligned FlowLayoutPanel (add order = visual left-to-right order, matching the
        // original Close/Copy/Calc(accent, rightmost) layout) actually renders them.
        var rightGroup = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        rightGroup.Controls.Add(_closeBtn);
        rightGroup.Controls.Add(_copyBtn);
        rightGroup.Controls.Add(_calcBtn);

        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = p.HeaderBackground,
            Tag = ThemeRole.HeaderBackground,
            Padding = new Padding(16, 8, 16, 8)
        };
        bottomPanel.Controls.Add(_statusLabel);
        bottomPanel.Controls.Add(rightGroup);

        // Dock=Fill must be added before Dock=Bottom/Top/Left/Right siblings (see
        // WinForms/DirectoryTreeForm.cs for the full explanation) - _resultList used to be laid
        // out under bottomPanel's 50px (invisible only because bottomPanel is opaque and
        // added-first = frontmost z-order), which would have also thrown off a scrollbar overlay
        // positioned from its Bounds.
        Controls.Add(_resultList);
        Controls.Add(bottomPanel);
        Controls.Add(topPanel);

        CancelButton = _closeBtn;
        Load += (_, _) => _ = CalculateAsync();
    }

    private async Task CalculateAsync()
    {
        var L = LocalizationService.Current;
        _calcBtn.Enabled = false;
        _resultList.Items.Clear();
        _statusLabel.Text = L.GetString("Checksum.Calculating");

        try
        {
            var algoName = _algoCombo.SelectedItem?.ToString() ?? "SHA256";

            foreach (var file in _files)
            {
                try
                {
                    var hash = await Task.Run(() =>
                    {
                        using var stream = File.OpenRead(file);
                        // MD5/SHA1 here are user-selectable file-identity checksums (comparing/
                        // verifying file contents), never a security boundary - not password
                        // hashing, signing, or anything an attacker could exploit by finding a
                        // collision. CA5350/CA5351 assume every use of these algorithms is
                        // cryptographic; this one isn't, so the warning is suppressed rather than
                        // the user-facing algorithm choice removed.
#pragma warning disable CA5350, CA5351
                        using var algorithm = algoName switch
                        {
                            "MD5" => (HashAlgorithm)MD5.Create(),
                            "SHA1" => SHA1.Create(),
                            _ => SHA256.Create()
                        };
#pragma warning restore CA5350, CA5351
                        var hashBytes = algorithm.ComputeHash(stream);
                        return Convert.ToHexString(hashBytes).ToLowerInvariant();
                    });

                    if (IsDisposed || !IsHandleCreated) return;

                    var fi = new FileInfo(file);
                    var lvi = new ListViewItem(fi.Name) { Tag = file };
                    lvi.SubItems.Add(algoName);
                    lvi.SubItems.Add(hash);
                    _resultList.Items.Add(lvi);
                }
                catch (Exception ex)
                {
                    LogService.Warning($"Checksum failed: {file}: {ex.Message}");
                    if (IsDisposed || !IsHandleCreated) return;

                    var lvi = new ListViewItem(Path.GetFileName(file));
                    lvi.SubItems.Add(algoName);
                    lvi.SubItems.Add(ex.Message);
                    lvi.ForeColor = ThemeService.Current.Danger;
                    _resultList.Items.Add(lvi);
                }
            }

            if (!IsDisposed && IsHandleCreated)
                _statusLabel.Text = L.GetString("Checksum.Done", _resultList.Items.Count);
        }
        catch (Exception ex)
        {
            LogService.Error("Checksum calculation failed", ex);
            if (!IsDisposed && IsHandleCreated)
                _statusLabel.Text = ex.Message;
        }
        finally
        {
            if (!IsDisposed && IsHandleCreated)
                _calcBtn.Enabled = true;
        }
    }

    private void CopyHash()
    {
        if (_resultList.SelectedItems.Count == 0) return;
        var hash = _resultList.SelectedItems[0].SubItems[2].Text;
        if (!string.IsNullOrEmpty(hash))
        {
            Clipboard.SetText(hash);
            _statusLabel.Text = LocalizationService.Current.GetString("Checksum.Copied");
        }
    }
}
