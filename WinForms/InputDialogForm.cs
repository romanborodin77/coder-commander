namespace CoderCommander.WinForms;

/// <summary>
/// Simple text-input dialog with a prompt and default value.
///
/// <para><b>First dialog migrated to the Windows Forms Designer.</b> Layout lives in
/// <c>InputDialogForm.Designer.cs</c> and is editable by dragging in Visual Studio; this file holds
/// only behaviour. Colours come from <see cref="Services.ThemeRole"/> tags read by
/// <see cref="ControlThemer"/>, and button text from <c>lang/*.lng</c> via
/// <see cref="UiMetadataProvider.ApplyLocalization"/> - neither is baked into the generated code, so
/// theme switching and localization keep working exactly as before the migration.</para>
/// </summary>
public sealed partial class InputDialogForm : ThemedForm
{
    /// <summary>The value entered by the user.</summary>
    public string Value => _textBox.Text;

    /// <param name="title">Window title (localized by the caller).</param>
    /// <param name="prompt">Label text shown above the text box (localized by the caller).</param>
    /// <param name="defaultValue">Pre-filled text.</param>
    public InputDialogForm(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        Text = title;
        _promptLabel.Text = prompt;
        _textBox.Text = defaultValue;
        _textBox.KeyDown += OnTextBoxKeyDown;
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); e.Handled = true; }
        if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); e.Handled = true; }
    }
}
