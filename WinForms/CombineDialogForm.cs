using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Combine dialog: shows the part files that were discovered for display/confirmation (the
/// actual discovery - and the authoritative missing-part check - is done again by
/// <see cref="CoderCommander.Operations.CombineOperation"/> itself when it runs, so this list is
/// informational, not load-bearing), the output file name, whether to verify against a
/// <c>.crc</c> sidecar, and whether to delete the parts once combined.
/// </summary>
public sealed class CombineDialogForm : ThemedForm
{
    private readonly TextBox _outputNameBox;
    private readonly ThemedCheckBox _verifyCrcCheck;
    private readonly ThemedCheckBox _deleteSourceCheck;
    private readonly string _destDir;

    /// <summary>Full path of the reassembled file.</summary>
    public string DestPath
    {
        get
        {
            var name = _outputNameBox.Text.Trim();
            return Path.IsPathRooted(name) ? name : Path.Combine(_destDir, name);
        }
    }

    /// <summary>Whether to compare the result against a <c>.crc</c> sidecar, if one exists.</summary>
    public bool VerifyCrc => _verifyCrcCheck.Checked;

    /// <summary>Whether to delete the part files once the combine succeeds.</summary>
    public bool DeleteSource => _deleteSourceCheck.Checked;

    /// <param name="suggestedName">Output file name, typically the parts' base name.</param>
    /// <param name="destDir">Folder the combined file is written into.</param>
    /// <param name="discoveredParts">Part file names shown for the user's own confirmation.</param>
    public CombineDialogForm(string suggestedName, string destDir, IReadOnlyList<string> discoveredParts)
    {
        _destDir = destDir;
        var L = LocalizationService.Current;

        Text = L.GetString("Combine.Title");
        ClientSize = new Size(440, 340);
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = ThemeService.Current.Background,
            Padding = new Padding(24, 20, 24, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var nameLabel = UiHelpers.CreateLabel(L.GetString("Combine.OutputName"));
        nameLabel.Dock = DockStyle.Fill;
        nameLabel.TextAlign = ContentAlignment.BottomLeft;

        _outputNameBox = UiHelpers.CreateTextBox(suggestedName, name: "CombineOutputNameBox");
        _outputNameBox.Dock = DockStyle.Fill;

        var partsLabel = UiHelpers.CreateLabel(L.GetString("Combine.PartsFound"));
        partsLabel.Dock = DockStyle.Fill;
        partsLabel.TextAlign = ContentAlignment.BottomLeft;

        var partsList = new ListBox
        {
            Dock = DockStyle.Fill,
            Name = "CombinePartsList",
            BackColor = ThemeService.Current.PanelBackground,
            ForeColor = ThemeService.Current.Foreground,
            BorderStyle = BorderStyle.FixedSingle,
            Font = ThemeService.Current.GridFont
        };
        foreach (var part in discoveredParts)
            partsList.Items.Add(part);

        var checksLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        _verifyCrcCheck = UiHelpers.CreateCheckBox(L.GetString("Combine.VerifyCrc"), true, name: "CombineVerifyCrcCheck");
        _verifyCrcCheck.AutoSize = true;
        _deleteSourceCheck = UiHelpers.CreateCheckBox(L.GetString("Combine.DeleteParts"), false, name: "CombineDeletePartsCheck");
        _deleteSourceCheck.AutoSize = true;
        checksLayout.Controls.Add(_verifyCrcCheck);
        checksLayout.Controls.Add(_deleteSourceCheck);

        var okBtn = CreateThemedButton(L.GetString("Common.OK"), accent: true, name: "CombineOkButton");
        okBtn.DialogResult = DialogResult.OK;
        okBtn.Width = 100;

        var cancelBtn = CreateThemedButton(L.GetString("Common.Cancel"), accent: false, name: "CombineCancelButton");
        cancelBtn.DialogResult = DialogResult.Cancel;
        cancelBtn.Width = 100;

        var bottomPanel = CreateBottomPanel(okBtn, cancelBtn);

        layout.Controls.Add(nameLabel, 0, 0);
        layout.Controls.Add(_outputNameBox, 0, 1);
        layout.Controls.Add(partsLabel, 0, 2);
        layout.Controls.Add(partsList, 0, 3);
        layout.Controls.Add(checksLayout, 0, 4);

        // Dock=Fill must be added before Dock=Bottom siblings (see DirectoryTreeForm.cs).
        Controls.Add(layout);
        Controls.Add(bottomPanel);

        AcceptButton = okBtn;
        CancelButton = cancelBtn;
    }
}
