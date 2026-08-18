using CoderCommander.Services;
using System.Drawing.Drawing2D;

namespace CoderCommander.WinForms;

/// <summary>
/// Fully owner-drawn tab control with theme support.
/// Replaces WinForms TabControl whose system-drawn borders stay light in dark mode.
/// </summary>
public sealed class ThemedTabControl : UserControl, ISelfThemedControl
{
    /// <summary>Side of the square hover pill behind a tab's close glyph.</summary>
    private const int CloseGlyphBox = 16;
    /// <summary>Gap between the close glyph's box and the tab button's right edge.</summary>
    private const int CloseGlyphRightMargin = 6;
    /// <summary>Horizontal room a close glyph claims on the right of a tab button, so the tab's
    /// own text is laid out inside what's left rather than running underneath it.</summary>
    private const int CloseGlyphReservedWidth = CloseGlyphBox + CloseGlyphRightMargin;

    /// <summary>Where a tab button of <paramref name="buttonSize"/> puts its close glyph. Exposed
    /// (rather than left inline in the paint/hit-test code) so a test can assert the box stays
    /// inside the button - an earlier close button drawn with a glyph font overflowed its bounds.</summary>
    internal static Rectangle CloseGlyphBoxFor(Size buttonSize) => new(
        buttonSize.Width - CloseGlyphBox - CloseGlyphRightMargin,
        (buttonSize.Height - CloseGlyphBox) / 2,
        CloseGlyphBox,
        CloseGlyphBox);

    /// <summary>Radius of the busy/idle indicator dot drawn at the left edge of a tab button.</summary>
    private const int IndicatorDotRadius = 3;
    /// <summary>Left inset of the indicator dot's centre from the tab button's left edge.</summary>
    private const int IndicatorDotLeftInset = 8;

    private readonly List<ThemedTabPage> _pages = new();
    private readonly Panel _buttonPanel;
    private readonly Panel _contentPanel;
    private readonly ToolTip _closeButtonTip = new();
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

    /// <summary>Unsubscribes from the <see cref="ThemeService.ThemeChanged"/> event and disposes
    /// every page's content - not just the selected one. Only the selected page's Content is ever
    /// parented into _contentPanel.Controls (see UpdateTabs()), so base.Dispose()'s recursive walk
    /// alone would leak the content of every other page. Control.Dispose() is idempotent, so
    /// disposing the (already-parented) selected page's content here too is harmless.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeService.ThemeChanged -= OnThemeChanged;
            _closeButtonTip.Dispose();
            foreach (var page in _pages)
                page.Content.Dispose();
        }
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
    /// When true, every tab button gets a small "x" close button of its own. Off by default so
    /// existing callers (EditorForm, ViewerForm, SettingsForm - none of which expose per-tab
    /// closing this way) are unaffected; opt in per instance.
    /// </summary>
    public bool ShowCloseButtons { get; set; }

    /// <summary>Tooltip text shown when hovering a tab's close button. Ignored if
    /// <see cref="ShowCloseButtons"/> is false.</summary>
    public string? CloseButtonTooltip { get; set; }

    /// <summary>
    /// Raised when a tab's close ("x") button is clicked. EventArgs = tab index. Only fires when
    /// <see cref="ShowCloseButtons"/> is true. The subscriber decides whether/how the tab actually
    /// closes (e.g. tearing down a session) - this control only reports the click.
    /// </summary>
    public event EventHandler<int>? TabCloseClicked;

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

    /// <summary>Rebuilds the tab button strip from scratch - e.g. after
    /// <see cref="CloseButtonTooltip"/> changes (a live language switch) and existing close
    /// buttons' tooltips need to pick up the new text.</summary>
    public void RefreshTabStrip() => RebuildButtons();

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

    /// <summary>Updates only the busy/idle indicator dot on the specified page's tab button,
    /// without rebuilding the entire button strip. Called frequently (every OSC 133 transition),
    /// so a full <see cref="RebuildButtons"/> would be wasteful and would also reset hover state.</summary>
    internal void UpdateTabIndicator(ThemedTabPage page, bool busy)
    {
        var idx = _pages.IndexOf(page);
        if (idx < 0) return;
        if (_buttonPanel.Controls.Count <= idx) return;
        if (_buttonPanel.Controls[idx] is not TabButton btn) return;
        btn.Busy = busy;
        btn.Invalidate();
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
            var textWidth = TextRenderer.MeasureText(page.Text, p.GridFont).Width;

            var btn = new TabButton
            {
                Text = page.Text,
                Height = 32,
                Width = textWidth + 32 + (ShowCloseButtons ? CloseGlyphReservedWidth : 0),
                FlatStyle = FlatStyle.Flat,
                Font = p.GridFont,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 2, 0),
                Tag = index,
                CornerRadius = 0,
                UseGradient = false,
                DrawShadow = false,
                ShowClose = ShowCloseButtons,
                CloseTooltip = CloseButtonTooltip,
                CloseTooltipHost = _closeButtonTip,
                Busy = page.Busy,
                HasShellIntegration = page.HasShellIntegration
            };
            btn.Click += (_, _) => SelectedIndex = index;
            btn.RightClick += (_, _) => TabRightClicked?.Invoke(this, index);
            btn.CloseClicked += (_, _) => TabCloseClicked?.Invoke(this, index);
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
            if (c is not TabButton btn || btn.Tag is not int idx) continue;

            var selected = idx == _selectedIndex;
            btn.BackColor = selected ? p.PanelBackground : p.Background;
            btn.ForeColor = selected ? p.Foreground : p.DimForeground;
            btn.HoverColor = p.ToolbarHover;
            btn.PressedColor = p.ToolbarHover;
            btn.BorderColor = selected ? p.Accent : p.GridLine;
            btn.BorderWidth = 1;
            btn.CloseGlyphColor = selected ? p.Foreground : p.DimForeground;
            btn.CloseGlyphHoverColor = p.Danger;
            btn.CloseHoverFill = p.ToolbarHover;
            // A tab with a close glyph keeps the same 12px text inset on the left, but its right
            // inset also has to clear the glyph - otherwise a long title runs straight under it.
            btn.Padding = new Padding(12, 0, btn.ShowClose ? CloseGlyphReservedWidth : 12, 0);
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

    /// <summary>
    /// One tab's button, with an optional close ("x") glyph drawn <em>inside its own</em> paint
    /// pass rather than as a child control on top. That distinction is the whole point of this
    /// class: <see cref="RoundedButton.OnPaint"/> starts by filling its entire
    /// <c>ClientRectangle</c> with the parent's background to avoid transparent-corner artifacts,
    /// so a close button parented over a tab button punches an opaque rectangle through the tab's
    /// rounded fill and border. Drawing the glyph here keeps a single, artifact-free surface, and
    /// sizing it in code (two lines in a fixed box) means it can't overflow the way a glyph-font
    /// character in a 16px button did.
    /// </summary>
    private sealed class TabButton : RoundedButton
    {
        private bool _closeHot;
        private bool _closeArmed;
        private bool _suppressClick;
        private bool _tooltipAttached;

        /// <summary>Whether to draw (and hit-test) the close glyph at all.</summary>
        public bool ShowClose { get; init; }

        /// <summary>Whether this tab's shell is busy (command running). Draws a small coloured dot
        /// at the left edge of the button — green-ish when idle (not drawn), amber when busy. The
        /// indicator is suppressed entirely when <see cref="HasShellIntegration"/> is false, since
        /// "busy" has no meaning for a shell that never reports its prompt state.</summary>
        public bool Busy { get; set; }

        /// <summary>Whether the tab's shell supports shell-integration (OSC 133). When false, the
        /// busy indicator is never drawn — there's no reliable signal to show.</summary>
        public bool HasShellIntegration { get; set; }

        /// <summary>Tooltip shown while the pointer is over the close glyph specifically - attached
        /// and detached on the fly, since a tooltip set on the whole button would also pop when
        /// hovering the tab's title.</summary>
        public string? CloseTooltip { get; init; }

        /// <summary>The owning control's shared <see cref="ToolTip"/> component.</summary>
        public ToolTip? CloseTooltipHost { get; init; }

        public Color CloseGlyphColor { get; set; } = Color.Empty;
        public Color CloseGlyphHoverColor { get; set; } = Color.Empty;
        public Color CloseHoverFill { get; set; } = Color.Empty;

        /// <summary>Raised when the close glyph itself is clicked (not the rest of the tab).</summary>
        public event EventHandler? CloseClicked;

        private Rectangle CloseBox => CloseGlyphBoxFor(Size);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Busy indicator dot — only for shell-integration-aware tabs (terminal tabs), and only
            // when a command is actually running. Idle tabs draw nothing (clean look, less noise).
            if (HasShellIntegration && Busy)
            {
                var cx = IndicatorDotLeftInset;
                var cy = Height / 2;
                using var dotBrush = new SolidBrush(ThemeService.Current.Warning);
                g.FillEllipse(dotBrush, cx - IndicatorDotRadius, cy - IndicatorDotRadius,
                    IndicatorDotRadius * 2, IndicatorDotRadius * 2);
            }

            if (!ShowClose) return;
            var box = CloseBox;

            if (_closeHot && CloseHoverFill != Color.Empty)
            {
                using var fill = new SolidBrush(CloseHoverFill);
                using var pill = GraphicsHelpers.GetRoundedRect(box, 3);
                g.FillPath(fill, pill);
            }

            var glyphColor = _closeHot
                ? (CloseGlyphHoverColor != Color.Empty ? CloseGlyphHoverColor : ForeColor)
                : (CloseGlyphColor != Color.Empty ? CloseGlyphColor : ForeColor);

            // Two diagonals in a fixed inset box - no font metrics involved, so the glyph is the
            // same crisp size at any theme font and can never overflow its box.
            const int inset = 5;
            using var pen = new Pen(glyphColor, 1.4f);
            g.DrawLine(pen, box.Left + inset, box.Top + inset, box.Right - inset, box.Bottom - inset);
            g.DrawLine(pen, box.Right - inset, box.Top + inset, box.Left + inset, box.Bottom - inset);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!ShowClose) return;

            var hot = CloseBox.Contains(e.Location);
            if (hot == _closeHot) return;

            _closeHot = hot;
            UpdateTooltipAttachment();
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_closeHot && !_closeArmed) return;

            _closeHot = false;
            _closeArmed = false;
            UpdateTooltipAttachment();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _closeArmed = ShowClose && e.Button == MouseButtons.Left && CloseBox.Contains(e.Location);
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            // Fire (and swallow the tab-select Click that base.OnMouseUp is about to raise) only
            // if the press *and* the release both landed on the glyph - matching how every other
            // button in the app behaves when you press it and drag off before releasing.
            var closeHit = _closeArmed && CloseBox.Contains(e.Location);
            _closeArmed = false;
            _suppressClick = closeHit;

            base.OnMouseUp(e);

            if (closeHit)
                CloseClicked?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnClick(EventArgs e)
        {
            if (_suppressClick)
            {
                _suppressClick = false;
                return;
            }
            base.OnClick(e);
        }

        private void UpdateTooltipAttachment()
        {
            if (CloseTooltipHost == null || string.IsNullOrEmpty(CloseTooltip)) return;
            if (_closeHot == _tooltipAttached) return;

            _tooltipAttached = _closeHot;
            CloseTooltipHost.SetToolTip(this, _closeHot ? CloseTooltip : null);
        }
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

    /// <summary>Whether the tab's shell is busy (command running). Updated frequently via
    /// <see cref="ThemedTabControl.UpdateTabIndicator"/> without rebuilding the button strip.</summary>
    public bool Busy { get; set; }

    /// <summary>Whether the tab's shell supports shell-integration (OSC 133). When false, the busy
    /// indicator dot is never drawn.</summary>
    public bool HasShellIntegration { get; set; }

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
