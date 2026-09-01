using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Split dialog: destination folder, part size (a preset list plus a free-typed custom size in
/// MB), whether to write a <c>.crc</c> sidecar, and whether to delete the source file(s) once
/// split. Mirrors <see cref="PackDialogForm"/>'s shape - results exposed as read-only properties
/// the caller reads after <c>ShowDialog</c> returns OK.
///
/// <para>Layout lives in <c>SplitDialogForm.Designer.cs</c> and is editable in Visual Studio.</para>
/// </summary>
public sealed partial class SplitDialogForm : ThemedForm
{
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
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _destDirBox.Text = destDir;
        _writeCrcCheck.Checked = writeCrcDefault;
        _deleteSourceCheck.Checked = deleteSourceDefault;

        PopulatePresets(defaultPartSizeBytes);

        _customSizeBox.Enabled = IsCustomSelected;
        _presetCombo.SelectedIndexChanged += (_, _) => _customSizeBox.Enabled = IsCustomSelected;
    }

    private bool IsCustomSelected => _presetCombo.SelectedIndex == PresetSizes.Length - 1;

    private void PopulatePresets(long defaultPartSizeBytes)
    {
        var L = LocalizationService.Current;
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
    }
}
