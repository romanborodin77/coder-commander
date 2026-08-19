using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Split dialog: destination folder, part size (a preset list plus a free-typed custom size in
/// MB), whether to write a <c>.crc</c> sidecar, and whether to delete the source file(s) once
/// split. Mirrors <see cref="PackDialogForm"/>'s shape - fixed layout built in the constructor,
/// results exposed as read-only properties the caller reads after <c>ShowDialog</c> returns OK.
/// </summary>
public sealed class SplitDialogForm : ThemedForm
{
    private readonly TextBox _destDirBox;
    private readonly ThemedComboBox _presetCombo;
    private readonly TextBox _customSizeBox;
    private readonly ThemedCheckBox _writeCrcCheck;
    private readonly ThemedCheckBox _deleteSourceCheck;

    /// <summary>Preset sizes in bytes, in the same order as <see cref="_presetCombo"/>'s items.
    /// The last entry (0) is the sentinel for "use <see cref="_customSizeBox"/> instead".</summary>
    private static readonly long[] PresetSizes =
    {
        1_474_560,           // 1.44 MB floppy
        100L * 1024 * 1024,  // 100 MB
        650L * 1024 * 1024,  // 650 MB CD
        700L * 1024 * 1024,  // 700 MB CD
        4_700_000_000,       // 4.7 GB DVD
        0                    // custom
    };

    /// <summary>Destination folder for the parts.</summary>
    public string DestDir => _destDirBox.Text.Trim();

    /// <summary>Chosen part size in bytes - either a preset or the parsed custom value (MB, rounded up to whole bytes).</summary>
    public long PartSizeBytes
    {
        get
        {
            var index = Math.Max(0, _presetCombo.SelectedIndex);
            var preset = PresetSizes[Math.Min(index, PresetSizes.Length - 1)];
            if (preset > 0) return preset;

            return double.TryParse(_customSizeBox.Text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var mb) && mb > 0
                ? (long)Math.Ceiling(mb * 1024 * 1024)
                : 0;
        }
    }

    /// <summary>Whether a <c>.crc</c> sidecar should be written next to the parts.</summary>
    public bool WriteCrc => _writeCrcCheck.Checked;

    /// <summary>Whether the source file should be deleted once every part is written.</summary>
    public bool DeleteSource => _deleteSourceCheck.Checked;

    /// <param name="destDir">Suggested destination folder (typically the source file's own folder).</param>
    /// <param name="defaultPartSizeBytes">Preselected part size (see <c>AppSettings.DefaultSplitPartSizeBytes</c>);
    /// 0 (or anything not matching a preset exactly) falls back to the 100 MB preset.</param>
    /// <param name="writeCrcDefault">Initial checked state of "create .crc" (<c>AppSettings.SplitWriteCrcDefault</c>).</param>
    /// <param name="deleteSourceDefault">Initial checked state of "delete source after splitting"
    /// (<c>AppSettings.DeleteOriginalsAfterSplit</c>).</param>
    public SplitDialogForm(string destDir, long defaultPartSizeBytes = 0, bool writeCrcDefault = true, bool deleteSourceDefault = false)
    {
        var L = LocalizationService.Current;

        Text = L.GetString("Split.Title");
        ClientSize = new Size(440, 330);
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 9,
            BackColor = ThemeService.Current.Background,
            Padding = new Padding(24, 20, 24, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 8; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, i % 2 == 0 ? 22 : 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var destLabel = UiHelpers.CreateLabel(L.GetString("Split.DestDir"));
        destLabel.Dock = DockStyle.Fill;
        destLabel.TextAlign = ContentAlignment.BottomLeft;

        _destDirBox = UiHelpers.CreateTextBox(destDir, name: "SplitDestDirBox");
        _destDirBox.Dock = DockStyle.Fill;

        var presetLabel = UiHelpers.CreateLabel(L.GetString("Split.PartSize"));
        presetLabel.Dock = DockStyle.Fill;
        presetLabel.TextAlign = ContentAlignment.BottomLeft;

        _presetCombo = new ThemedComboBox { Dock = DockStyle.Fill, Name = "SplitPresetCombo" };
        _presetCombo.AddItems(
            L.GetString("Split.Preset.Floppy"),
            L.GetString("Split.Preset.100Mb"),
            L.GetString("Split.Preset.Cd650"),
            L.GetString("Split.Preset.Cd700"),
            L.GetString("Split.Preset.Dvd"),
            L.GetString("Split.Preset.Custom"));
        var presetIndex = Array.IndexOf(PresetSizes, defaultPartSizeBytes);
        _presetCombo.SelectedIndex = presetIndex >= 0 && presetIndex < PresetSizes.Length - 1
            ? presetIndex
            : 1; // 100 MB - a sane default for most files, and for anything not matching a preset exactly.

        var customLabel = UiHelpers.CreateLabel(L.GetString("Split.CustomSizeMb"));
        customLabel.Dock = DockStyle.Fill;
        customLabel.TextAlign = ContentAlignment.BottomLeft;

        _customSizeBox = UiHelpers.CreateTextBox("10", name: "SplitCustomSizeBox");
        _customSizeBox.Dock = DockStyle.Fill;
        _customSizeBox.Enabled = _presetCombo.SelectedIndex == PresetSizes.Length - 1;
        _presetCombo.SelectedIndexChanged += (_, _) =>
            _customSizeBox.Enabled = _presetCombo.SelectedIndex == PresetSizes.Length - 1;

        _writeCrcCheck = UiHelpers.CreateCheckBox(L.GetString("Split.WriteCrc"), writeCrcDefault, name: "SplitWriteCrcCheck");
        _writeCrcCheck.AutoSize = true;
        _writeCrcCheck.Dock = DockStyle.Left;

        _deleteSourceCheck = UiHelpers.CreateCheckBox(L.GetString("Split.DeleteSource"), deleteSourceDefault, name: "SplitDeleteSourceCheck");
        _deleteSourceCheck.AutoSize = true;
        _deleteSourceCheck.Dock = DockStyle.Left;

        var okBtn = CreateThemedButton(L.GetString("Common.OK"), accent: true, name: "SplitOkButton");
        okBtn.DialogResult = DialogResult.OK;
        okBtn.Width = 100;

        var cancelBtn = CreateThemedButton(L.GetString("Common.Cancel"), accent: false, name: "SplitCancelButton");
        cancelBtn.DialogResult = DialogResult.Cancel;
        cancelBtn.Width = 100;

        var bottomPanel = CreateBottomPanel(okBtn, cancelBtn);

        layout.Controls.Add(destLabel, 0, 0);
        layout.Controls.Add(_destDirBox, 0, 1);
        layout.Controls.Add(presetLabel, 0, 2);
        layout.Controls.Add(_presetCombo, 0, 3);
        layout.Controls.Add(customLabel, 0, 4);
        layout.Controls.Add(_customSizeBox, 0, 5);
        layout.Controls.Add(_writeCrcCheck, 0, 6);
        layout.Controls.Add(_deleteSourceCheck, 0, 7);

        // Dock=Fill must be added before Dock=Bottom siblings (see DirectoryTreeForm.cs).
        Controls.Add(layout);
        Controls.Add(bottomPanel);

        AcceptButton = okBtn;
        CancelButton = cancelBtn;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _presetCombo?.Dispose();
            _customSizeBox?.Dispose();
            _deleteSourceCheck?.Dispose();
            _writeCrcCheck?.Dispose();
            _destDirBox?.Dispose();
        }
        base.Dispose(disposing);
    }
}
