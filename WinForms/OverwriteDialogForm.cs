using CoderCommander.Operations;

namespace CoderCommander.WinForms;

/// <summary>
/// File overwrite confirmation dialog. Displays source vs. destination info
/// and offers six overwrite policies (Overwrite, Skip, Rename, OverwriteAll,
/// SkipAll, OverwriteOlder).
///
/// <para>Layout lives in <c>OverwriteDialogForm.Designer.cs</c> and is editable in Visual Studio.</para>
/// </summary>
public sealed partial class OverwriteDialogForm : ThemedForm
{
    /// <summary>The selected <see cref="Operations.OverwriteAction"/> value after the dialog closes.</summary>
    public int Result { get; private set; } = 2;

    /// <param name="fileName">Name of the conflicting file.</param>
    /// <param name="sourceInfo">Size and timestamp of the source file.</param>
    /// <param name="destInfo">Size and timestamp of the existing destination file.</param>
    public OverwriteDialogForm(string fileName, string sourceInfo, string destInfo)
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _fileLabel.Text = fileName;
        _sourceValue.Text = sourceInfo;
        _destValue.Text = destInfo;

        WirePolicy(_overwriteBtn, OverwriteAction.Overwrite);
        WirePolicy(_skipBtn, OverwriteAction.Skip);
        WirePolicy(_renameBtn, OverwriteAction.Rename);
        WirePolicy(_overwriteAllBtn, OverwriteAction.OverwriteAll);
        WirePolicy(_skipAllBtn, OverwriteAction.SkipAll);
        WirePolicy(_overwriteOlderBtn, OverwriteAction.OverwriteOlder);
    }

    /// <summary>Closes the dialog reporting <paramref name="action"/> as the chosen policy.</summary>
    private void WirePolicy(RoundedButton button, OverwriteAction action)
    {
        button.Click += (_, _) =>
        {
            Result = (int)action;
            DialogResult = DialogResult.OK;
            Close();
        };
    }
}
