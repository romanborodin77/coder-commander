using CoderCommander.FileSystem;
using CoderCommander.Services;
using CoderCommander.Utils;
using CoderCommander.Viewers;
using CoderCommander.Viewers.Formats;
using System.Threading;

namespace CoderCommander.WinForms.Viewers;

/// <summary>
/// Everything <c>ViewerForm</c> (F3) used to own directly, minus the window chrome: format
/// switching, the <see cref="IViewerContent"/> cache, the async load state machine (staleness
/// guards, <see cref="ViewerPayload.ReleaseUnapplied"/>), and the two <see cref="Lazy{T}"/>
/// (<c>WebViewHost</c>/<c>ViewerTempSession</c>) with their documented dispose order. Extracted
/// (Ф4 plan, step 1 - a pure move, no behavior change) so Quick View can reuse the exact same
/// machinery inside a compact panel-hosted control instead of a full F3 window; <c>ViewerForm</c>
/// itself is now a thin <see cref="ThemedForm"/> wrapper that docks one of these and forwards
/// window-level concerns (title, Escape-closes, <c>KeyPreview</c>) to it.
///
/// <para><b>Not a <see cref="Form"/></b>: keyboard routing can't rely on <c>Form.KeyPreview</c>
/// here, so <see cref="HandleKeyDown"/> is a plain public method the embedder's own key-handling
/// calls into (<c>ViewerForm.KeyPreview</c>+<c>KeyDown</c> today) - what "Escape" means differs
/// per embedder (close the window vs. exit Quick View vs. do nothing), so this control never closes
/// anything itself; it raises <see cref="CloseRequested"/> and lets the embedder decide.</para>
///
/// <para><b>Self-themed</b> (<see cref="ISelfThemedControl"/>): re-themes its own chrome via
/// <see cref="ControlThemer.ThemeDescendants"/> plus every cached <see cref="IViewerContent"/>'s
/// own bespoke <c>ApplyTheme()</c> (not reachable by the generic role-based walk) and the find
/// bar's. Subscribes to <see cref="ThemeService.ThemeChanged"/> itself rather than relying on an
/// ancestor <see cref="ThemedForm"/> to find it via the descendant walk - Quick View's host
/// (<c>FilePanelUserControl</c>) is a plain <see cref="UserControl"/>, not a <see cref="ThemedForm"/>,
/// so there is no such walk to rely on there.</para>
/// </summary>
public sealed class ViewerHostControl : UserControl, ISelfThemedControl
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

    /// <summary>The file currently shown - read by the embedder to build a window title/tab label
    /// (this control has no title of its own). Updated by <see cref="NavigateFile"/>; see
    /// <see cref="PathChanged"/>.</summary>
    public string CurrentPath => _path;

    /// <summary>Raised whenever <see cref="CurrentPath"/> changes (Prev/Next navigation) - the
    /// embedder re-reads <see cref="CurrentPath"/> in response (e.g. to update a window title).
    /// Not raised at construction; the embedder reads the initial path directly.</summary>
    public event EventHandler? PathChanged;

    /// <summary>Raised when Escape is pressed and there is nothing left for this control itself to
    /// do with it (the find bar, if visible, already consumed Escape by closing itself first) -
    /// what "close" means is entirely up to the embedder (close the window, exit Quick View,
    /// nothing at all), so this control never closes anything on its own.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>When true, a load failure that would normally pop a modal <c>StyledMessageBox</c>
    /// (<see cref="ViewerErrorPayload.Modal"/> - a format's loader saying "this needs the user's
    /// attention", e.g. a password-protected archive or an unsupported/corrupt file) instead
    /// renders inline like any other error, same as it already does for a non-modal one. Off by
    /// default (F3's own behavior, unchanged) - Quick View sets this, since a modal dialog on
    /// every arrow-key tick through a folder with one broken image would make browsing unusable.
    /// Set once, at construction (<c>init</c>) - this control is never toggled between the two
    /// uses after the fact, F3 and Quick View each construct their own instance.</summary>
    public bool CompactMode { get; init; }

    /// <summary>
    /// Builds the toolbar, content surface and status bar, and resolves the initial format for
    /// <paramref name="path"/> - does not start loading it; the embedder calls
    /// <see cref="LoadCurrentAsync"/> when it wants that to happen (immediately for F3, debounced
    /// for Quick View).
    /// </summary>
    public ViewerHostControl(IFileSystem fileSystem, string path, List<string>? files, int currentIndex, AppSettings settings)
    {
        _fileSystem = fileSystem;
        _path = path;
        _files = files ?? new List<string>();
        _currentIndex = currentIndex;
        _settings = settings;
        _tempSession = new Lazy<ViewerTempSession>(() => new ViewerTempSession());
        _webViewHost = new Lazy<WebViewHost>(() => new WebViewHost());

        Dock = DockStyle.Fill;

        BuildToolbar();
        BuildContentPanel();
        BuildStatusBar();

        // Fill must be added before Top/Bottom siblings (WinForms lays out docked children from
        // the last-added Controls index down to the first) - added in the right order directly,
        // rather than building then fixing up with SetChildIndex.
        Controls.Add(_contentPanel);
        Controls.Add(_toolStrip);
        Controls.Add(_statusStrip);

        _activeFormatId = ResolveInitialFormat();
        UpdateModeButtonHighlight();

        ThemeService.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => RefreshTheme();

    /// <summary>Re-themes this control's own chrome (toolbar/status bar/content host) via the
    /// generic role-based walk, plus every cached <see cref="IViewerContent"/>'s own bespoke
    /// <c>ApplyTheme()</c> and the find bar's - neither is reachable by the generic walk alone.</summary>
    public void RefreshTheme()
    {
        var p = ThemeService.Current;
        BackColor = p.PanelBackground;
        ControlThemer.ThemeDescendants(this);
        foreach (var content in _contents.Values) content.ApplyTheme();
        _findBar.ApplyTheme();
    }

    // ── Build ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Builds the toolbar: navigation, then the mode group (a dynamic matched-format
    /// button, if the current file has one, followed by the fixed Text/ASCII/Binary/Hex group),
    /// then the active content's own items (Find/Word Wrap for text-family, zoom/rotate for
    /// Image - inserted lazily by <see cref="GetOrCreateContent"/> as each format is first
    /// visited).</summary>
    private void BuildToolbar()
    {
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
    }

    /// <summary>Builds the single content surface: a plain host panel that every format's
    /// <see cref="IViewerContent.View"/> is added to (<c>Dock=Fill</c>, only one
    /// <see cref="Control.Visible"/> at a time - no re-parenting, no handle recreation). The find
    /// bar and loading strip are docked <c>Top</c> siblings of the host, not children of it -
    /// <see cref="_contentHost"/> itself has only <c>Dock=Fill</c> children (added lazily, in any
    /// order), so the Fill-before-Top docking-order rule only has to be honored once, right here,
    /// rather than at every future lazy content-view insertion.</summary>
    private void BuildContentPanel()
    {
        _contentPanel = new Panel { Dock = DockStyle.Fill };
        _contentHost = new Panel { Dock = DockStyle.Fill };

        _findBar = new ViewerFindBar();
        _loadingBar = new ThemedProgressBar { Dock = DockStyle.Top, Height = 3, Visible = false };

        _contentPanel.Controls.Add(_contentHost);
        _contentPanel.Controls.Add(_findBar);
        _contentPanel.Controls.Add(_loadingBar);
    }

    /// <summary>Builds the status bar with file info, extension, and mode labels. Zoom (Image
    /// mode only) is folded into the mode label itself - see <see cref="IViewerContent.StatusText"/> -
    /// rather than a separate always-present label that used to be hidden outside Image mode.</summary>
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
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Handles keyboard shortcuts the embedder forwards to this control: Escape (close
    /// find bar, else raise <see cref="CloseRequested"/>), arrows (navigate), F5 (reload), Ctrl+F
    /// (search, no-op if the active format isn't searchable), 1-4 (switch to Text/ASCII/Binary/Hex).
    /// While the find bar's own controls hold focus, everything except Escape falls through
    /// unhandled so typing/arrow-editing a search term keeps working normally.</summary>
    public void HandleKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            if (_findBar.Visible) { _findBar.CloseBar(); e.Handled = true; return; }
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

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
            _ = LoadCurrentAsync();
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
    /// from settings, falling back to Text if that preference is stale/unknown. Runs at
    /// construction, and again from <see cref="LoadPath"/> (Quick View retargeting at a whole
    /// different file wants a fresh natural-format decision each time) - <see cref="NavigateFile"/>
    /// (F3's own Prev/Next within one folder) deliberately does not re-run this, see its own doc
    /// comment for why sticky is right there.</summary>
    private string ResolveInitialFormat()
    {
        UpdateMatchedFormatForCurrentFile();

        var matched = DetectMatchedFormat(ReadOnlySpan<byte>.Empty);
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
    /// succeeds (see <see cref="LoadCurrentAsync"/>/<see cref="ShowContent"/>) - only the button
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
        _ = LoadCurrentAsync();
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
    /// selection).
    ///
    /// <para><paramref name="header"/> defaults to empty for the callers that need an instant,
    /// synchronous answer before any I/O has happened (construction, the moment Prev/Next is
    /// clicked) - in that shape only <see cref="IViewerFormat.MatchesExtension"/> ever contributes,
    /// same as before this parameter existed. <see cref="LoadCurrentAsync"/> calls this a second
    /// time once it has actually read a real prefix off the file, letting
    /// <see cref="IViewerFormat.MatchesSignature"/> contribute too - e.g. a screenshot saved as
    /// <c>capture.dat</c> or a PDF named <c>invoice.bin</c> gets its Image/PDF button offered once
    /// the bytes are in hand, without ever blocking the UI thread on a synchronous read to get
    /// there.</para></summary>
    private void UpdateMatchedFormatForCurrentFile(ReadOnlySpan<byte> header = default)
    {
        var detected = DetectMatchedFormat(header);

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

    /// <summary>Format-detection choke point for both <see cref="UpdateMatchedFormatForCurrentFile"/>
    /// and <see cref="ResolveInitialFormat"/> - in <see cref="CompactMode"/> (Quick View), Media
    /// never wins the "what is this file" match: a video/audio file's own player format would
    /// autoplay the instant its button becomes active, which arrow-key browsing would trigger on
    /// every file. Universal formats (Text/ASCII/Binary/Hex) are unaffected - only the dynamic
    /// matched-format button/initial-format selection is filtered.</summary>
    private IViewerFormat? DetectMatchedFormat(ReadOnlySpan<byte> header)
    {
        var detected = ViewerFormatRegistry.Detect(_path, header);
        return CompactMode && detected?.Id == MediaViewerFormat.Instance.Id ? null : detected;
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
    /// both initially hidden) the first time this format is visited.</summary>
    private IViewerContent GetOrCreateContent(IViewerFormat format)
    {
        if (_contents.TryGetValue(format.Id, out var existing)) return existing;

        var ctx = new ViewerContentContext(_settings, () => _findBar.ShowBar(), () => _ = LoadCurrentAsync(),
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
    /// actually produced something to show - see <see cref="LoadCurrentAsync"/>.</summary>
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
    /// sticky across Prev/Next (a stated policy, not an accidental one); the dynamic matched-format
    /// button still updates to reflect the new file, so the user can switch to it explicitly if
    /// they want to.</summary>
    private void NavigateFile(int direction)
    {
        if (_files.Count == 0) return;

        _currentIndex += direction;
        if (_currentIndex < 0) _currentIndex = _files.Count - 1;
        if (_currentIndex >= _files.Count) _currentIndex = 0;

        _path = _files[_currentIndex];

        UpdateMatchedFormatForCurrentFile();
        PathChanged?.Invoke(this, EventArgs.Empty);
        _ = LoadCurrentAsync();
    }

    /// <summary>Retargets this control at an entirely different file - unlike
    /// <see cref="NavigateFile"/> (F3's own Prev/Next within one already-open folder, format stays
    /// sticky), this re-resolves the format fresh, the same as a brand new file opened for the
    /// first time. Used by Quick View when the panel's cursor moves to a different file, so each
    /// one opens in its own natural format (an image after a text file shows as an image, not
    /// forced into whatever format the previous file happened to be showing). Does not itself
    /// start loading - the caller still calls <see cref="LoadCurrentAsync"/>.</summary>
    public void LoadPath(string path, List<string>? files = null, int currentIndex = 0)
    {
        _path = path;
        _files = files ?? new List<string>();
        _currentIndex = currentIndex;
        _activeFormatId = ResolveInitialFormat();
        UpdateModeButtonHighlight();
        PathChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Async loading ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads <see cref="_path"/> in the current active format off the UI thread. Cancels any
    /// still-in-flight previous load first. Pattern mirrors <c>FindFilesForm.StartSearchAsync</c>
    /// (<c>Task.Run</c> + per-run <see cref="CancellationTokenSource"/> + guarded <c>finally</c>),
    /// extended with an explicit <c>payload</c> local whose ownership is tracked all the way
    /// through: a payload that never reaches <see cref="IViewerContent.RenderAsync"/> (superseded
    /// mid-flight, or the control disposing mid-render) is released via
    /// <see cref="ViewerPayload.ReleaseUnapplied"/> rather than silently dropped.
    ///
    /// <para>The embedder decides when this runs - called once from <c>ViewerForm.Load</c> for F3;
    /// Quick View calls it after its own debounce timer fires, never on every arrow-key tick.</para>
    /// </summary>
    public async Task LoadCurrentAsync()
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

            // A small, cheap extra read (see UpdateMatchedFormatForCurrentFile's own doc comment)
            // so signature-based matched-format detection (Image/PDF-by-content, not just by
            // extension) actually has real bytes to work with. Best-effort: a failed prefix read
            // (permission hiccup, connection drop) just means this file keeps whatever
            // extension-only match it already had, not a load failure.
            // Skip signature-prefix read for non-native filesystems (MTP/archive/remote) —
            // OpenReadAsync on MTP downloads the entire file to a temp, so ReadPrefixAsync would
            // download it once for 512 bytes, discard the temp, then LoadAsync downloads it again.
            if (source.FileSystem.Capabilities.HasFlag(FileSystemCapabilities.NativePaths))
            {
                try
                {
                    var header = await source.ReadPrefixAsync(512, ct);
                    if (ct == _loadCts?.Token) UpdateMatchedFormatForCurrentFile(header);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogService.Warning($"Viewer signature-prefix read failed: {path}: {ex.Message}");
                }
            }
            if (ct != _loadCts?.Token) return;

            payload = await Task.Run(() => loader.LoadAsync(source, ct), ct);
            // CompactMode: a loader's "this needs the user's attention" modal error (password-
            // protected, corrupt, unsupported) would otherwise pop a StyledMessageBox from inside
            // RenderAsync - fine once, for a deliberate F3 open, but Quick View calls this on every
            // arrow-key tick, and a broken file in the middle of a folder would mean a dialog per
            // keystroke. Downgraded to the same inline rendering a non-modal error already gets;
            // the eight loader classes that can return Modal:true stay untouched.
            if (CompactMode && payload is ViewerErrorPayload { Modal: true } modalError)
                payload = modalError with { Modal = false };
            // A newer LoadCurrentAsync call may have already superseded this one while the
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
    /// can route Dispose(true) through this override more than once for the same instance -
    /// without the guard, the second call hits an already-disposed <see cref="_loadCts"/> and
    /// <see cref="CancellationTokenSource.Cancel"/> throws <see cref="ObjectDisposedException"/>
    /// as an unhandled exception on the UI thread.
    ///
    /// <para>Nulling <see cref="_loadCts"/> after disposing it (not just the disposed-guard above)
    /// matters on its own: a still-running <see cref="LoadCurrentAsync"/> reads <c>_loadCts?.Token</c>
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
            _toolStrip?.Dispose();
            _prevBtn?.Dispose();
            _nextBtn?.Dispose();
            _firstUniversalButton?.Dispose();
            _contentPanel?.Dispose();
            _contentHost?.Dispose();
            _beforeClose?.Dispose();
            _statusStrip?.Dispose();
            _lblFileInfo?.Dispose();
            _lblExtension?.Dispose();
            _lblMode?.Dispose();
            _loadingBar?.Dispose();
            _findBar?.Dispose();
        }
        base.Dispose(disposing);
    }
}
