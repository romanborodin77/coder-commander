using CoderCommander.FileSystem;

namespace CoderCommander.WinForms;

/// <summary>
/// Combine dialog: shows the part files that were discovered for display/confirmation (the
/// actual discovery - and the authoritative missing-part check - is done again by
/// <see cref="CoderCommander.Operations.CombineOperation"/> itself when it runs, so this list is
/// informational, not load-bearing), the output file name, whether to verify against a
/// <c>.crc</c> sidecar, and whether to delete the parts once combined.
///
/// <para>Layout lives in <c>CombineDialogForm.Designer.cs</c> and is editable in Visual Studio.</para>
/// </summary>
public sealed partial class CombineDialogForm : ThemedForm
{
    private readonly string _destDir;

    /// <summary>Full path of the reassembled file.</summary>
    public string DestPath
    {
        get
        {
            var name = _outputNameBox.Text.Trim();
            return Path.IsPathRooted(name) ? name : VfsPath.Combine(_destDir, name);
        }
    }

    /// <summary>Whether to compare the result against a <c>.crc</c> sidecar, if one exists.</summary>
    public bool VerifyCrc => _verifyCrcCheck.Checked;

    /// <summary>Whether to delete the part files once the combine succeeds.</summary>
    public bool DeleteSource => _deleteSourceCheck.Checked;

    /// <param name="suggestedName">Output file name, typically the parts' base name.</param>
    /// <param name="destDir">Folder the combined file is written into.</param>
    /// <param name="discoveredParts">Part file names shown for the user's own confirmation.</param>
    /// <param name="verifyCrcDefault">Initial checked state of "verify against .crc"
    /// (<c>AppSettings.VerifyCrcAfterCombine</c>).</param>
    /// <param name="deleteSourceDefault">Initial checked state of "delete parts after combining"
    /// (<c>AppSettings.DeleteOriginalsAfterCombine</c>).</param>
    public CombineDialogForm(string suggestedName, string destDir, IReadOnlyList<string> discoveredParts,
        bool verifyCrcDefault = true, bool deleteSourceDefault = false)
    {
        ArgumentNullException.ThrowIfNull(discoveredParts);

        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _destDir = destDir;
        _outputNameBox.Text = suggestedName;
        _verifyCrcCheck.Checked = verifyCrcDefault;
        _deleteSourceCheck.Checked = deleteSourceDefault;

        foreach (var part in discoveredParts)
            _partsList.Items.Add(part);
    }
}
