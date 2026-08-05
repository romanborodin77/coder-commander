using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Fully owner-drawn tab control with theme support.
/// Replaces WinForms TabControl whose system-drawn borders stay light in dark mode.
/// </summary>
public sealed class ThemedTabControl : UserControl, ISelfThemedControl
{
    private readonly List<ThemedTabPage> _pages = new();
    private readonly Panel _buttonPanel;
    private readonly Panel _contentPanel;
    private int _selectedIndex = -1;
    private Control? _trailingControl;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemedTabControl"/> class with a button strip
    /// and content panel, using the current theme palette.
    /// </summary>
    public ThemedTabControl()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        var p = ThemeService.Current;
        BackColor = p.Background;

        _buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = p.Background,
            Padding = new Padding(0, 0, 0, 1),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = p.PanelBackground,
            Padding = new Padding(1)
        };

        Controls.Add(_contentPanel);
        Controls.Add(_buttonPanel);
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    /// <summary>Handles the <see cref="ThemeService.ThemeChanged"/> event by calling <see cref="RefreshTheme"/>.</summary>
    private void OnThemeChanged(object? sender, EventArgs e) => RefreshTheme();

    /// <summary>Unsubscribes from the <see cref="ThemeService.ThemeChanged"/> event.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ThemeService.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }

    /// <summary>Gets the list of tab pages currently in this control.</summary>
    public IReadOnlyList<ThemedTabPage> Pages => _pages;

    /// <summary>
    /// Gets or sets the index of the currently selected tab page.
    /// Setting this value triggers a visual update and raises <see cref="SelectedIndexChanged"/>.
    /// </summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value || value < 0 || value >= _pages.Count) return;
            _selectedIndex = value;
            UpdateTabs();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gets the currently selected tab page, or <c>null</c> if no tab is selected.</summary>
    public ThemedTabPage? SelectedPage =>
        _selectedIndex >= 0 && _selectedIndex < _pages.Count ? _pages[_selectedIndex] : null;

    /// <summary>Raised when the selected tab page changes.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>
    /// Raised when a tab button is right-clicked. EventArgs = tab index.
    /// </summary>
    public event EventHandler<int>? TabRightClicked;

    /// <summary>
    /// Re-themes the tab chrome (buttons, strip) and, critically, every page's content - not
    /// just the currently selected one. Only the selected page's <see cref="ThemedTabPage.Content"/>
    /// is actually parented into <see cref="_contentPanel"/> at any given time (see
    /// <see cref="UpdateTabs"/>), so a hidden page's content is otherwise unreachable by any
    /// control-tree walk until the user switches to it - which used to mean it kept showing the
    /// theme that was active when the tab was last visible.
    /// </summary>
    public void RefreshTheme()
    {
        UpdateTabs();
        var p = ThemeService.Current;
        foreach (var page in _pages)
        {
            ControlThemer.ThemeSingleControl(page.Content, p);
            if (page.Content is ISelfThemedControl self)
                self.RefreshTheme();
            else if (page.Content.HasChildren)
                ControlThemer.ThemeDescendants(page.Content);
        }
        Invalidate();
    }

    /// <summary>Place a control (e.g. an "add tab" button) right after the last tab button.
    /// Preserved across tab add/remove, which otherwise rebuild the whole button strip.</summary>
    public void SetTrailingControl(Control control)
    {
        _trailingControl = control;
        RebuildButtons();
    }

    /// <summary>Adds a new tab page to the control and selects it if it's the first page.</summary>
    public void AddPage(ThemedTabPage page)
    {
        page.SetParent(this);
        _pages.Add(page);
        if (_selectedIndex < 0)
            _selectedIndex = 0;
        RebuildButtons();
        UpdateTabs();
    }

    /// <summary>Removes a tab page and adjusts the selected index accordingly.</summary>
    public void RemovePage(ThemedTabPage page)
    {
        var idx = _pages.IndexOf(page);
        if (idx < 0) return;
        page.SetParent(null);
        _pages.RemoveAt(idx);
        if (_pages.Count == 0)
            _selectedIndex = -1; // must reset, or the next AddPage's index-0 selection is a silent no-op
        else if (_selectedIndex >= _pages.Count)
            _selectedIndex = _pages.Count - 1;
        RebuildButtons();
        UpdateTabs();
    }

    /// <summary>Refreshes the tab button for the specified page (e.g. after its title changes).</summary>
    internal void RefreshTab(ThemedTabPage page)
    {
        var idx = _pages.IndexOf(page);
        if (idx < 0) return;
        RebuildButtons();
    }

    /// <summary>Removes all tab pages and resets the selection.</summary>
    public void ClearPages()
    {
        _pages.Clear();
        _selectedIndex = -1;
        RebuildButtons();
    }

    /// <summary>Rebuilds the tab button strip from the current page list, preserving the trailing control.</summary>
    private void RebuildButtons()
    {
        // Dispose the previous tab buttons (created fresh below) - the trailing control is owned by
        // the caller and gets re-added every time, so it must survive the rebuild.
        foreach (Control c in _buttonPanel.Controls.Cast<Control>().ToList())
        {
            if (c != _trailingControl)
                c.Dispose();
        }
        _buttonPanel.Controls.Clear();
        var p = ThemeService.Current;

        for (int i = 0; i < _pages.Count; i++)
        {
            var index = i;
            var page = _pages[i];
            var btn = new RoundedButton
            {
                Text = page.Text,
                Height = 32,
                Width = TextRenderer.MeasureText(page.Text, p.GridFont).Width + 32,
                FlatStyle = FlatStyle.Flat,
                Font = p.GridFont,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 2, 0),
                Tag = index,
                CornerRadius = 0,
                UseGradient = false,
                DrawShadow = false
            };
            btn.Click += (_, _) => SelectedIndex = index;
            btn.RightClick += (_, _) => TabRightClicked?.Invoke(this, index);
            _buttonPanel.Controls.Add(btn);
        }

        if (_trailingControl != null)
            _buttonPanel.Controls.Add(_trailingControl);

        UpdateTabs();
    }

    /// <summary>Updates button colors, border styles, and re-parents the selected page's content.</summary>
    private void UpdateTabs()
    {
        var p = ThemeService.Current;
        _buttonPanel.BackColor = p.Background;
        _contentPanel.BackColor = p.PanelBackground;
        BackColor = p.Background;

        foreach (Control c in _buttonPanel.Controls)
        {
            if (c is not RoundedButton btn || btn.Tag is not int idx) continue;

            var selected = idx == _selectedIndex;
            btn.BackColor = selected ? p.PanelBackground : p.Background;
            btn.ForeColor = selected ? p.Foreground : p.DimForeground;
            btn.HoverColor = p.ToolbarHover;
            btn.PressedColor = p.ToolbarHover;
            btn.BorderColor = selected ? p.Accent : p.GridLine;
            btn.BorderWidth = 1;
            btn.Padding = new Padding(12, 0, 12, 0);
            btn.Invalidate();
        }

        // Re-parenting a control (Clear+Add) always drops its keyboard focus, so only do it when
        // the selected page's content actually changed. RefreshTab()/RebuildButtons() run on
        // every tab-title update (e.g. the editor's modified-indicator flips on every keystroke),
        // so without this guard, typing into a hosted editor lost focus after the first character.
        var desiredContent = SelectedPage?.Content;
        var currentContent = _contentPanel.Controls.Count > 0 ? _contentPanel.Controls[0] : null;
        if (!ReferenceEquals(currentContent, desiredContent))
        {
            _contentPanel.Controls.Clear();
            if (desiredContent != null)
            {
                desiredContent.Dock = DockStyle.Fill;
                _contentPanel.Controls.Add(desiredContent);
            }
        }

        _buttonPanel.Invalidate();
        _contentPanel.Invalidate();
    }

    /// <summary>Draws the content border and the separator line between the button strip and content area.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var p = ThemeService.Current;
        var g = e.Graphics;

        // Content border
        var rect = _contentPanel.Bounds;
        using (var borderPen = new Pen(p.GridLine, 1))
            g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);

        // Separator between button strip and content
        var sepY = _buttonPanel.Bottom - 1;
        using (var sepPen = new Pen(p.GridLine, 1))
            g.DrawLine(sepPen, rect.X, sepY, rect.Right - 1, sepY);
    }
}

/// <summary>
/// Tab page for <see cref="ThemedTabControl"/>.
/// </summary>
public sealed class ThemedTabPage
{
    private string _text;
    private ThemedTabControl? _parent;

    /// <summary>Gets or sets the display text for the tab button.</summary>
    public string Text
    {
        get => _text;
        internal set => _text = value;
    }
    /// <summary>Gets the content control displayed when this tab page is selected.</summary>
    public Control Content { get; }

    /// <summary>Sets the parent tab control for this page.</summary>
    internal void SetParent(ThemedTabControl? parent) => _parent = parent;

    /// <summary>Refreshes this tab's button appearance (e.g. after the title text changes).</summary>
    public void RefreshTab() => _parent?.RefreshTab(this);

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemedTabPage"/> class with the specified
    /// display text and content control.
    /// </summary>
    public ThemedTabPage(string text, Control content)
    {
        _text = text;
        Content = content;
    }
}
