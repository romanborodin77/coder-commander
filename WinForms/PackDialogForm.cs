using CoderCommander.Archives;
using CoderCommander.FileSystem;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Pack dialog: archive name, target format (from <see cref="ArchiveFormatRegistry.Creatable"/>),
/// compression preset for the selected format, and whether to delete the originals afterwards
/// (move semantics - already supported by <see cref="CoderCommander.Operations.PackOperation"/>,
/// just never exposed in the UI before). Replaces the bare <see cref="InputDialogForm"/> the Pack
/// command used to show.
/// </summary>
public sealed partial class PackDialogForm : ThemedForm
{
    private readonly List<IArchiveFormat> _formats;
    private readonly string _destDir;

    /// <summary>The <see cref="IArchiveFormat"/> selected in the format combo.</summary>
    public IArchiveFormat SelectedFormat => _formats[Math.Max(0, _formatCombo.SelectedIndex)];

    /// <summary>The compression preset selected for <see cref="SelectedFormat"/>.</summary>
    public ArchiveCompressionSpec SelectedCompression
    {
        get
        {
            var presets = SelectedFormat.SupportedPresets;
            var index = Math.Clamp(_compressionCombo.SelectedIndex, 0, Math.Max(0, presets.Count - 1));
            return new ArchiveCompressionSpec(presets.Count > 0 ? presets[index] : CompressionPreset.Balanced);
        }
    }

    /// <summary>Whether to delete source files after archiving (move semantics).</summary>
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
            return Path.IsPathRooted(fileName) ? fileName : VfsPath.Combine(_destDir, fileName);
        }
    }

    /// <param name="suggestedBaseName">Pre-filled name, without any extension.</param>
    /// <param name="destDir">Folder the new archive is created in.</param>
    /// <param name="defaultFormatId">Format id to preselect (see <c>AppSettings.DefaultArchiveFormat</c>).</param>
    /// <param name="deleteOriginalsDefault">Initial checked state of the "delete originals"
    /// checkbox (see <c>AppSettings.DeleteOriginalsAfterPack</c>).</param>
    public PackDialogForm(string suggestedBaseName, string destDir, string defaultFormatId, bool deleteOriginalsDefault = false)
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _destDir = destDir;
        var L = LocalizationService.Current;

        _formats = ArchiveFormatRegistry.Creatable.ToList();
        if (_formats.Count == 0)
            _formats.Add(Archives.Zip.ZipArchiveFormat.Instance);

        _nameBox.Text = suggestedBaseName;
        _moveCheck.Checked = deleteOriginalsDefault;

        foreach (var format in _formats)
            _formatCombo.AddItem(L.GetString(format.DisplayNameKey));

        var defaultIndex = _formats.FindIndex(f => string.Equals(f.Id, defaultFormatId, StringComparison.OrdinalIgnoreCase));
        _formatCombo.SelectedIndex = defaultIndex >= 0 ? defaultIndex : 0;
        PopulateCompressionCombo();

        _formatCombo.SelectedIndexChanged += (_, _) => PopulateCompressionCombo();

        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing && DialogResult == DialogResult.OK &&
                string.IsNullOrWhiteSpace(_nameBox.Text))
            {
                StyledMessageBox.Show(LocalizationService.Current.GetString("Archive.PackNameRequired"),
                    LocalizationService.Current.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Warning, this);
                e.Cancel = true;
            }
        };
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
