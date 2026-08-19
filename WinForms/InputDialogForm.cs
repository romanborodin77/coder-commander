using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Simple text-input dialog with a prompt and default value.
/// </summary>
public sealed class InputDialogForm : ThemedForm
{
    private readonly TextBox _textBox;
    private readonly Button _okBtn;
    private readonly Button _cancelBtn;

    /// <summary>The value entered by the user.</summary>
    public string Value => _textBox.Text;

    /// <param name="title">Window title (localized).</param>
    /// <param name="prompt">Label text shown above the text box.</param>
    /// <param name="defaultValue">Pre-filled text.</param>
    public InputDialogForm(string title, string prompt, string defaultValue = "")
    {
        Text = title;
        ClientSize = new Size(420, 170);
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = ThemeService.Current.Background,
            Padding = new Padding(24, 20, 24, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var promptLabel = UiHelpers.CreateLabel(prompt);
        promptLabel.Dock = DockStyle.Fill;
        promptLabel.TextAlign = ContentAlignment.BottomLeft;

        _textBox = UiHelpers.CreateTextBox(defaultValue);
        _textBox.Dock = DockStyle.Fill;
        _textBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); e.Handled = true; }
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); e.Handled = true; }
        };

        _okBtn = ThemedForm.CreateThemedButton(LocalizationService.Current.GetString("Common.OK"), accent: true);
        _okBtn.DialogResult = DialogResult.OK;
        _okBtn.Width = 100;

        _cancelBtn = ThemedForm.CreateThemedButton(LocalizationService.Current.GetString("Common.Cancel"), accent: false);
        _cancelBtn.DialogResult = DialogResult.Cancel;
        _cancelBtn.Width = 100;

        var bottomPanel = CreateBottomPanel(_okBtn, _cancelBtn);

        layout.Controls.Add(promptLabel, 0, 0);
        layout.Controls.Add(_textBox, 0, 1);

        // Dock=Fill must be added before Dock=Bottom/Top/Left/Right siblings (see
        // WinForms/DirectoryTreeForm.cs for the full explanation).
        Controls.Add(layout);
        Controls.Add(bottomPanel);

        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _textBox?.Dispose();
            _okBtn?.Dispose();
            _cancelBtn?.Dispose();
        }
        base.Dispose(disposing);
    }
}
