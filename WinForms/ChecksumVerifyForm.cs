using System.Globalization;
using CoderCommander.FileSystem;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Reads a <c>.sfv</c>/<c>.md5</c>/<c>.sha1</c>/<c>.sha256</c> checksum file and verifies every
/// entry it lists against the real file contents, via <see cref="ChecksumService.VerifyAsync"/>.
///
/// <para><b>Was the missing half of <see cref="ChecksumForm"/>.</b> That dialog could compute and
/// export checksums but never read one back to check anything against it - the export format was
/// fully specified (<see cref="ChecksumService.ExportSfvAsync"/>/<see cref="ChecksumService.ExportHashAsync"/>)
/// with no corresponding import/compare path anywhere in the app.</para>
/// </summary>
public sealed partial class ChecksumVerifyForm : ThemedForm
{
    private readonly IFileSystem _fs;
    private readonly string _checksumFilePath;
    private CancellationTokenSource? _cts;

    public ChecksumVerifyForm(IFileSystem fs, string checksumFilePath)
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _fs = fs;
        _checksumFilePath = checksumFilePath;

        var L = LocalizationService.Current;
        // Interpolates the checksum file's name, so it cannot travel as a plain LocalizationKey.
        Text = string.Format(CultureInfo.InvariantCulture,
            L.GetString("ChecksumVerify.Title"), VfsPath.GetName(checksumFilePath));

        // A ColumnHeader is not a Control, so it cannot carry a LocalizationKey - the same reason
        // FilePanelUserControl.Relocalize rewrites its own column captions by hand.
        _colFileName.Text = L.GetString("Checksum.FileName");
        _colStatus.Text = L.GetString("ChecksumVerify.Status");
        _colHash.Text = L.GetString("Checksum.Hash");

        // Set here rather than in the designer: ThemedForm.Resizable is this app's own property,
        // applied in OnLoad rather than a real FormBorderStyle the designer could round-trip.
        Resizable = true;

        _closeBtn.Click += (_, _) => Close();
        Load += (_, _) => _ = VerifyAsync();
        FormClosing += (_, _) =>
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        };
    }

    private async Task VerifyAsync()
    {
        var L = LocalizationService.Current;
        var p = DesignerSafeThemeService.Current;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _resultList.Items.Clear();
        _statusLabel.Text = L.GetString("ChecksumVerify.Verifying");

        // Reported incrementally (one row per entry as verification proceeds) rather than waiting
        // for the whole list - a checksum file listing thousands of entries would otherwise leave
        // the dialog looking frozen for the entire run.
        var progress = new Progress<ChecksumService.ChecksumVerifyResult>(r =>
        {
            if (IsDisposed || !IsHandleCreated) return;

            var lvi = new ListViewItem(r.Name);
            string statusText;
            Color color;
            if (r.Missing)
            {
                statusText = L.GetString("ChecksumVerify.Missing");
                color = p.Warning;
            }
            else if (r.Error != null)
            {
                statusText = r.Error;
                color = p.Danger;
            }
            else if (r.Matched)
            {
                statusText = L.GetString("ChecksumVerify.Ok");
                color = p.GitAddedColor;
            }
            else
            {
                statusText = L.GetString("ChecksumVerify.Mismatch");
                color = p.Danger;
            }
            lvi.SubItems.Add(statusText);
            lvi.SubItems.Add(r.Actual ?? "");
            lvi.ForeColor = color;
            _resultList.Items.Add(lvi);
        });

        try
        {
            var results = await ChecksumService.VerifyAsync(_fs, _checksumFilePath, progress, ct).ConfigureAwait(true);
            if (IsDisposed || !IsHandleCreated) return;

            if (results.Count == 0)
            {
                _statusLabel.Text = L.GetString("ChecksumVerify.NoEntries");
                return;
            }

            var ok = results.Count(r => r.Matched);
            var missing = results.Count(r => r.Missing);
            var bad = results.Count - ok - missing;
            _statusLabel.Text = bad + missing == 0
                ? L.GetString("ChecksumVerify.AllOk", ok)
                : string.Format(CultureInfo.InvariantCulture,
                    L.GetString("ChecksumVerify.Summary"), ok, results.Count, bad, missing);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogService.Error($"Checksum verify failed: {_checksumFilePath}", ex);
            if (!IsDisposed && IsHandleCreated)
                _statusLabel.Text = ex.Message;
        }
    }

}
