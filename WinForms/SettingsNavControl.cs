using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Left-hand section navigator for <see cref="SettingsForm"/> (VS Code-style: a vertical list of
/// section names on the left, the selected section's content filling the rest) - replaces the
/// horizontal <see cref="ThemedTabControl"/> strip that had no room left for a 7th section without
/// wrapping or shrinking each label unreadably.
///
/// <para>Mirrors <see cref="ThemedTabControl"/>'s shape deliberately: <see cref="AddPage"/>/
/// <see cref="SelectedIndex"/>/<see cref="SelectedIndexChanged"/>/<see cref="RefreshTheme"/>, and
/// the same "only the selected page's content is ever parented" contract - the other pages'
/// controls exist and hold their own state (working copies, event subscriptions) but aren't in the
/// visible tree until selected. <see cref="ISelfThemedControl"/> for the same reason
/// <c>ThemedTabControl</c> needs it: <see cref="ControlThemer.ThemeDescendants"/> must defer to
/// this control's own re-theme logic instead of walking (and double-theming, or missing the
/// unparented pages entirely) its children itself.</para>
///
/// <para>Nav items are real <see cref="RoundedButton"/>s with real <see cref="Control.Text"/> -
/// not owner-drawn labels on a canvas - so UIA continues to expose each section by name exactly
/// like <c>ThemedTabControl</c>'s tab buttons did (<c>UiTests/SettingsFormUiTests.cs</c> finds
/// dialog content by walking from a named tab/section).</para>
/// </summary>
public sealed class SettingsNavControl : UserControl, ISelfThemedControl
{
    private const int NavWidth = 176;
    private const int ItemHeight = 34;

    private readonly List<SettingsNavPage> _pages = new();
    private readonly FlowLayoutPanel _navPanel;
    private readonly Panel _contentPanel;
    private int _selectedIndex = -1;

    public SettingsNavControl()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        var p = ThemeService.Current;
        BackColor = p.Background;

        _navPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            Width = NavWidth,
            BackColor = p.PanelBackground,
            Padding = new Padding(6),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };

        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = p.Background,
            Padding = new Padding(1)
        };

        // Dock=Fill must be added before Dock=Left - see WinForms/DirectoryTreeForm.cs for why.
        Controls.Add(_contentPanel);
        Controls.Add(_navPanel);
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => RefreshTheme();

    /// <summary>Unsubscribes from the theme event and disposes every page's content - not just the
    /// selected one, for the same reason <see cref="ThemedTabControl.Dispose"/> does: an
    /// unparented page is unreachable by <c>base.Dispose</c>'s recursive control-tree walk.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeService.ThemeChanged -= OnThemeChanged;
            foreach (var page in _pages)
                page.Content.Dispose();
            _navPanel?.Dispose();
            _contentPanel?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Gets the list of section pages currently in this control.</summary>
    public IReadOnlyList<SettingsNavPage> Pages => _pages;

    /// <summary>Gets or sets the index of the currently selected section.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value || value < 0 || value >= _pages.Count) return;
            _selectedIndex = value;
            UpdateSelection();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gets the currently selected section page, or <c>null</c> if none.</summary>
    public SettingsNavPage? SelectedPage =>
        _selectedIndex >= 0 && _selectedIndex < _pages.Count ? _pages[_selectedIndex] : null;

    /// <summary>Raised when the selected section changes.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>Adds a new section page and selects it if it's the first one added.</summary>
    public void AddPage(SettingsNavPage page)
    {
        _pages.Add(page);
        if (_selectedIndex < 0)
            _selectedIndex = 0;
        RebuildNavButtons();
        UpdateSelection();
    }

    /// <summary>Re-themes the nav strip (button colors) and every page's content - not just the
    /// selected one, mirroring <see cref="ThemedTabControl.RefreshTheme"/> for the same reason: a
    /// hidden page's content is otherwise unreachable by any control-tree walk until selected.</summary>
    public void RefreshTheme()
    {
        var p = ThemeService.Current;
        BackColor = p.Background;
        _navPanel.BackColor = p.PanelBackground;
        _contentPanel.BackColor = p.Background;

        foreach (Control c in _navPanel.Controls)
        {
            if (c is not RoundedButton btn || btn.Tag is not int idx) continue;
            ApplyButtonColors(btn, idx == _selectedIndex, p);
        }

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

    private static void ApplyButtonColors(RoundedButton btn, bool selected, ThemePalette p)
    {
        btn.BackColor = selected ? p.ToolbarHover : p.PanelBackground;
        btn.ForeColor = selected ? p.Foreground : p.DimForeground;
        btn.HoverColor = p.ToolbarHover;
        btn.PressedColor = p.ToolbarHover;
        btn.BorderColor = selected ? p.Accent : Color.Empty;
        btn.BorderWidth = selected ? 1 : 0;
        btn.Invalidate();
    }

    private void RebuildNavButtons()
    {
        foreach (Control c in _navPanel.Controls.Cast<Control>().ToList())
            c.Dispose();
        _navPanel.Controls.Clear();

        var p = ThemeService.Current;
        for (var i = 0; i < _pages.Count; i++)
        {
            var index = i;
            var page = _pages[i];
            var btn = new RoundedButton
            {
                Text = page.Text,
                Name = page.AutomationId,
                Width = NavWidth - _navPanel.Padding.Horizontal,
                Height = ItemHeight,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 8, 0),
                Margin = new Padding(0, 0, 0, 2),
                FlatStyle = FlatStyle.Flat,
                Font = p.GridFont,
                Cursor = Cursors.Hand,
                Tag = index,
                CornerRadius = 4,
                UseGradient = false,
                DrawShadow = false,
                TabStop = true
            };
            btn.Click += (_, _) => SelectedIndex = index;
            ApplyButtonColors(btn, index == _selectedIndex, p);
            _navPanel.Controls.Add(btn);
        }
    }

    private void UpdateSelection()
    {
        var p = ThemeService.Current;
        foreach (Control c in _navPanel.Controls)
        {
            if (c is not RoundedButton btn || btn.Tag is not int idx) continue;
            ApplyButtonColors(btn, idx == _selectedIndex, p);
        }

        // Re-parenting always drops keyboard focus - only do it when the selected page's content
        // actually changed (same guard ThemedTabControl.UpdateTabs uses).
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

        _navPanel.Invalidate();
        _contentPanel.Invalidate();
    }
}

/// <summary>One section in a <see cref="SettingsNavControl"/>.</summary>
public sealed class SettingsNavPage
{
    /// <summary>Display text for the nav button.</summary>
    public string Text { get; }

    /// <summary>Content control shown when this section is selected.</summary>
    public Control Content { get; }

    /// <summary>Sets <see cref="Control.Name"/> on the nav button, which WinForms' UIA bridge
    /// exposes as <c>AutomationId</c> - lets a UI test address a section by stable identity
    /// instead of by its (possibly localized) display text.</summary>
    public string? AutomationId { get; }

    public SettingsNavPage(string text, Control content, string? automationId = null)
    {
        Text = text;
        Content = content;
        AutomationId = automationId;
    }
}
