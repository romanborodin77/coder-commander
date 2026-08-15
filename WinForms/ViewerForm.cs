using CoderCommander.FileSystem;
using CoderCommander.Services;
using CoderCommander.Utils;
using CoderCommander.Viewers;
using CoderCommander.Viewers.Formats;
using CoderCommander.WinForms.Viewers;
using System.Threading;

namespace CoderCommander.WinForms;

/// <summary>
/// File viewer (F3): a toolbar-driven mode switcher over a single content surface - no tab strip,
/// no separate mode bar. Text/ASCII/Binary/Hex are always offered (the universal fall-back group,
/// matching Total Commander Lister's own "you can always look at any file as text or hex"
/// philosophy), plus a dynamic button for whatever <see cref="Viewers.IViewerFormat"/> the current
/// file actually matches (Image today; CSV/Markdown/HTML/PDF/media/Office documents in later
/// phases - see <see cref="Viewers.ViewerFormatRegistry"/>). Loading is fully asynchronous and
/// reads through the panel's own <see cref="IFileSystem"/>, so F3 works on a file inside an
/// archive or on a remote connection, not just a real disk path.
/// </summary>
public class ViewerForm : ThemedForm
{
    private readonly IFileSystem _fileSystem;
    private string _path;
    private List<string> _files;
    private int _currentIndex;
    private long _lastKnownSize;

    private string _activeFormatId = TextViewerFormat.Instance.Id;

    private ToolStrip _toolStrip = null!;
    private ToolStripButton _prevBtn = null!, _nextBtn = null!;
    private readonly Dictionary<string, ToolStripButton> _universalButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToolStripButton> _matchedButtons = new(StringComparer.Ordinal);
    private ToolStripButton _firstUniversalButton = null!;
    private ToolStripSeparator _beforeClose = null!;

    private Panel _contentPanel = null!;
    private Panel _contentHost = null!;
    private ViewerFindBar _findBar = null!;
    private ThemedProgressBar _loadingBar = null!;
    private System.Windows.Forms.Timer? _loadingAnimTimer;

    private readonly Dictionary<string, IViewerContent> _contents = new(StringComparer.Ordinal);
    private IViewerContent? _activeContent;

    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _lblFileInfo = null!;
    private ToolStripStatusLabel _lblExtension = null!;
    private ToolStripStatusLabel _lblMode = null!;

    private CancellationTokenSource? _loadCts;
    private bool _disposed;
    private readonly AppSettings _settings;
    private readonly Lazy<ViewerTempSession> _tempSession;
    private readonly Lazy<WebViewHost> _webViewHost;

    /// <summary>
    /// Initializes the viewer form with toolbar, single content surface, and status bar. Loads
    /// the specified file in the resolved initial format (a matched format like Image always
    /// wins for a file it recognizes; otherwise the last-used universal format preference).
    /// </summary>
    public ViewerForm(IFileSystem fileSystem, string path,
                       List<string>? files = null, int currentIndex = 0)
    {
        _fileSystem = fileSystem;
        _path = path;
        _files = files ?? new List<string>();
        _currentIndex = currentIndex;
        _settings = SettingsService.Load();
        _tempSession = new Lazy<ViewerTempSession>(() => new ViewerTempSession());
        _webViewHost = new Lazy<WebViewHost>(() => new WebViewHost());

        var L = LocalizationService.Current;
        Text = $"{L.GetString("View.Title")} — {VfsPath.GetName(path)}";
        ClientSize = new Size(1000, 700);
        Resizable = true;
        MinimumSize = new Size(500, 400);
        // Form sees every key first (Escape/arrows/F5/Ctrl+F/1-4/etc.) regardless of which child
        // control currently has focus - the read-only content view would otherwise swallow arrow
        // keys for its own (useless, given ReadOnly) caret movement instead of them reaching
        // NavigateFile. OnViewerKeyDown explicitly steps aside while the find bar holds focus so
        // typing/arrow-editing a search term still works normally.
        KeyPreview = true;

        BuildToolbar();
        BuildContentPanel();
        BuildStatusBar();

        // WinForms: Fill must be at index 0 (drawn first, gets remaining space).
        // Top/Bottom drawn on top. Fix docking overlap.
        Controls.SetChildIndex(_contentPanel, 0);
        Controls.SetChildIndex(_toolStrip, 1);
        Controls.SetChildIndex(_statusStrip, 2);

        _activeFormatId = ResolveInitialFormat();
        UpdateModeButtonHighlight();

        KeyDown += OnViewerKeyDown;
        Load += (_, _) => _ = LoadFileAsync();
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ApplyTheme();
        foreach (var content in _contents.Values) content.ApplyTheme();
        _findBar.ApplyTheme();
    }

    // ── Build ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Builds the toolbar: navigation, then the mode group (a dynamic matched-format
    /// button, if the current file has one, followed by the fixed Text/ASCII/Binary/Hex group),
    /// then the active content's own items (Find/Word Wrap for text-family, zoom/rotate for
    /// Image - inserted lazily by <see cref="GetOrCreateContent"/> as each format is first
    /// visited), then Close.</summary>
    private void BuildToolbar()
    {
        var L = LocalizationService.Current;

        _toolStrip = new ToolStrip
        {
            Dock = DockStyle.Top,
            ImageScalingSize = new Size(16, 16),
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(4, 2, 4, 2),
            Renderer = new ThemeRenderer()
        };

        _prevBtn = ViewerToolbarFactory.CreateToolButton("View.Toolbar.Previous", "back", (_, _) => NavigateFile(-1));
        _nextBtn = ViewerToolbarFactory.CreateToolButton("View.Toolbar.Next", "forward", (_, _) => NavigateFile(1));
        _toolStrip.Items.Add(_prevBtn);
        _toolStrip.Items.Add(_nextBtn);
        _toolStrip.Items.Add(new ToolStripSeparator());

        foreach (var format in ViewerFormatRegistry.Universal)
        {
            var fmt = format;
            var btn = ViewerToolbarFactory.CreateToolButton(fmt.DisplayNameKey, fmt.IconKey, (_, _) => SetFormat(fmt.Id));
            _universalButtons[fmt.Id] = btn;
            _toolStrip.Items.Add(btn);
        }
        _firstUniversalButton = _universalButtons[TextViewerFormat.Instance.Id];

        _toolStrip.Items.Add(new ToolStripSeparator());
        _beforeClose = new ToolStripSeparator();
        _toolStrip.Items.Add(_beforeClose);

        var closeBtn = new ToolStripButton(L.GetString("Common.Close"), ToolbarIcons.Get("close"));
        closeBtn.Click += (_, _) => Close();
        _toolStrip.Items.Add(closeBtn);

        Controls.Add(_toolStrip);
    }

    /// <summary>Builds the single content surface: a plain host panel that every format's
    /// <see cref="IViewerContent.View"/> is added to (<c>Dock=Fill</c>, only one
    /// <see cref="Control.Visible"/> at a time - the literal replacement for the removed
    /// <c>ThemedTabControl</c>, no re-parenting, no handle recreation). The find bar and loading
    /// strip are docked <c>Top</c> siblings of the host, not children of it - <see cref="_contentHost"/>
    /// itself has only <c>Dock=Fill</c> children (added lazily, in any order), so the
    /// Fill-before-Top docking-order rule only has to be honored once, right here, rather than at
    /// every future lazy content-view insertion.</summary>
    private void BuildContentPanel()
    {
        _contentPanel = new Panel { Dock = DockStyle.Fill };
        _contentHost = new Panel { Dock = DockStyle.Fill };

        _findBar = new ViewerFindBar();
        _loadingBar = new ThemedProgressBar { Dock = DockStyle.Top, Height = 3, Visible = false };

        _contentPanel.Controls.Add(_contentHost);
        _contentPanel.Controls.Add(_findBar);
        _contentPanel.Controls.Add(_loadingBar);

        Controls.Add(_contentPanel);
    }

    /// <summary>Builds the status bar with file info, extension, and mode labels. Zoom (Image
    /// mode only) is folded into the mode label itself now - see
    /// <see cref="IViewerContent.StatusText"/> - rather than a separate always-present label that
    /// used to be hidden outside Image mode.</summary>
    private void BuildStatusBar()
    {
        var p = ThemeService.Current;

        _statusStrip = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            SizingGrip = true,
            Renderer = new ThemeRenderer()
        };

        _lblFileInfo = new ToolStripStatusLabel { Text = "", ForeColor = p.DimForeground, Margin = new Padding(4, 0, 8, 0) };
        _lblExtension = new ToolStripStatusLabel { Text = "", ForeColor = p.DimForeground, Margin = new Padding(4, 0, 8, 0) };
        _lblMode = new ToolStripStatusLabel { Text = "", ForeColor = p.DimForeground, Margin = new Padding(4, 0, 8, 0) };

        _statusStrip.Items.AddRange(new ToolStripItem[]
        {
            _lblFileInfo,
            new ToolStripSeparator(),
            _lblExtension,
            new ToolStripSeparator(),
            _lblMode
        });

        Controls.Add(_statusStrip);
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Handles keyboard shortcuts: Escape (close find bar first, else close viewer),
    /// arrows (navigate), F5 (reload), Ctrl+F (search, no-op if the active format isn't
    /// searchable), 1-4 (switch to Text/ASCII/Binary/Hex).</summary>
    private void OnViewerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            if (_findBar.Visible) { _findBar.CloseBar(); e.Handled = true; return; }
            Close();
            e.Handled = true;
            return;
        }

        // Let the find bar's own controls handle everything else while they hold focus (typing a
        // search term, moving the caret within it with the arrow keys) - KeyPreview means the form
        // would otherwise steal Left/Right for file navigation before the textbox ever sees them.
        if (_findBar.Visible && _findBar.ContainsFocus) return;

        if (e.Control && e.KeyCode == Keys.F)
        {
            _findBar.ShowBar();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Up)
        {
            NavigateFile(-1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Down)
        {
            NavigateFile(1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F5)
        {
            _ = LoadFileAsync();
            e.Handled = true;
        }
        else if (!e.Control && !e.Alt && e.KeyCode is Keys.D1 or Keys.D2 or Keys.D3 or Keys.D4)
        {
            var id = e.KeyCode switch
            {
                Keys.D1 => TextViewerFormat.Instance.Id,
                Keys.D2 => AsciiViewerFormat.Instance.Id,
                Keys.D3 => BinaryViewerFormat.Instance.Id,
                _ => HexViewerFormat.Instance.Id,
            };
            SetFormat(id);
            e.Handled = true;
        }
    }

    // ── Format selection ─────────────────────────────────────────────────────────────────────

    /// <summary>Resolves the format to open the initial file in: a matched format (Image today)
    /// always wins for a file it recognizes; otherwise the last-used universal format preference
    /// from settings, falling back to Text if that preference is stale/unknown. Runs once, at
    /// construction - <see cref="NavigateFile"/> deliberately does not re-run this (see its own
    /// doc comment).</summary>
    private string ResolveInitialFormat()
    {
        UpdateMatchedFormatForCurrentFile();

        var matched = ViewerFormatRegistry.Detect(_path, ReadOnlySpan<byte>.Empty);
        if (matched != null) return matched.Id;

        var last = _settings.ViewerLastMode;
        return ViewerFormatRegistry.ById(last) is { Availability: ViewerAvailability.Universal }
            ? last
            : TextViewerFormat.Instance.Id;
    }

    /// <summary>Switches the active format (from a toolbar button click or a digit-key shortcut),
    /// persists the preference for universal formats only (never a matched format - the same
    /// reasoning the old <c>ViewerLastMode</c> doc comment gave for never persisting "Image": it
    /// would make the next unrelated file default to a forced, likely-failing decode), and
    /// reloads. The visible content/toolbar-group swap happens only once the new load actually
    /// succeeds (see <see cref="LoadFileAsync"/>/<see cref="ShowContent"/>) - only the button
    /// highlight changes immediately, for instant click feedback.</summary>
    private void SetFormat(string formatId)
    {
        if (_activeFormatId == formatId) return;

        var format = ViewerFormatRegistry.ById(formatId);
        if (format == null) return;

        _activeFormatId = formatId;
        if (format.Availability == ViewerAvailability.Universal)
        {
            _settings.ViewerLastMode = formatId;
            SettingsService.Save(_settings);
        }

        UpdateModeButtonHighlight();
        _ = LoadFileAsync();
    }

    private void UpdateModeButtonHighlight()
    {
        foreach (var (id, btn) in _universalButtons) btn.Checked = id == _activeFormatId;
        foreach (var (id, btn) in _matchedButtons) btn.Checked = id == _activeFormatId;
    }

    /// <summary>Determines which <see cref="ViewerAvailability.Matched"/> format (if any) applies
    /// to <see cref="_path"/>, lazily creates its toolbar button the first time it's encountered
    /// in this window, and shows/hides every cached matched-format button so at most one is
    /// visible - called on every navigation, independent of which format is actually active
    /// (matched-format buttons reflect "what this file could be shown as", not the sticky active
    /// selection).</summary>
    private void UpdateMatchedFormatForCurrentFile()
    {
        var detected = ViewerFormatRegistry.Detect(_path, ReadOnlySpan<byte>.Empty);

        foreach (var (id, btn) in _matchedButtons)
            btn.Visible = detected != null && id == detected.Id;

        if (detected != null && !_matchedButtons.ContainsKey(detected.Id))
        {
            var fmt = detected;
            var btn = ViewerToolbarFactory.CreateToolButton(fmt.DisplayNameKey, fmt.IconKey, (_, _) => SetFormat(fmt.Id));
            btn.Checked = _activeFormatId == fmt.Id;
            _matchedButtons[fmt.Id] = btn;
            _toolStrip.Items.Insert(_toolStrip.Items.IndexOf(_firstUniversalButton), btn);
        }
    }

    private void SetNavigationEnabled(bool enabled)
    {
        _prevBtn.Enabled = enabled;
        _nextBtn.Enabled = enabled;
        foreach (var btn in _universalButtons.Values) btn.Enabled = enabled;
        foreach (var btn in _matchedButtons.Values) btn.Enabled = enabled;
    }

    /// <summary>Gets the cached content for <paramref name="format"/>, creating it (view added to
    /// <see cref="_contentHost"/>, toolbar items inserted just before <see cref="_beforeClose"/>,
    /// both initially hidden) the first time this format is visited in this window.</summary>
    private IViewerContent GetOrCreateContent(IViewerFormat format)
    {
        if (_contents.TryGetValue(format.Id, out var existing)) return existing;

        var ctx = new ViewerContentContext(_settings, () => _findBar.ShowBar(), () => _ = LoadFileAsync(),
            _webViewHost, _tempSession);
        var content = format.CreateContent(ctx);

        _contentHost.Controls.Add(content.View);
        foreach (var item in content.ToolbarItems)
        {
            item.Visible = false;
            _toolStrip.Items.Insert(_toolStrip.Items.IndexOf(_beforeClose), item);
        }

        content.StatusChanged += (_, _) =>
        {
            if (ReferenceEquals(_activeContent, content)) _lblMode.Text = content.StatusText ?? "";
        };

        _contents[format.Id] = content;
        return content;
    }

    /// <summary>Swaps the visible content view and toolbar-item group to <paramref name="content"/>,
    /// and points the find bar at whatever it can search (null for Image). Called once a load has
    /// actually produced something to show - see <see cref="LoadFileAsync"/>.</summary>
    private void ShowContent(IViewerContent content)
    {
        foreach (var existing in _contents.Values)
        {
            var isActive = ReferenceEquals(existing, content);
            existing.View.Visible = isActive;
            foreach (var item in existing.ToolbarItems) item.Visible = isActive;
        }
        _activeContent = content;
        _findBar.SetTarget(content.SearchTarget);
    }

    // ── Navigation ───────────────────────────────────────────────────────────────────────────

    /// <summary>Navigates to the next or previous file in the folder (wrapping around).
    /// Deliberately does NOT re-run <see cref="ResolveInitialFormat"/> - the active format stays
    /// sticky across Prev/Next (matches this viewer's behavior before this rewrite, now a stated
    /// policy rather than an accidental one); the dynamic matched-format button still updates to
    /// reflect the new file, so the user can switch to it explicitly if they want to.</summary>
    private void NavigateFile(int direction)
    {
        if (_files.Count == 0) return;

        _currentIndex += direction;
        if (_currentIndex < 0) _currentIndex = _files.Count - 1;
        if (_currentIndex >= _files.Count) _currentIndex = 0;

        _path = _files[_currentIndex];

        var L = LocalizationService.Current;
        Text = $"{L.GetString("View.Title")} — {VfsPath.GetName(_path)}";
        UpdateMatchedFormatForCurrentFile();
        _ = LoadFileAsync();
    }

    // ── Async loading ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads <see cref="_path"/> in the current <see cref="_activeFormatId"/> off the UI thread.
    /// Cancels any still-in-flight previous load first. Pattern mirrors
    /// <c>FindFilesForm.StartSearchAsync</c> (<c>Task.Run</c> + per-run
    /// <see cref="CancellationTokenSource"/> + guarded <c>finally</c>), extended with an explicit
    /// <c>payload</c> local whose ownership is tracked all the way through: a payload that never
    /// reaches <see cref="IViewerContent.RenderAsync"/> (superseded mid-flight, or the form
    /// closing mid-render) is released via <see cref="ViewerPayload.ReleaseUnapplied"/> rather
    /// than silently dropped - closing the leak the previous rewrite's staleness guard had for a
    /// decoded <see cref="Image"/> discarded without disposal.
    /// </summary>
    private async Task LoadFileAsync()
    {
        _findBar.CloseBar();

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var ct = cts.Token;

        // Snapshot locals on the UI thread before entering the background task - reading _path/
        // _activeFormatId from inside the Task.Run lambda would race a NavigateFile/SetFormat call
        // that changes those fields after this method returns control (at the first await) but
        // before the lambda actually starts running on a pool thread.
        var path = _path;
        var format = ViewerFormatRegistry.ById(_activeFormatId) ?? TextViewerFormat.Instance;
        var source = new ViewerSource(_fileSystem, path);
        var content = GetOrCreateContent(format);

        SetNavigationEnabled(false);
        _loadingBar.Value = 0;
        _loadingBar.Visible = true;
        _loadingAnimTimer ??= new System.Windows.Forms.Timer { Interval = 30 };
        _loadingAnimTimer.Tick -= OnLoadingAnimTick;
        _loadingAnimTimer.Tick += OnLoadingAnimTick;
        _loadingAnimTimer.Start();

        ViewerPayload? payload = null;
        try
        {
            var loader = format.CreateLoader();

            // Fetched here for the shared file-info status label; the loader also fetches it
            // internally for its own size-based limit checks and status text - one extra metadata
            // round-trip on a remote filesystem, accepted rather than threading a pre-fetched size
            // through IViewerLoader's signature for every format.
            var size = await source.GetSizeAsync(ct);
            if (ct != _loadCts?.Token) return;

            payload = await Task.Run(() => loader.LoadAsync(source, ct), ct);
            // A newer LoadFileAsync call may have already superseded this one while the
            // Task.Run was finishing up (GDI+ decode/File I/O isn't reliably cancellable
            // mid-operation) - discard a result that arrives after it's no longer current, rather
            // than flashing the wrong file's content on screen.
            if (ct != _loadCts?.Token)
            {
                payload.ReleaseUnapplied();
                payload = null;
                return;
            }

            ShowContent(content);
            await content.RenderAsync(payload, ct);
            payload = null; // ownership transferred to the content
            if (ct != _loadCts?.Token) return; // superseded while RenderAsync was awaiting

            _lastKnownSize = size;
            UpdateStatus(content);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load - nothing to do.
        }
        catch (Exception ex)
        {
            LogService.Error($"Viewer load failed: {path}", ex);
            if (ct == _loadCts?.Token)
            {
                ShowContent(content);
                var errorPayload = new ViewerErrorPayload(
                    $"{LocalizationService.Current.GetString("View.Error")}: {ex.Message}", Modal: false);
                await content.RenderAsync(errorPayload, ct);
                UpdateStatus(content);
            }
        }
        finally
        {
            payload?.ReleaseUnapplied();
            if (ct == _loadCts?.Token)
            {
                _loadingAnimTimer?.Stop();
                _loadingBar.Visible = false;
                SetNavigationEnabled(true);
            }
        }
    }

    private void OnLoadingAnimTick(object? sender, EventArgs e) =>
        _loadingBar.Value = (_loadingBar.Value + 7) % 100;

    private void UpdateStatus(IViewerContent content)
    {
        var ext = FileEntry.GetExtension(_path).ToUpperInvariant().TrimStart('.');
        _lblFileInfo.Text = $"{VfsPath.GetName(_path)} ({FormatUtils.FormatSize(_lastKnownSize)})";
        _lblExtension.Text = ext;
        _lblMode.Text = content.StatusText ?? "";
    }

    // ── Disposal ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Unsubscribes from theme events, cancels any in-flight load, and disposes every
    /// created content (which in turn releases whatever non-control resources it holds - decoded
    /// images for <c>ImageViewerContent</c>). Guarded by <see cref="_disposed"/> because WinForms
    /// can route Dispose(true) through this override more than once for the same instance (e.g.
    /// the form's own Close() plus the owner form disposing its owned windows on shutdown) -
    /// without the guard, the second call hits an already-disposed <see cref="_loadCts"/> and
    /// <see cref="CancellationTokenSource.Cancel"/> throws <see cref="ObjectDisposedException"/>
    /// as an unhandled exception on the UI thread.
    ///
    /// <para>Nulling <see cref="_loadCts"/> after disposing it (not just the disposed-guard above)
    /// matters on its own: a still-running <see cref="LoadFileAsync"/> reads <c>_loadCts?.Token</c>
    /// as its staleness check at several points after this method returns (the load itself is not
    /// cancelled synchronously - GDI+ decode/file I/O isn't reliably cancellable mid-operation).
    /// With the field left pointing at the disposed instance, <c>.Token</c> throws
    /// <see cref="ObjectDisposedException"/> on that next check instead of the intended "superseded,
    /// stop here" comparison; with it nulled, <c>_loadCts?.Token</c> short-circuits to <c>null</c>
    /// and the existing <c>ct != _loadCts?.Token</c> comparisons correctly read as "no longer
    /// current" without touching the disposed object.</para></summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            ThemeService.ThemeChanged -= OnThemeChanged;
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
            _loadingAnimTimer?.Dispose();
            foreach (var content in _contents.Values) content.Dispose();
            // WebViewHost before ViewerTempSession: disposing the WebView2 control releases its
            // handle on whatever materialized file it last navigated to, so the session's
            // directory delete (best-effort, but still) has a better chance of succeeding.
            if (_webViewHost.IsValueCreated) _webViewHost.Value.Dispose();
            if (_tempSession.IsValueCreated) _tempSession.Value.Dispose();
        }
        base.Dispose(disposing);
    }
}
