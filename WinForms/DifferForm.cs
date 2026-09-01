using CoderCommander.FileSystem;
using CoderCommander.Services;
using CoderCommander.Utils;
using System.Globalization;

namespace CoderCommander.WinForms;

/// <summary>
/// Shows two files side-by-side with line-by-line highlighting of differences.
/// </summary>
public sealed partial class DifferForm : ThemedForm
{
    /// <summary>Above this (per file), reading the whole file into memory to diff it line-by-line
    /// is large enough to freeze the UI thread for seconds or throw
    /// <see cref="OutOfMemoryException"/> comparing two multi-GB files. Same threshold
    /// <see cref="ViewerForm"/> uses for its own text mode.</summary>
    private const long LargeFileConfirmBytes = 16 * 1024 * 1024;


    // Each side starts on the file system the panel selection came from (so a file inside an
    // archive or on an SFTP/FTP/WebDAV connection can actually be diffed), but Browse... only
    // knows how to pick a real local file - picking one switches that side to LocalFileSystem
    // independently of the other side, which is why these are two separate mutable fields rather
    // than one shared "current file system" for the whole dialog.
    private IFileSystem _leftFs;
    private IFileSystem _rightFs;

    private CancellationTokenSource? _compareCts;

    /// <summary>
    /// Initializes a new instance of the <see cref="DifferForm"/> class, optionally pre-filling both file paths.
    /// </summary>
    /// <param name="leftPath">Path to the left-side file, or <c>null</c> for an empty field.</param>
    /// <param name="rightPath">Path to the right-side file, or <c>null</c> for an empty field.</param>
    /// <param name="fileSystem">File system both initial paths live on - normally the active
    /// panel's <c>CurrentFileSystem</c>, since both selected files come from the same panel.</param>
    public DifferForm(string? leftPath, string? rightPath, IFileSystem fileSystem)
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _leftFs = fileSystem;
        _rightFs = fileSystem;

        // Set here rather than in the designer: ThemedForm.Resizable is this app's own property,
        // applied in OnLoad rather than a real FormBorderStyle the designer could round-trip.
        Resizable = true;

        _leftPathBox.Text = leftPath ?? "";
        _rightPathBox.Text = rightPath ?? "";

        _leftBrowseBtn.Click += (_, _) => Browse(_leftPathBox, fs => _leftFs = fs);
        _rightBrowseBtn.Click += (_, _) => Browse(_rightPathBox, fs => _rightFs = fs);
        _compareBtn.Click += (_, _) => _ = CompareFilesAsync();
        _closeBtn.Click += (_, _) => Close();

        // Only after the container is parented and docked: SplitContainer.Width is still its
        // unparented 150px default during InitializeComponent, so setting SplitterDistance against
        // ClientSize.Width any earlier either clamps to that tiny default or is silently orphaned
        // once the container grows to fill the real client area, leaving the splitter stuck near one
        // edge instead of centered.
        _split.SplitterDistance = (ClientSize.Width - 4) / 2;
    }

    /// <summary>A native folder/file picker only ever browses the real local disk - picking a
    /// file this way always switches that side to <see cref="LocalFileSystem"/>, independently of
    /// whatever the side started on (a panel's archive or connection).</summary>
    private static void Browse(TextBox box, Action<IFileSystem> setFs)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = LocalizationService.Current.GetString("Differ.FilterAll")
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            box.Text = dlg.FileName;
            setFs(new LocalFileSystem());
        }
    }

    private async Task CompareFilesAsync()
    {
        var L = LocalizationService.Current;
        var left = _leftPathBox.Text.Trim();
        var right = _rightPathBox.Text.Trim();
        var leftFs = _leftFs;
        var rightFs = _rightFs;

        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right) ||
            !await leftFs.ExistsAsync(left).ConfigureAwait(true) ||
            !await rightFs.ExistsAsync(right).ConfigureAwait(true))
        {
            if (!IsDisposed && IsHandleCreated)
                _statusLabel.Text = L.GetString("Differ.FilesNotFound");
            return;
        }
        if (IsDisposed || !IsHandleCreated) return;

        var leftInfo = await leftFs.GetFileInfoAsync(left).ConfigureAwait(true);
        var rightInfo = await rightFs.GetFileInfoAsync(right).ConfigureAwait(true);
        if (IsDisposed || !IsHandleCreated) return;

        var largestSize = Math.Max(leftInfo?.Size ?? 0, rightInfo?.Size ?? 0);
        if (largestSize > LargeFileConfirmBytes)
        {
            var confirmed = StyledMessageBox.Show(
                L.GetString("Differ.ConfirmLargeFile", FormatUtils.FormatSize(largestSize), FormatUtils.FormatSize(LargeFileConfirmBytes)),
                L.GetString("Common.Confirm"), MsgBoxButtons.YesNo, MsgBoxIcon.Warning, this) == MsgBoxResult.Yes;
            if (!confirmed) return;
        }

        try
        {
            _compareCts?.Cancel();
            _compareCts?.Dispose();
            _compareCts = new CancellationTokenSource();
            var ct = _compareCts.Token;

            var leftLines = await ReadAllLinesAsync(leftFs, left, ct).ConfigureAwait(true);
            var rightLines = await ReadAllLinesAsync(rightFs, right, ct).ConfigureAwait(true);
            if (IsDisposed || !IsHandleCreated) return;
            var maxLines = Math.Max(leftLines.Count, rightLines.Count);

            int diffCount = 0;
            var sbLeft = new System.Text.StringBuilder();
            var sbRight = new System.Text.StringBuilder();

            for (int i = 0; i < maxLines; i++)
            {
                var l = i < leftLines.Count ? leftLines[i] : "";
                var r = i < rightLines.Count ? rightLines[i] : "";
                var lineNum = (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(5);

                if (string.Equals(l, r, StringComparison.Ordinal))
                {
                    sbLeft.AppendLine(CultureInfo.InvariantCulture, $" {lineNum}: {l}");
                    sbRight.AppendLine(CultureInfo.InvariantCulture, $" {lineNum}: {r}");
                }
                else
                {
                    sbLeft.AppendLine(CultureInfo.InvariantCulture, $">{lineNum}: {l}");
                    sbRight.AppendLine(CultureInfo.InvariantCulture, $">{lineNum}: {r}");
                    diffCount++;
                }
            }

            _leftBox.Text = sbLeft.ToString();
            _rightBox.Text = sbRight.ToString();
            _leftBox.SelectionStart = 0;
            _rightBox.SelectionStart = 0;

            _statusLabel.Text = L.GetString("Differ.Summary", leftLines.Count, rightLines.Count, diffCount);
        }
        catch (Exception ex)
        {
            if (!IsDisposed && IsHandleCreated)
                _statusLabel.Text = ex.Message;
            LogService.Error("Differ compare failed", ex);
        }
    }

    /// <summary>Reads every line of <paramref name="path"/> through <paramref name="fs"/> - the
    /// VFS equivalent of <c>File.ReadAllLines</c>, working for a file inside an archive or on a
    /// remote connection the same way it does for a local one. Bounded by the same
    /// <see cref="LargeFileConfirmBytes"/> confirmation the caller already gates on, so this never
    /// buffers more than what the user explicitly agreed to load.</summary>
    private static async Task<List<string>> ReadAllLinesAsync(IFileSystem fs, string path, CancellationToken ct)
    {
        using var stream = await fs.OpenReadAsync(path, ct).ConfigureAwait(true);
        // Binary detection: a null byte in the first 8 KB means this is not a text file —
        // feeding it to StreamReader produces garbage (null chars, invalid UTF-8 replacements)
        // and the diff output is meaningless.
        var probeBuffer = new byte[8192];
        var probeRead = 0;
        while (probeRead < probeBuffer.Length)
        {
            var n = await stream.ReadAsync(probeBuffer.AsMemory(probeRead, probeBuffer.Length - probeRead), ct).ConfigureAwait(true);
            if (n == 0) break;
            probeRead += n;
        }
        for (var i = 0; i < probeRead; i++)
        {
            if (probeBuffer[i] == 0)
                throw new IOException($"\"{VfsPath.GetName(path)}\" appears to be a binary file — text diff is not applicable.");
        }
        stream.Position = 0;

        var lines = new List<string>();
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(true) is { } line)
        {
            lines.Add(line);
            ct.ThrowIfCancellationRequested();
        }
        return lines;
    }

}
