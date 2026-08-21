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
public sealed class ChecksumVerifyForm : ThemedForm
{
    private readonly ListView _resultList;
    private readonly Label _statusLabel;
    private readonly Button _closeBtn;
    private readonly IFileSystem _fs;
    private readonly string _checksumFilePath;
    private CancellationTokenSource? _cts;

    public ChecksumVerifyForm(IFileSystem fs, string checksumFilePath)
    {
        _fs = fs;
        _checksumFilePath = checksumFilePath;

        var L = LocalizationService.Current;
        Text = string.Format(CultureInfo.InvariantCulture,
            L.GetString("ChecksumVerify.Title"), VfsPath.GetName(checksumFilePath));
        ClientSize = new Size(640, 480);
        Resizable = true;
        MinimumSize = new Size(420, 300);

        var p = ThemeService.Current;

        _resultList = UiHelpers.CreateListView(
            (L.GetString("Checksum.FileName"), 320),
            (L.GetString("ChecksumVerify.Status"), 140),
            (L.GetString("Checksum.Hash"), 160));
        _resultList.Dock = DockStyle.Fill;

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = p.DimForeground,
            Font = p.GridFont,
            TextAlign = ContentAlignment.MiddleLeft,
            Tag = ThemeRole.Muted
        };

        _closeBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Close"));
        _closeBtn.Margin = new Padding(0);
        _closeBtn.Click += (_, _) => Close();

        var rightGroup = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        rightGroup.Controls.Add(_closeBtn);

        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = p.HeaderBackground,
            Tag = ThemeRole.HeaderBackground,
            Padding = new Padding(16, 8, 16, 8)
        };
        bottomPanel.Controls.Add(_statusLabel);
        bottomPanel.Controls.Add(rightGroup);

        // Dock=Fill must be added before Dock=Bottom/Top/Left/Right siblings (see
        // WinForms/DirectoryTreeForm.cs for the full explanation).
        Controls.Add(_resultList);
        Controls.Add(bottomPanel);

        CancelButton = _closeBtn;
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
        var p = ThemeService.Current;
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _resultList?.Dispose();
            _statusLabel?.Dispose();
            _closeBtn?.Dispose();
            _cts?.Dispose();
        }
        base.Dispose(disposing);
    }
}
