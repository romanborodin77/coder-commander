using CoderCommander.Archives;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Pack dialog: archive name, target format (from <see cref="ArchiveFormatRegistry.Creatable"/>),
/// compression preset for the selected format, and whether to delete the originals afterwards
/// (move semantics - already supported by <see cref="CoderCommander.Operations.PackOperation"/>,
/// just never exposed in the UI before). Replaces the bare <see cref="InputDialogForm"/> the Pack
/// command used to show.
/// </summary>
public sealed class PackDialogForm : ThemedForm
{
    private readonly TextBox _nameBox;
    private readonly ThemedComboBox _formatCombo;
    private readonly ThemedComboBox _compressionCombo;
    private readonly ThemedCheckBox _moveCheck;
    private readonly List<IArchiveFormat> _formats;
    private readonly string _destDir;

    public IArchiveFormat SelectedFormat => _formats[Math.Max(0, _formatCombo.SelectedIndex)];

    public ArchiveCompressionSpec SelectedCompression
    {
        get
        {
            var presets = SelectedFormat.SupportedPresets;
            var index = Math.Clamp(_compressionCombo.SelectedIndex, 0, Math.Max(0, presets.Count - 1));
            return new ArchiveCompressionSpec(presets.Count > 0 ? presets[index] : CompressionPreset.Balanced);
        }
    }

    public bool MoveOriginals => _moveCheck.Checked;

    /// <summary>Destination combined with the typed name; the selected format's default extension
    /// is appended unless the typed name already ends with one of that format's own extensions.</summary>
    public string ArchivePath
    {
        get
        {
            var name = _nameBox.Text.Trim();
            var format = SelectedFormat;
            var hasRecognizedExtension = format.Extensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
            var fileName = hasRecognizedExtension ? name : name + format.DefaultExtension;
            return Path.IsPathRooted(fileName) ? fileName : Path.Combine(_destDir, fileName);
        }
    }

    /// <param name="suggestedBaseName">Pre-filled name, without any extension.</param>
    /// <param name="destDir">Folder the new archive is created in.</param>
    /// <param name="defaultFormatId">Format id to preselect (see <c>AppSettings.DefaultArchiveFormat</c>).</param>
    public PackDialogForm(string suggestedBaseName, string destDir, string defaultFormatId)
    {
        _destDir = destDir;
        var L = LocalizationService.Current;

        Text = L.GetString("Archive.PackTitle");
        ClientSize = new Size(440, 300);
        MaximizeBox = false;
        MinimizeBox = false;

        _formats = ArchiveFormatRegistry.Creatable.ToList();
        if (_formats.Count == 0)
            _formats.Add(Archives.Zip.ZipArchiveFormat.Instance);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = ThemeService.Current.Background,
            Padding = new Padding(24, 20, 24, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var nameLabel = UiHelpers.CreateLabel(L.GetString("Archive.PackPrompt"));
        nameLabel.Dock = DockStyle.Fill;
        nameLabel.TextAlign = ContentAlignment.BottomLeft;

        _nameBox = UiHelpers.CreateTextBox(suggestedBaseName);
        _nameBox.Dock = DockStyle.Fill;

        var formatLabel = UiHelpers.CreateLabel(L.GetString("Archive.PackFormat"));
        formatLabel.Dock = DockStyle.Fill;
        formatLabel.TextAlign = ContentAlignment.BottomLeft;

        _formatCombo = new ThemedComboBox { Dock = DockStyle.Fill };
        foreach (var format in _formats)
            _formatCombo.AddItem(L.GetString(format.DisplayNameKey));

        var compressionLabel = UiHelpers.CreateLabel(L.GetString("Archive.PackCompression"));
        compressionLabel.Dock = DockStyle.Fill;
        compressionLabel.TextAlign = ContentAlignment.BottomLeft;

        _compressionCombo = new ThemedComboBox { Dock = DockStyle.Fill };

        var defaultIndex = _formats.FindIndex(f => string.Equals(f.Id, defaultFormatId, StringComparison.OrdinalIgnoreCase));
        _formatCombo.SelectedIndex = defaultIndex >= 0 ? defaultIndex : 0;
        PopulateCompressionCombo();

        _formatCombo.SelectedIndexChanged += (_, _) => PopulateCompressionCombo();

        _moveCheck = UiHelpers.CreateCheckBox(L.GetString("Archive.PackMoveOriginals"), false);
        _moveCheck.AutoSize = true;
        _moveCheck.Dock = DockStyle.Left;

        var okBtn = CreateThemedButton(L.GetString("Common.OK"), accent: true);
        okBtn.DialogResult = DialogResult.OK;
        okBtn.Width = 100;

        var cancelBtn = CreateThemedButton(L.GetString("Common.Cancel"), accent: false);
        cancelBtn.DialogResult = DialogResult.Cancel;
        cancelBtn.Width = 100;

        var bottomPanel = CreateBottomPanel(okBtn, cancelBtn);

        layout.Controls.Add(nameLabel, 0, 0);
        layout.Controls.Add(_nameBox, 0, 1);
        layout.Controls.Add(formatLabel, 0, 2);
        layout.Controls.Add(_formatCombo, 0, 3);
        layout.Controls.Add(compressionLabel, 0, 4);
        layout.Controls.Add(_compressionCombo, 0, 5);
        layout.Controls.Add(_moveCheck, 0, 6);

        // Dock=Fill must be added before Dock=Bottom/Top/Left/Right siblings (see
        // WinForms/DirectoryTreeForm.cs for the full explanation).
        Controls.Add(layout);
        Controls.Add(bottomPanel);

        AcceptButton = okBtn;
        CancelButton = cancelBtn;
    }

    private void PopulateCompressionCombo()
    {
        var L = LocalizationService.Current;
        var format = SelectedFormat;
        var presets = format.SupportedPresets;

        _compressionCombo.ClearItems();
        foreach (var preset in presets)
            _compressionCombo.AddItem(L.GetString($"Archive.Compression.{preset}"));

        _compressionCombo.SelectedIndex = presets.Count > 0 ? DefaultPresetIndex(format, presets) : 0;
    }

    /// <summary>Index of the saved default preset for <paramref name="format"/> (see
    /// <c>AppSettings.ArchiveCompression</c>), falling back to Balanced (or the first available
    /// preset if Balanced isn't one of them) when nothing is saved yet.</summary>
    private static int DefaultPresetIndex(IArchiveFormat format, IReadOnlyList<CompressionPreset> presets)
    {
        var settings = SettingsService.Load();
        if (settings.ArchiveCompression.TryGetValue(format.Id, out var presetName) &&
            Enum.TryParse<CompressionPreset>(presetName, out var saved))
        {
            var savedIndex = IndexOf(presets, saved);
            if (savedIndex >= 0) return savedIndex;
        }

        var balancedIndex = IndexOf(presets, CompressionPreset.Balanced);
        return balancedIndex >= 0 ? balancedIndex : 0;
    }

    private static int IndexOf(IReadOnlyList<CompressionPreset> presets, CompressionPreset value)
    {
        for (var i = 0; i < presets.Count; i++)
        {
            if (presets[i] == value) return i;
        }
        return -1;
    }
}
