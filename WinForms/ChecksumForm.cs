using CoderCommander.FileSystem;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Computes CRC32, MD5, SHA1, SHA256 for selected files via <see cref="ChecksumService"/>,
/// and exports results in <c>.sfv</c>/<c>.md5</c>/<c>.sha1</c>/<c>.sha256</c> formats.
///
/// <para><b>VFS-aware.</b> The form accepts an <see cref="IFileSystem"/> and <see cref="FileEntry"/>
/// list, so checksums work inside archives and remote connections, not only on local native
/// paths. The previous implementation used <c>File.OpenRead</c> directly and was blind to any
/// non-local filesystem.</para>
/// </summary>
public sealed partial class ChecksumForm : ThemedForm
{
    private readonly IFileSystem _fs;
    private readonly List<FileEntry> _files;
    private CancellationTokenSource? _cts;
    private string _currentAlgo = AlgoSha256;

    /// <summary>Protocol identifiers consumed by the switch in <see cref="CalculateAsync"/> — must
    /// stay unlocalised, unlike every other user-facing string in this dialog.</summary>
    private const string AlgoCrc32 = "CRC32";
    private const string AlgoMd5 = "MD5";
    private const string AlgoSha1 = "SHA1";
    private const string AlgoSha256 = "SHA256";

    /// <summary>
    /// Initializes a new instance of the <see cref="ChecksumForm"/> class for the specified files
    /// and starts calculation automatically on load.
    /// </summary>
    /// <param name="fs">Filesystem to read files from — may be local, archive, or remote.</param>
    /// <param name="files">Files to compute checksums for.</param>
    public ChecksumForm(IFileSystem fs, IReadOnlyList<FileEntry> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _fs = fs;
        _files = files.ToList();

        var L = LocalizationService.Current;
        // A ColumnHeader is not a Control and cannot carry a LocalizationKey.
        _colFileName.Text = L.GetString("Checksum.FileName");
        _colFileSize.Text = L.GetString("Checksum.FileSize");
        _colResultName.Text = L.GetString("Checksum.FileName");
        _colResultAlgo.Text = L.GetString("Checksum.Algorithm");
        _colResultHash.Text = L.GetString("Checksum.Hash");

        // Set here rather than in the designer: ThemedForm.Resizable is this app's own property,
        // applied in OnLoad rather than a real FormBorderStyle the designer could round-trip.
        Resizable = true;

        foreach (var f in _files)
        {
            var lvi = new ListViewItem(f.Name) { Tag = f.FullPath };
            lvi.SubItems.Add(UiHelpers.FormatSize(f.Size));
            _fileList.Items.Add(lvi);
        }

        // Protocol identifiers, deliberately not localized - CalculateAsync switches on them.
        _algoCombo.AddItems(AlgoCrc32, AlgoMd5, AlgoSha1, AlgoSha256);
        _algoCombo.SelectedIndex = 3; // SHA256 default

        _closeBtn.Click += (_, _) => Close();
        _copyBtn.Click += (_, _) => CopyHash();
        _exportBtn.Click += (_, _) => _ = ExportAsync();
        _calcBtn.Click += (_, _) => _ = CalculateAsync();

        Load += (_, _) => _ = CalculateAsync();
        FormClosing += (_, _) =>
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        };
    }

    private async Task CalculateAsync()
    {
        var L = LocalizationService.Current;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _calcBtn.Enabled = false;
        _exportBtn.Enabled = false;
        _resultList.Items.Clear();
        _statusLabel.Text = L.GetString("Checksum.Calculating");

        try
        {
            var algoName = _algoCombo.SelectedItem?.ToString() ?? AlgoSha256;
            _currentAlgo = algoName;

            foreach (var file in _files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var hash = algoName switch
                    {
                        AlgoCrc32 => await ChecksumService.ComputeCrc32Async(_fs, file.FullPath, ct).ConfigureAwait(true),
                        AlgoMd5 => await ChecksumService.ComputeMd5Async(_fs, file.FullPath, ct).ConfigureAwait(true),
                        AlgoSha1 => await ChecksumService.ComputeSha1Async(_fs, file.FullPath, ct).ConfigureAwait(true),
                        _ => await ChecksumService.ComputeSha256Async(_fs, file.FullPath, ct).ConfigureAwait(true)
                    };

                    if (IsDisposed || !IsHandleCreated || ct.IsCancellationRequested) return;

                    var lvi = new ListViewItem(file.Name) { Tag = file.FullPath };
                    lvi.SubItems.Add(algoName);
                    lvi.SubItems.Add(hash);
                    _resultList.Items.Add(lvi);
                }
                catch (Exception ex)
                {
                    LogService.Warning($"Checksum failed: {file.FullPath}: {ex.Message}");
                    if (IsDisposed || !IsHandleCreated || ct.IsCancellationRequested) return;

                    var lvi = new ListViewItem(file.Name);
                    lvi.SubItems.Add(algoName);
                    lvi.SubItems.Add(ex.Message);
                    lvi.ForeColor = ThemeService.Current.Danger;
                    _resultList.Items.Add(lvi);
                }
            }

            if (!IsDisposed && IsHandleCreated)
            {
                _statusLabel.Text = L.GetString("Checksum.Done", _resultList.Items.Count);
                _exportBtn.Enabled = _resultList.Items.Count > 0;
            }
        }
        catch (OperationCanceledException) { }
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
            ClipboardHelper.TrySetClipboard(hash);
            _statusLabel.Text = LocalizationService.Current.GetString("Checksum.Copied");
        }
    }

    private async Task ExportAsync()
    {
        var L = LocalizationService.Current;
        var algoName = _currentAlgo;

        var (filter, defaultExt) = algoName switch
        {
            AlgoCrc32 => ("SFV files (*.sfv)|*.sfv", ".sfv"),
            AlgoMd5 => ("MD5 files (*.md5)|*.md5", ".md5"),
            AlgoSha1 => ("SHA1 files (*.sha1)|*.sha1", ".sha1"),
            _ => ("SHA256 files (*.sha256)|*.sha256", ".sha256")
        };

        using var dlg = new SaveFileDialog
        {
            Filter = filter,
            DefaultExt = defaultExt,
            FileName = "checksums" + defaultExt
        };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var entries = new List<(string Name, string Hash)>();
        foreach (ListViewItem lvi in _resultList.Items)
        {
            var name = lvi.Text;
            var hash = lvi.SubItems[2].Text;
            // Skip error rows (hash contains an exception message, not a hex string).
            if (hash.Length > 0 && hash.Length <= 128 && IsHex(hash))
                entries.Add((name, hash));
        }

        if (entries.Count == 0)
        {
            _statusLabel.Text = L.GetString("Checksum.NothingToExport");
            return;
        }

        try
        {
            // SaveFileDialog returns a local Windows path — always write through LocalFileSystem,
            // not through the panel's IFileSystem (which may be remote/archive/MTP and can't
            // resolve a "C:\Users\..." path).
            var localFs = new FileSystem.LocalFileSystem();
            if (algoName == AlgoCrc32)
                await ChecksumService.ExportSfvAsync(localFs, dlg.FileName, entries).ConfigureAwait(true);
            else
                await ChecksumService.ExportHashAsync(localFs, dlg.FileName, algoName, entries).ConfigureAwait(true);

            if (!IsDisposed && IsHandleCreated)
                _statusLabel.Text = L.GetString("Checksum.ExportDone", dlg.FileName);
        }
        catch (Exception ex)
        {
            LogService.Warning($"Checksum export failed: {ex.Message}");
            if (!IsDisposed && IsHandleCreated)
                _statusLabel.Text = L.GetString("Checksum.ExportFailed", ex.Message);
        }
    }

    /// <summary>Checks whether <paramref name="s"/> is a non-empty lowercase-or-uppercase hex
    /// string — used to distinguish real hash rows from error-message rows in the results list.</summary>
    private static bool IsHex(string s)
    {
        foreach (var c in s)
        {
            if (!char.IsAsciiHexDigit(c)) return false;
        }
        return true;
    }

}
