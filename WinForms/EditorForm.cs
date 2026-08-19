using CoderCommander.FileSystem;
using CoderCommander.Services;
using CoderCommander.Utils;
using System.Text;

namespace CoderCommander.WinForms;

/// <summary>
/// Professional tabbed code editor with syntax highlighting, toolbar, and status bar.
/// </summary>
public class EditorForm : ThemedForm
{
    /// <summary>Above this, opening a file means loading the whole thing into memory via
    /// <see cref="EditorTab.LoadFile"/> (<c>File.ReadAllBytes</c>, no streaming) - large enough to
    /// freeze the UI thread for seconds or throw <see cref="OutOfMemoryException"/> on a multi-GB
    /// log/dump. Same threshold <see cref="ViewerForm"/> uses for its own text mode.</summary>
    private const long LargeFileConfirmBytes = 16 * 1024 * 1024;

    private readonly List<EditorTab> _tabs = new();
    private readonly List<ThemedTabPage> _tabPages = new();
    private ThemedTabControl _tabControl = null!;
    private ToolStrip _toolStrip = null!;
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _lblPosition = null!;
    private ToolStripStatusLabel _lblLanguage = null!;
    private ToolStripStatusLabel _lblEncoding = null!;
    private ToolStripStatusLabel _lblFileSize = null!;
    private ToolStripStatusLabel _lblModified = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="EditorForm"/> class, optionally opening the
    /// specified file.
    /// </summary>
    /// <param name="fileSystem">Filesystem <paramref name="path"/> lives on; null (or omitted via
    /// the other constructor) means local disk. Only ever consulted for the INITIAL file - every
    /// subsequent File&gt;Open/Save-As in this window goes through its own dialog, which can only
    /// ever produce a real local path, so those always use a fresh <see cref="LocalFileSystem"/>
    /// regardless of what this file came from.</param>
    /// <param name="path">File path to open on startup, or <c>null</c>/<see cref="string.Empty"/> to create an empty tab.</param>
    public EditorForm(IFileSystem? fileSystem, string? path)
    {
        var L = LocalizationService.Current;
        Text = L.GetString("Edit.Title");
        ClientSize = new Size(1000, 700);
        Resizable = true;
        MinimumSize = new Size(500, 400);

        BuildToolbar();
        BuildTabControl();
        BuildStatusBar();

        // WinForms: Fill must be at index 0 (drawn first, gets remaining space).
        // Top/Bottom drawn on top. Fix docking overlap.
        Controls.SetChildIndex(_tabControl, 0);
        Controls.SetChildIndex(_toolStrip, 1);
        Controls.SetChildIndex(_statusStrip, 2);

        // Subscribe to theme changes
        ThemeService.ThemeChanged += OnThemeChanged;

        // Open initial file or create empty tab. Fire-and-forget, same as every other VFS read in
        // this app kicked off from a synchronous UI entry point (e.g. OnArchiveEntered) - the
        // window shows with an empty tab for the instant it takes OpenFileCoreAsync's existence
        // check to resolve, then the real content replaces it, rather than blocking construction
        // on a network round trip the way the old File.Exists check blocked on local disk I/O.
        if (!string.IsNullOrEmpty(path))
            _ = OpenFileCoreAsync(fileSystem ?? new LocalFileSystem(), path);
        else
            NewTab();

        UpdateTitle();

        // Apply syntax highlighting after form is shown
        Shown += (_, _) =>
        {
            var tab = GetCurrentTab();
            if (tab != null && tab.Language != LanguageId.PlainText)
            {
                LogService.Info($"Applying syntax highlighting for {tab.Language}");
                tab.ApplySyntaxHighlighting();
            }
        };
    }

    /// <summary>Convenience overload for every existing call site that only ever opened a local path.</summary>
    public EditorForm(string? path) : this(null, path)
    {
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ApplyTheme();
        foreach (var tab in _tabs)
            tab.ApplyTheme();
    }

    private void BuildToolbar()
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        _toolStrip = new ToolStrip
        {
            Dock = DockStyle.Top,
            ImageScalingSize = new Size(16, 16),
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(4, 2, 4, 2),
            Renderer = new ThemeRenderer()
        };

        // File operations
        _toolStrip.Items.Add(CreateToolButton("New", "newdir", (_, _) => NewTab()));
        _toolStrip.Items.Add(CreateToolButton("Open", "view", (_, _) => OpenFile()));
        _toolStrip.Items.Add(CreateToolButton("Save", "copy", (_, _) => SaveCurrentFile()));
        _toolStrip.Items.Add(CreateToolButton("SaveAll", "copy", (_, _) => SaveAllFiles()));
        _toolStrip.Items.Add(new ToolStripSeparator());

        // Edit operations
        _toolStrip.Items.Add(CreateToolButton("Undo", "undo", (_, _) => GetCurrentTab()?.Editor.Undo()));
        _toolStrip.Items.Add(CreateToolButton("Redo", "redo", (_, _) => GetCurrentTab()?.Editor.Redo()));
        _toolStrip.Items.Add(new ToolStripSeparator());
        _toolStrip.Items.Add(CreateToolButton("Cut", "cut", (_, _) => GetCurrentTab()?.Editor.Cut()));
        _toolStrip.Items.Add(CreateToolButton("Copy", "copy", (_, _) => GetCurrentTab()?.Editor.Copy()));
        _toolStrip.Items.Add(CreateToolButton("Paste", "paste", (_, _) => GetCurrentTab()?.Editor.Paste()));
        _toolStrip.Items.Add(new ToolStripSeparator());

        // Find & Replace
        _toolStrip.Items.Add(CreateToolButton("Find", "search", (_, _) => ShowFind()));
        _toolStrip.Items.Add(CreateToolButton("Replace", "rename", (_, _) => ShowReplace()));
        _toolStrip.Items.Add(new ToolStripSeparator());

        // View options
        var wordWrapBtn = new ToolStripButton(L.GetString("Edit.WordWrap"))
        {
            CheckOnClick = true,
            Checked = false
        };
        wordWrapBtn.Click += (_, _) =>
        {
            foreach (var tab in _tabs)
                tab.Editor.WordWrap = wordWrapBtn.Checked;
        };
        _toolStrip.Items.Add(wordWrapBtn);

        var showWhitespaceBtn = new ToolStripButton(L.GetString("Edit.ShowWhitespace"))
        {
            CheckOnClick = true,
            Checked = false
        };
        showWhitespaceBtn.Click += (_, _) =>
        {
            foreach (var tab in _tabs)
                tab.Editor.ShowWhitespace = showWhitespaceBtn.Checked;
        };
        _toolStrip.Items.Add(showWhitespaceBtn);

        Controls.Add(_toolStrip);
    }

    private ToolStripButton CreateToolButton(string textKey, string iconKey, EventHandler onClick)
    {
        var L = LocalizationService.Current;
        var fullKey = $"Edit.Toolbar.{textKey}";
        var displayText = L.GetString(fullKey);
        
        LogService.Info($"CreateToolButton: key={fullKey}, text={displayText}");
        
        var btn = new ToolStripButton(displayText, ToolbarIcons.Get(iconKey))
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ToolTipText = displayText
        };
        btn.Click += onClick;
        return btn;
    }

    private void BuildTabControl()
    {
        _tabControl = new ThemedTabControl
        {
            Dock = DockStyle.Fill
        };
        _tabControl.SelectedIndexChanged += OnTabChanged;
        _tabControl.TabRightClicked += OnTabRightClicked;

        Controls.Add(_tabControl);
    }

    private void OnTabRightClicked(object? sender, int tabIndex)
    {
        if (tabIndex < 0 || tabIndex >= _tabPages.Count) return;

        var L = LocalizationService.Current;
        var p = ThemeService.Current;
        // Built fresh on every right-click and never stored - self-disposes once closed (via
        // Closed below) instead of leaking a ContextMenuStrip per click. The analyzer can't trace
        // disposal happening inside the control's own event handler.
#pragma warning disable CA2000
        var ctx = new ContextMenuStrip
        {
            Renderer = new ThemeRenderer(),
            BackColor = p.HeaderBackground,
            ForeColor = p.Foreground,
            Font = p.GridFont
        };
#pragma warning restore CA2000
        ctx.Closed += (_, _) => ctx.Dispose();

        var closeItem = new ToolStripMenuItem(L.GetString("Edit.TabClose"))
        {
            ForeColor = p.Foreground
        };
        closeItem.Click += (_, _) =>
        {
            _tabControl.SelectedIndex = tabIndex;
            CloseCurrentTab();
        };
        ctx.Items.Add(closeItem);

        var closeOthersItem = new ToolStripMenuItem(L.GetString("Edit.TabCloseOthers"))
        {
            ForeColor = p.Foreground
        };
        closeOthersItem.Click += (_, _) => CloseOtherTabs(tabIndex);
        ctx.Items.Add(closeOthersItem);

        if (_tabPages.Count > 1)
        {
            var closeAllItem = new ToolStripMenuItem(L.GetString("Edit.TabCloseAll"))
            {
                ForeColor = p.Foreground
            };
            closeAllItem.Click += (_, _) => CloseAllTabs();
            ctx.Items.Add(closeAllItem);
        }

        ctx.Show(Cursor.Position);
    }

    private void CloseOtherTabs(int keepIndex)
    {
        var L = LocalizationService.Current;
        var unsaved = _tabs.Where((t, i) => i != keepIndex && t.IsModified).ToList();
        if (unsaved.Count > 0)
        {
            var result = StyledMessageBox.Show(
                L.GetString("Edit.UnsavedChangesAll", unsaved.Count),
                L.GetString("Common.Confirm"),
                MsgBoxButtons.YesNo,
                MsgBoxIcon.Question);
            if (result != MsgBoxResult.Yes) return;
        }

        for (int i = _tabs.Count - 1; i >= 0; i--)
        {
            if (i == keepIndex) continue;
            _tabs[i].Dispose();
            _tabControl.RemovePage(_tabPages[i]);
            _tabs.RemoveAt(i);
            _tabPages.RemoveAt(i);
        }
        _tabControl.SelectedIndex = 0;
        UpdateTitle();
        UpdateStatusBar();
        UpdateFileSizeLabel();
    }

    private void CloseAllTabs()
    {
        var L = LocalizationService.Current;
        var unsaved = _tabs.Where(t => t.IsModified).ToList();
        if (unsaved.Count > 0)
        {
            var result = StyledMessageBox.Show(
                L.GetString("Edit.UnsavedChangesAll", unsaved.Count),
                L.GetString("Common.Confirm"),
                MsgBoxButtons.YesNo,
                MsgBoxIcon.Question);
            if (result != MsgBoxResult.Yes) return;
        }

        foreach (var tab in _tabs)
            tab.Dispose();
        _tabs.Clear();
        _tabControl.ClearPages();
        _tabPages.Clear();
        NewTab();
        UpdateTitle();
        UpdateStatusBar();
        UpdateFileSizeLabel();
    }

    private void BuildStatusBar()
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        _statusStrip = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            SizingGrip = true,
            Renderer = new ThemeRenderer()
        };

        _lblModified = new ToolStripStatusLabel
        {
            Text = "",
            ForeColor = p.Accent,
            Margin = new Padding(4, 0, 8, 0)
        };

        _lblPosition = new ToolStripStatusLabel
        {
            Text = L.GetString("Edit.StatusPosition", 1, 1),
            ForeColor = p.DimForeground,
            Margin = new Padding(4, 0, 8, 0)
        };

        _lblLanguage = new ToolStripStatusLabel
        {
            Text = LanguageDetector.GetDisplayName(LanguageId.PlainText),
            ForeColor = p.DimForeground,
            Margin = new Padding(4, 0, 8, 0)
        };

        _lblEncoding = new ToolStripStatusLabel
        {
            Text = "UTF-8",
            ForeColor = p.DimForeground,
            Margin = new Padding(4, 0, 8, 0)
        };

        _lblFileSize = new ToolStripStatusLabel
        {
            Text = "0 bytes",
            ForeColor = p.DimForeground,
            Margin = new Padding(4, 0, 8, 0)
        };

        _statusStrip.Items.AddRange(new ToolStripItem[]
        {
            _lblModified,
            new ToolStripSeparator(),
            _lblPosition,
            new ToolStripSeparator(),
            _lblLanguage,
            new ToolStripSeparator(),
            _lblEncoding,
            new ToolStripSeparator(),
            _lblFileSize
        });

        Controls.Add(_statusStrip);
    }

    private EditorTab? GetCurrentTab()
    {
        var idx = _tabControl.SelectedIndex;
        if (idx < 0 || idx >= _tabs.Count)
            return null;
        return _tabs[idx];
    }

    private void NewTab()
    {
        var tab = new EditorTab();
        _tabs.Add(tab);

        var tabPage = new ThemedTabPage(tab.DisplayName, tab.Editor);
        _tabPages.Add(tabPage);
        _tabControl.AddPage(tabPage);
        _tabControl.SelectedIndex = _tabControl.Pages.Count - 1;

        tab.Editor.TextChanged += (_, _) => OnTabContentChanged(tab);
        tab.Editor.SelectionChanged += (_, _) => UpdateStatusBar();

        UpdateTitle();
    }

    /// <summary>File&gt;Open / toolbar Open - always a local path, since
    /// <see cref="OpenFileDialog"/> can't produce anything else.</summary>
    private void OpenFile(string? path = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "All files (*.*)|*.*|Text files (*.txt)|*.txt|Source files (*.cs;*.cpp;*.h;*.java;*.js;*.ts;*.py)|*.cs;*.cpp;*.h;*.java;*.js;*.ts;*.py",
                Multiselect = true
            };

            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

            foreach (var file in dlg.FileNames)
                OpenFile(file);
            return;
        }

        _ = OpenFileCoreAsync(new LocalFileSystem(), path);
    }

    /// <summary>Shared by the constructor's initial file and every subsequent File&gt;Open -
    /// reads through <paramref name="fs"/> (see <see cref="EditorTab"/>'s own doc comment for why),
    /// so this is what makes F4 work on a file inside an archive or on a connection instead of the
    /// old <c>File.Exists</c> check silently failing into a blank untitled tab.</summary>
    private async Task OpenFileCoreAsync(IFileSystem fs, string path)
    {
        try
        {
            var L = LocalizationService.Current;

            if (!await fs.ExistsAsync(path).ConfigureAwait(true))
            {
                StyledMessageBox.Show(L.GetString("Err.PathNotFound", path),
                    L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error);
                if (_tabs.Count == 0)
                    NewTab();
                return;
            }

            var info = await fs.GetFileInfoAsync(path).ConfigureAwait(true);
            var fileSize = info?.Size ?? 0;
            if (fileSize > LargeFileConfirmBytes)
            {
                var confirmed = StyledMessageBox.Show(
                    L.GetString("Edit.ConfirmLargeFile", FormatUtils.FormatSize(fileSize), FormatUtils.FormatSize(LargeFileConfirmBytes)),
                    L.GetString("Common.Confirm"), MsgBoxButtons.YesNo, MsgBoxIcon.Warning, this) == MsgBoxResult.Yes;
                if (!confirmed)
                {
                    if (_tabs.Count == 0) NewTab();
                    return;
                }
            }

            EditorTab? tab = null;
            try
            {
                tab = new EditorTab(fs, path);
                await tab.LoadFileAsync(path).ConfigureAwait(true);
                _tabs.Add(tab);

                var tabPage = new ThemedTabPage(tab.DisplayName, tab.Editor);
                _tabPages.Add(tabPage);
                _tabControl.AddPage(tabPage);
                _tabControl.SelectedIndex = _tabControl.Pages.Count - 1;

                var editorTab = tab;
                tab = null;
                editorTab.Editor.TextChanged += (_, _) => OnTabContentChanged(editorTab);
                editorTab.Editor.SelectionChanged += (_, _) => UpdateStatusBar();
            }
            finally
            {
                tab?.Dispose();
            }

            UpdateTitle();
            UpdateStatusBar();
            UpdateFileSizeLabel();
        }
        catch (Exception ex)
        {
            LogService.Error($"OpenFileCoreAsync failed: {ex.Message}", ex);
        }
    }

    private void SaveCurrentFile()
    {
        var tab = GetCurrentTab();
        if (tab == null) return;

        if (string.IsNullOrEmpty(tab.FilePath))
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "All files (*.*)|*.*|Text files (*.txt)|*.txt"
            };

            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

            tab.FilePath = dlg.FileName;
        }

        _ = SaveTabAsync(tab);
    }

    private async Task SaveTabAsync(EditorTab tab)
    {
        try
        {
            await tab.SaveFileAsync().ConfigureAwait(true);
            UpdateTabTitle(tab);
            UpdateTitle();
            UpdateStatusBar();
            UpdateFileSizeLabel();
        }
        catch (Exception ex)
        {
            LogService.Error($"SaveTabAsync failed: {ex.Message}", ex);
        }
    }

    private void SaveAllFiles()
    {
        _ = SaveAllFilesAsync();
    }

    private async Task SaveAllFilesAsync()
    {
        try
        {
            foreach (var tab in _tabs.Where(t => t.IsModified))
            {
                if (string.IsNullOrEmpty(tab.FilePath))
                {
                    using var dlg = new SaveFileDialog
                    {
                        Filter = "All files (*.*)|*.*|Text files (*.txt)|*.txt"
                    };

                    if (dlg.ShowDialog(this) != DialogResult.OK)
                        continue;

                    tab.FilePath = dlg.FileName;
                }

                await tab.SaveFileAsync().ConfigureAwait(true);
                UpdateTabTitle(tab);
            }

            UpdateTitle();
        }
        catch (Exception ex)
        {
            LogService.Error($"SaveAllFilesAsync failed: {ex.Message}", ex);
        }
    }

    private void OnTabContentChanged(EditorTab tab)
    {
        UpdateTabTitle(tab);
        UpdateTitle();
        if (tab == GetCurrentTab())
            UpdateFileSizeLabel();
    }

    private void OnTabChanged(object? sender, EventArgs e)
    {
        UpdateStatusBar();
        UpdateTitle();
        UpdateFileSizeLabel();
    }

    private void UpdateTabTitle(EditorTab tab)
    {
        var idx = _tabs.IndexOf(tab);
        if (idx >= 0 && idx < _tabPages.Count)
        {
            _tabPages[idx].Text = tab.DisplayName;
            _tabPages[idx].RefreshTab();
        }
    }

    private void UpdateTitle()
    {
        var L = LocalizationService.Current;
        var tab = GetCurrentTab();
        var fileName = tab?.FileName ?? L.GetString("Edit.NewFile");
        var modified = tab?.IsModified == true ? " *" : "";
        Text = $"{fileName}{modified} - {L.GetString("Edit.Title")}";
    }

    /// <summary>
    /// Cheap, O(1) status bar fields — safe to refresh on every caret move (i.e. every keystroke).
    /// File size is deliberately not here: see <see cref="UpdateFileSizeLabel"/>.
    /// </summary>
    private void UpdateStatusBar()
    {
        var L = LocalizationService.Current;
        var tab = GetCurrentTab();
        if (tab == null) return;

        var (line, col) = tab.GetCursorPosition();
        _lblPosition.Text = L.GetString("Edit.StatusPosition", line, col);
        _lblLanguage.Text = LanguageDetector.GetDisplayName(tab.Language);
        _lblEncoding.Text = tab.Encoding.EncodingName;
        _lblModified.Text = tab.IsModified ? L.GetString("Edit.Modified") : "";
    }

    /// <summary>
    /// Refreshes the file-size label. Deliberately separate from <see cref="UpdateStatusBar"/>: for a
    /// saved file this hits disk (File.Exists + FileInfo.Length), and for an unsaved one it walks the
    /// whole buffer to get its length (TextLength is O(document)) — neither belongs on the caret-move
    /// path that fires on every single keystroke.
    /// </summary>
    private void UpdateFileSizeLabel()
    {
        var L = LocalizationService.Current;
        var tab = GetCurrentTab();
        if (tab == null) return;

        if (!string.IsNullOrEmpty(tab.FilePath))
        {
            // Fire-and-forget: the UI must not block on a remote (SFTP/FTP/WebDAV) round-trip
            // just to update a size label. The result is marshaled back to the UI thread.
            _ = UpdateFileSizeLabelAsync(tab, L);
        }
        else
        {
            _lblFileSize.Text = $"{tab.Editor.TextLength} {L.GetString("Edit.Bytes")}";
        }
    }

    /// <summary>Async counterpart of <see cref="UpdateFileSizeLabel"/> — queries the file size
    /// without blocking the UI thread, then marshals the result back via BeginInvoke.</summary>
    private async Task UpdateFileSizeLabelAsync(EditorTab tab, LocalizationService L)
    {
        try
        {
            var info = await tab.FileSystem.GetFileInfoAsync(tab.FilePath, CancellationToken.None).ConfigureAwait(false);
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                if (info != null)
                {
                    var size = info.Size;
                    _lblFileSize.Text = size > 1024 ? $"{size / 1024} {L.GetString("Edit.KB")}" : $"{size} {L.GetString("Edit.Bytes")}";
                }
                else
                {
                    _lblFileSize.Text = "";
                }
            });
        }
        catch
        {
            if (IsDisposed) return;
            BeginInvoke(() => _lblFileSize.Text = "");
        }
    }

    private void ShowFind()
    {
        // Stub until the find/replace-bar milestone lands; kept as its own method so the
        // toolbar/hotkey call sites don't need to change shape again later.
        GetCurrentTab()?.Editor.ShowFindBar(withReplace: false);
    }

    private void ShowReplace()
    {
        GetCurrentTab()?.Editor.ShowFindBar(withReplace: true);
    }

    /// <summary>
    /// Global editor shortcuts, intercepted here so they work no matter which child control has
    /// focus (the canvas, the gutter, or a textbox inside the find bar) — the previous approach
    /// (a KeyDown handler never actually wired to any control's event) never fired at all.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolStrip?.Dispose();
            _tabControl?.Dispose();
            _statusStrip?.Dispose();
            _lblEncoding?.Dispose();
            _lblFileSize?.Dispose();
            _lblLanguage?.Dispose();
            _lblModified?.Dispose();
            _lblPosition?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.Control) == Keys.Control && (keyData & Keys.Alt) == 0)
        {
            switch (keyData & ~Keys.Control & ~Keys.Shift)
            {
                case Keys.S: SaveCurrentFile(); return true;
                case Keys.N: NewTab(); return true;
                case Keys.O: OpenFile(); return true;
                case Keys.W: CloseCurrentTab(); return true;
                case Keys.F: ShowFind(); return true;
                case Keys.H: ShowReplace(); return true;
                case Keys.G: ShowGoToLine(); return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ShowGoToLine()
    {
        var tab = GetCurrentTab();
        if (tab == null) return;
        var L = LocalizationService.Current;
        using var dlg = new InputDialogForm(L.GetString("Edit.GoToLine"), L.GetString("Edit.GoToLinePrompt"), "");
        if (dlg.ShowDialog(this) == DialogResult.OK && int.TryParse(dlg.Value, out var line))
            tab.Editor.GoToLine(line);
    }

    /// <summary>
    /// <c>async void</c> - the established pattern this codebase already uses for a UI event
    /// handler that needs to await (e.g. <c>MainForm.OnArchiveEntered</c>); every caller of this
    /// method (toolbar button, Ctrl+W, a tab's close glyph) already treats it as a fire-and-forget
    /// void action, so the signature change is source-compatible.
    ///
    /// <para>Deliberately NOT <c>tab.SaveFileAsync().GetAwaiter().GetResult()</c>, despite
    /// <c>SaveFileAsync</c> using <c>ConfigureAwait(false)</c> throughout: that looks safe (no
    /// particular thread is needed to resume on) but isn't - blocking this thread while its own
    /// continuation still needs a free thread-pool worker to run on is a real deadlock under a
    /// small enough pool, reproduced directly while building <see cref="PanelViewModel.ReleaseArchiveLeaseAsync"/>'s
    /// equivalent call. A plain <c>await</c> here has no such risk.</para>
    /// </summary>
    private async void CloseCurrentTab()
    {
        try
        {
            var L = LocalizationService.Current;
            var tab = GetCurrentTab();
            if (tab == null) return;

            if (tab.IsModified)
            {
                var result = StyledMessageBox.Show(
                    L.GetString("Edit.UnsavedChanges", tab.FileName),
                    L.GetString("Common.Confirm"),
                    MsgBoxButtons.YesNoCancel,
                    MsgBoxIcon.Question);

                if (result == MsgBoxResult.Cancel)
                    return;
                if (result == MsgBoxResult.Yes && !await tab.SaveFileAsync())
                    return;
            }

            var idx = _tabs.IndexOf(tab);
            if (idx < 0) return;

            _tabs.RemoveAt(idx);
            _tabControl.RemovePage(_tabPages[idx]);
            _tabPages.RemoveAt(idx);
            tab.Dispose();

            if (_tabs.Count == 0)
                NewTab();

            UpdateTitle();
            UpdateStatusBar();
            UpdateFileSizeLabel();
        }
        catch (Exception ex)
        {
            LogService.Error($"CloseCurrentTab failed: {ex.Message}", ex);
        }
    }

    /// <summary>Set once <see cref="ConfirmAndCloseAsync"/> has resolved every unsaved tab (saved
    /// or the user accepted losing it) and is re-issuing <see cref="Close"/> - lets the next
    /// <see cref="OnFormClosing"/> re-entry tell "already resolved, let it close" apart from a
    /// fresh close attempt that still needs to ask.</summary>
    private bool _closeConfirmed;

    /// <summary>
    /// <see cref="FormClosingEventArgs"/> offers no way to await inside this override - the classic
    /// WinForms async-close problem. Solved the standard way: cancel this attempt immediately if
    /// there is unsaved work, resolve the save/discard decision asynchronously in
    /// <see cref="ConfirmAndCloseAsync"/>, then call <see cref="Close"/> again once resolved - which
    /// re-enters this method, and <see cref="_closeConfirmed"/> is what lets that second entry
    /// proceed instead of asking all over again.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        if (_closeConfirmed || !_tabs.Any(t => t.IsModified))
        {
            // Only unsubscribe once we're actually going through with the close - a cancelled
            // attempt (Cancel button, below) must leave this window still reacting to theme changes.
            ThemeService.ThemeChanged -= OnThemeChanged;
            foreach (var tab in _tabs)
                tab.Dispose();
            return;
        }

        e.Cancel = true;
        if (_closingInProgress) return; // a second X-click while a prior save is still in flight
        _ = ConfirmAndCloseGuardedAsync();
    }

    /// <summary>Guards <see cref="ConfirmAndCloseAsync"/> against re-entrancy: without it, clicking
    /// the window's X a second time while an earlier save is still uploading re-runs the whole loop
    /// concurrently - duplicate confirmation dialogs for the same tab and, worse, two overlapping
    /// <c>SaveFileAsync</c> calls racing their own sidecar-then-rename uploads for the same
    /// file.</summary>
    private bool _closingInProgress;

    private async Task ConfirmAndCloseGuardedAsync()
    {
        _closingInProgress = true;
        try
        {
            await ConfirmAndCloseAsync();
        }
        catch (Exception ex)
        {
            LogService.Error($"ConfirmAndCloseGuardedAsync failed: {ex.Message}", ex);
        }
        finally
        {
            _closingInProgress = false;
        }
    }

    private async Task ConfirmAndCloseAsync()
    {
        var L = LocalizationService.Current;

        foreach (var tab in _tabs.Where(t => t.IsModified))
        {
            var result = StyledMessageBox.Show(
                L.GetString("Edit.UnsavedChanges", tab.FileName),
                L.GetString("Common.Confirm"),
                MsgBoxButtons.YesNoCancel,
                MsgBoxIcon.Question);

            if (result == MsgBoxResult.Cancel)
                return; // leave the window open - OnFormClosing already cancelled this attempt

            // A failed save must not let the window close anyway - SaveFileAsync already reported
            // the error; stop here so the tab (and the window) stays open with the buffer intact,
            // exactly as if the user had pressed Cancel.
            if (result == MsgBoxResult.Yes && !await tab.SaveFileAsync())
                return;
        }

        _closeConfirmed = true;
        Close();
    }
}
