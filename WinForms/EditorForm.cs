using CoderCommander.Services;
using System.Text;

namespace CoderCommander.WinForms;

/// <summary>
/// Professional tabbed code editor with syntax highlighting, toolbar, and status bar.
/// </summary>
public class EditorForm : ThemedForm
{
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

    public EditorForm(string? path)
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

        // Open initial file or create empty tab
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            OpenFile(path);
        }
        else
        {
            NewTab();
        }

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
        var ctx = new ContextMenuStrip
        {
            Renderer = new ThemeRenderer(),
            BackColor = p.HeaderBackground,
            ForeColor = p.Foreground,
            Font = p.GridFont
        };

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

    private void OpenFile(string? path = null)
    {
        var L = LocalizationService.Current;
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

        if (!File.Exists(path))
        {
            StyledMessageBox.Show(L.GetString("Err.PathNotFound", path),
                L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error);
            return;
        }

        var tab = new EditorTab(path);
        tab.LoadFile(path);
        _tabs.Add(tab);

        var tabPage = new ThemedTabPage(tab.DisplayName, tab.Editor);
        _tabPages.Add(tabPage);
        _tabControl.AddPage(tabPage);
        _tabControl.SelectedIndex = _tabControl.Pages.Count - 1;

        tab.Editor.TextChanged += (_, _) => OnTabContentChanged(tab);
        tab.Editor.SelectionChanged += (_, _) => UpdateStatusBar();

        UpdateTitle();
        UpdateStatusBar();
        UpdateFileSizeLabel();
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

        tab.SaveFile();
        UpdateTabTitle(tab);
        UpdateTitle();
        UpdateStatusBar();
        UpdateFileSizeLabel();
    }

    private void SaveAllFiles()
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

            tab.SaveFile();
            UpdateTabTitle(tab);
        }

        UpdateTitle();
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

        if (!string.IsNullOrEmpty(tab.FilePath) && File.Exists(tab.FilePath))
        {
            var size = new FileInfo(tab.FilePath).Length;
            _lblFileSize.Text = size > 1024 ? $"{size / 1024} {L.GetString("Edit.KB")}" : $"{size} {L.GetString("Edit.Bytes")}";
        }
        else
        {
            _lblFileSize.Text = $"{tab.Editor.TextLength} {L.GetString("Edit.Bytes")}";
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

    private void CloseCurrentTab()
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
            if (result == MsgBoxResult.Yes)
            {
                tab.SaveFile();
            }
        }

        var idx = _tabControl.SelectedIndex;
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

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        var L = LocalizationService.Current;

        // Check for unsaved changes
        foreach (var tab in _tabs.Where(t => t.IsModified))
        {
            var result = StyledMessageBox.Show(
                L.GetString("Edit.UnsavedChanges", tab.FileName),
                L.GetString("Common.Confirm"),
                MsgBoxButtons.YesNoCancel,
                MsgBoxIcon.Question);

            if (result == MsgBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (result == MsgBoxResult.Yes)
            {
                tab.SaveFile();
            }
        }

        // Only unsubscribe once we're actually going through with the close - cancelling above
        // must leave this window still reacting to theme changes.
        ThemeService.ThemeChanged -= OnThemeChanged;

        // Dispose all tabs
        foreach (var tab in _tabs)
            tab.Dispose();
    }
}
