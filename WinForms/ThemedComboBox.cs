using CoderCommander.Services;
using System.Drawing.Drawing2D;

namespace CoderCommander.WinForms;

/// <summary>
/// Fully owner-drawn drop-down list with theme support and full keyboard/DPI support.
/// Replaces WinForms ComboBox which ignores BackColor in dark mode.
/// </summary>
public sealed class ThemedComboBox : UserControl, ISelfThemedControl
{
    private readonly List<string> _items = new();
    private readonly ContextMenuStrip _menu;
    private int _selectedIndex = -1;
    private bool _hover;
    private bool _pressed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemedComboBox"/> class with theme-aware
    /// colors, keyboard support, and a drop-down context menu.
    /// </summary>
    public ThemedComboBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
        TabStop = true;

        Height = ScaledDefaultHeight;
        Width = ScaledDefaultWidth;
        Cursor = Cursors.Hand;
        Font = ThemeService.Current.GridFont;

        _menu = new ContextMenuStrip
        {
            BackColor = ThemeService.Current.PanelBackground,
            ForeColor = ThemeService.Current.Foreground,
            Renderer = new ThemeRenderer()
        };
        _menu.ItemClicked += OnMenuItemClicked;
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    /// <summary>Handles the <see cref="ThemeService.ThemeChanged"/> event by calling <see cref="RefreshTheme"/>.</summary>
    private void OnThemeChanged(object? sender, EventArgs e) => RefreshTheme();

    /// <summary>
    /// Re-reads the palette for the drop-down menu (previously only set once, at construction,
    /// so it kept the old theme's colors after a live switch) and repaints the owner-drawn face.
    /// </summary>
    public void RefreshTheme()
    {
        var p = ThemeService.Current;
        _menu.BackColor = p.PanelBackground;
        _menu.ForeColor = p.Foreground;
        Font = p.GridFont;
        Invalidate();
    }

    /// <summary>Unsubscribes from the theme event and disposes the drop-down menu.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeService.ThemeChanged -= OnThemeChanged;
            ClearMenuItems();
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Clears and disposes all items in the drop-down menu, avoiding collection-modified exceptions.</summary>
    private void ClearMenuItems()
    {
        // Disposing a ToolStripItem removes it from its owner's Items collection, so disposing
        // while foreach-ing over that same live collection throws "Collection was modified" -
        // snapshot first, then clear, then dispose the snapshot.
        var items = _menu.Items.Cast<ToolStripItem>().ToArray();
        _menu.Items.Clear();
        foreach (var item in items)
            item.Dispose();
    }

    /// <summary>Gets the list of items displayed in this combo box.</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>
    /// Gets or sets the index of the currently selected item.
    /// Setting this value clamps to valid range and raises <see cref="SelectedIndexChanged"/>.
    /// </summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            value = _items.Count == 0 ? -1 : Math.Clamp(value, -1, _items.Count - 1);
            if (_selectedIndex == value) return;
            _selectedIndex = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gets the currently selected item text, or <c>null</c> if nothing is selected.</summary>
    public string? SelectedItem =>
        _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

    /// <summary>Raised when the selected item changes.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>Adds a single item to the combo box. Auto-selects the first item added.</summary>
    public void AddItem(string item)
    {
        _items.Add(item);
        if (_selectedIndex < 0 && _items.Count == 1)
            SelectedIndex = 0;
        Invalidate();
    }

    /// <summary>Bulk-adds items, in order. The migration path off the raw <see cref="ComboBox"/>
    /// (<c>UiHelpers.CreateComboBox</c>, now removed) went through this - every call site there
    /// populated the whole list at once via <c>Items.AddRange</c>.</summary>
    public void AddItems(params string[] items)
    {
        foreach (var item in items)
            AddItem(item);
    }

    /// <summary>Adds multiple items from an enumerable collection.</summary>
    public void AddItems(IEnumerable<string> items)
    {
        foreach (var item in items)
            AddItem(item);
    }

    /// <summary>Removes all items and resets the selection, raising <see cref="SelectedIndexChanged"/> if a selection was active.</summary>
    public void ClearItems()
    {
        var hadSelection = _selectedIndex >= 0;
        _items.Clear();
        _selectedIndex = -1;
        Invalidate();
        // Previously silent - a caller repopulating the list on e.g. a format change never
        // observed the selection actually being dropped, so anything reading SelectedItem in
        // between (before the next AddItem re-selects index 0) saw stale state.
        if (hadSelection)
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Scales a 96-DPI design pixel value to this control's current DPI. Read fresh on
    /// every call (never cached) - the app is PerMonitorV2-aware.</summary>
    private int Scale(int px96) => (int)Math.Round(px96 * DeviceDpi / 96.0);

    private const int DefaultHeight96 = 28;
    private const int DefaultWidth96 = 160;
    private const int Radius96 = 4;
    private const int ArrowWidth96 = 24;
    private int ScaledDefaultHeight => Scale(DefaultHeight96);
    private int ScaledDefaultWidth => Scale(DefaultWidth96);
    private int ScaledRadius => Math.Max(2, Scale(Radius96));
    private int ScaledArrowWidth => Scale(ArrowWidth96);

    /// <summary>Invalidates the control when the parent DPI changes.</summary>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Invalidate();
    }

    /// <summary>Treats arrow keys, Home, and End as input keys for direct navigation.</summary>
    protected override bool IsInputKey(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Up:
            case Keys.Down:
            case Keys.Left:
            case Keys.Right:
            case Keys.Home:
            case Keys.End:
                return true;
            default:
                return base.IsInputKey(keyData);
        }
    }

    /// <summary>Handles F4/Space/Alt+Down to open the drop-down, and arrow/Home/End for selection navigation.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_items.Count == 0) return;

        if (e.KeyCode == Keys.F4 || e.KeyCode == Keys.Space || (e.KeyCode == Keys.Down && e.Alt))
        {
            ShowDropDown();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Left)
        {
            SelectedIndex = SelectedIndex <= 0 ? 0 : SelectedIndex - 1;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Right)
        {
            SelectedIndex = SelectedIndex < 0 ? 0 : Math.Min(_items.Count - 1, SelectedIndex + 1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Home)
        {
            SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.End)
        {
            SelectedIndex = _items.Count - 1;
            e.Handled = true;
        }
    }

    /// <summary>Implements type-ahead: pressing a character jumps to the next matching item, wrapping around.</summary>
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (_items.Count == 0 || char.IsControl(e.KeyChar)) return;

        // Type-ahead: jump to the next item (wrapping) whose text starts with the typed
        // character, starting the search just past the current selection so repeated presses of
        // the same letter cycle through matches instead of always landing on the first one.
        var start = _selectedIndex + 1;
        for (var offset = 0; offset < _items.Count; offset++)
        {
            var idx = (start + offset) % _items.Count;
            if (_items[idx].StartsWith(e.KeyChar.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                SelectedIndex = idx;
                break;
            }
        }
        e.Handled = true;
    }

    /// <summary>Repaints the control when focus is gained.</summary>
    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    /// <summary>Repaints the control when focus is lost.</summary>
    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    /// <summary>Repaints when the parent changes to fix corner slivers against the new parent background.</summary>
    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        // Repaints the corner slivers outside the rounded rect against the new parent's color -
        // every call site constructs this control before adding it to a parent.
        Invalidate();
    }

    /// <summary>Sets the hover state and repaints.</summary>
    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    /// <summary>Clears hover and pressed states, then repaints.</summary>
    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    /// <summary>Sets the pressed state on left mouse button down.</summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
        base.OnMouseDown(e); // Selectable=true -> base already calls Focus() here.
    }

    /// <summary>Clears the pressed state and repaints.</summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    /// <summary>Opens the drop-down menu when the control is clicked.</summary>
    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        ShowDropDown();
    }

    /// <summary>Builds and shows the drop-down context menu with all items and current selection state.</summary>
    private void ShowDropDown()
    {
        if (_items.Count == 0) return;

        var p = ThemeService.Current;
        ClearMenuItems();
        _menu.BackColor = p.PanelBackground;
        _menu.ForeColor = p.Foreground;
        // Otherwise a narrow combo (e.g. Checksum's 120px algorithm picker) can open a menu
        // narrower than its own face, since ContextMenuStrip sizes itself to its longest item by
        // default with no floor.
        _menu.MinimumSize = new Size(Width, 0);

        for (var i = 0; i < _items.Count; i++)
        {
            var mi = new ToolStripMenuItem(_items[i])
            {
                Tag = i,
                BackColor = p.PanelBackground,
                ForeColor = p.Foreground,
                Checked = i == _selectedIndex,
                CheckOnClick = false
            };
            _menu.Items.Add(mi);
        }

        _menu.Show(this, new Point(0, Height));
    }

    /// <summary>Handles drop-down menu item clicks, resolving the selected index by stored Tag.</summary>
    private void OnMenuItemClicked(object? sender, ToolStripItemClickedEventArgs e)
    {
        // Resolved by index (Tag), not by matching mi.Text back against _items - two items with
        // the same display text used to always resolve to the first one, regardless of which was
        // actually clicked (CopyMoveDialogForm's overwrite-policy combo is exactly the kind of
        // list this could bite, if two policy names ever collided after translation).
        if (e.ClickedItem is ToolStripMenuItem { Tag: int idx })
            SelectedIndex = idx;
    }

    /// <summary>Owner-draws the combo box face with rounded rectangle, gradient, border, text, and drop arrow.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        var p = ThemeService.Current;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        var radius = ScaledRadius;

        // Clear entire background first, with the PARENT's color - the previous ControlThemer
        // case forced this control's own BackColor to PanelBackground, which made the corner
        // slivers outside the rounded rect show as visible light notches on any parent whose
        // background is p.Background instead (e.g. a dialog's main TableLayoutPanel).
        var clearColor = Parent?.BackColor ?? p.Background;
        using (var clearBrush = new SolidBrush(clearColor))
            g.FillRectangle(clearBrush, ClientRectangle);

        // Base color
        var baseColor = _pressed ? p.ToolbarHover : _hover ? p.HeaderBackground : p.PanelBackground;
        var topColor = ControlPaint.Light(baseColor, 0.06f);
        var bottomColor = ControlPaint.Dark(baseColor, 0.03f);

        // Background gradient
        using (var path = GraphicsHelpers.GetRoundedRect(rect, radius))
        using (var grad = new LinearGradientBrush(rect, topColor, bottomColor, 90f))
            g.FillPath(grad, path);

        // Border - focus ring takes priority over hover, both drawn as the same thicker/accented
        // outline (no separate shadow layer: on a flat control the old hardcoded-alpha shadow
        // just clipped into a hard dark edge rather than reading as a soft shadow).
        var showAccent = Focused || _hover;
        using (var path = GraphicsHelpers.GetRoundedRect(rect, radius))
        using (var borderPen = new Pen(showAccent ? p.Accent : p.GridLine, showAccent ? 2 : 1))
            g.DrawPath(borderPen, path);

        // Text
        var text = SelectedItem ?? "";
        var arrowWidth = ScaledArrowWidth;
        var textInset = Scale(10);
        var textRect = new Rectangle(rect.X + textInset, rect.Y + 2, rect.Width - arrowWidth - Scale(16), rect.Height - 4);
        TextRenderer.DrawText(g, text, Font, textRect, p.Foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // Filled triangle arrow, centered under the arrow-reserved width
        var arrowCenterX = rect.Right - arrowWidth / 2 - 1;
        var arrowY = rect.Y + rect.Height / 2;
        var halfBase = Math.Max(3, Scale(4));
        var dropAmount = Math.Max(2, Scale(3));
        using var arrowPath = new GraphicsPath();
        arrowPath.AddPolygon(new[]
        {
            new Point(arrowCenterX - halfBase, arrowY - dropAmount / 2),
            new Point(arrowCenterX + halfBase, arrowY - dropAmount / 2),
            new Point(arrowCenterX, arrowY + dropAmount)
        });
        using var arrowBrush = new SolidBrush(p.DimForeground);
        g.FillPath(arrowBrush, arrowPath);
    }
}
