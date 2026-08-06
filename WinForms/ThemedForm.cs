using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Base form with theme support and standard WinForms title bar.
/// All application dialogs inherit from this.
/// </summary>
public class ThemedForm : Form
{
    /// <summary>Whether this dialog is resizable (default false).</summary>
    public bool Resizable { get; set; } = false;

    private readonly List<ListViewScrollbarOverlay> _lvOverlays = new();
    private bool _overlaysAttached;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemedForm"/> class with standard
    /// dialog layout, theme-aware colors, and double buffering.
    /// </summary>
    public ThemedForm()
    {
        DoubleBuffered = true;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = ThemeService.Current.GridFont;
        BackColor = ThemeService.Current.Background;
        ForeColor = ThemeService.Current.Foreground;
        Padding = new Padding(0);
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    /// <summary>Handles the <see cref="ThemeService.ThemeChanged"/> event by scheduling a theme refresh on the UI thread.</summary>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated)
            BeginInvoke(RefreshTheme);
    }

    /// <summary>Releases the theme event subscription and disposes all <see cref="ListViewScrollbarOverlay"/> instances.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeService.ThemeChanged -= OnThemeChanged;
            foreach (var overlay in _lvOverlays)
                overlay.Dispose();
            _lvOverlays.Clear();
        }
        base.Dispose(disposing);
    }

    /// <summary>Applies the immersive dark title bar via <see cref="NativeControlThemer"/> after the native window handle is created.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeControlThemer.ApplyDarkTitleBar(Handle);
    }

    /// <summary>
    /// Applies resizable border style, attaches <see cref="ListViewScrollbarOverlay"/> instances,
    /// and applies the current theme palette after the form is loaded.
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (Resizable)
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
        }
        // Before ApplyTheme: attaching an overlay to a ListView sitting inside a
        // TableLayoutPanel/FlowLayoutPanel re-parents it into a host Panel, which recreates its
        // window handle. NativeControlThemer.ThemeListView subscribes to that ListView's
        // HandleCreated only once (unsubscribing itself after firing), so doing this after
        // ApplyTheme would mean the recreated handle never gets dark-mode-themed.
        AttachListViewOverlays();
        ApplyTheme();
    }

    /// <summary>
    /// Covers every dialog <see cref="ListView"/>'s native scrollbars with the same themed
    /// overlay <see cref="Views.FilePanelUserControl"/> uses for the file panels - a plain
    /// <c>SetWindowTheme</c> does not reliably darken a ListView's native scrollbar, which used
    /// to leave an unstyled strip next to it (see <see cref="ListViewScrollbarOverlay"/>'s own
    /// doc comment). Runs once; <see cref="OnLoad"/> can in principle be re-entered.
    /// </summary>
    private void AttachListViewOverlays()
    {
        if (_overlaysAttached) return;
        _overlaysAttached = true;

        foreach (var lv in FindListViews(this))
        {
            var overlay = ListViewScrollbarOverlay.Attach(lv);
            overlay.NativeMetricsChanged += (_, _) => NativeControlThemer.RefitLastColumn(lv);
            _lvOverlays.Add(overlay);
        }
    }

    /// <summary>Recursively finds all <see cref="ListView"/> controls nested under the given parent.</summary>
    private static IEnumerable<ListView> FindListViews(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is ListView lv)
                yield return lv;
            else
                foreach (var nested in FindListViews(child))
                    yield return nested;
        }
    }

    /// <summary>
    /// Re-applies the current theme palette, refreshes the dark title bar, and forces a full repaint.
    /// Call this after a live theme switch or when the form needs visual regeneration.
    /// </summary>
    public void RefreshTheme()
    {
        ApplyTheme();
        NativeControlThemer.ApplyDarkTitleBar(Handle);
        Invalidate();
        Update();
    }

    /// <summary>
    /// Applies the current <see cref="ThemeService.Current"/> palette to this form's background,
    /// foreground, native scrollbars, and all descendant controls via <see cref="ControlThemer"/>.
    /// </summary>
    protected virtual void ApplyTheme()
    {
        var p = ThemeService.Current;
        BackColor = p.Background;
        ForeColor = p.Foreground;
        if (IsHandleCreated)
            NativeControlThemer.ApplyDarkScrollbars(this);
        ControlThemer.ThemeDescendants(this);
    }

    /// <summary>Creates a themed button with optional accent styling. Reusable from any context.</summary>
    public static Button CreateThemedButton(string text, bool accent = false)
    {
        var p = ThemeService.Current;
        const int hPadding = 20;
        var textWidth = TextRenderer.MeasureText(text, p.GridFont).Width;
        var btn = new RoundedButton
        {
            Text = text,
            Height = 32,
            Width = Math.Max(80, textWidth + hPadding * 2 + 4),
            Font = p.GridFont,
            Cursor = Cursors.Hand,
            Padding = new Padding(hPadding, 0, hPadding, 0),
            Role = accent ? ThemeRole.PrimaryButton : ThemeRole.SecondaryButton,
            CornerRadius = 4,
            UseGradient = true,
            DrawShadow = false
        };
        ControlThemer.ThemeSingleControl(btn, p);
        return btn;
    }

    /// <summary>Creates a bottom button panel with primary + secondary buttons.</summary>
    protected Panel CreateBottomPanel(Button primary, Button? secondary = null, Button? tertiary = null)
    {
        var p = ThemeService.Current;
        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            // Margin defaults to WinForms' built-in 3px on every side. Harmless for a control
            // added directly to a Form, but when this panel ends up inside a TableLayoutPanel
            // cell (as in CopyMoveDialogForm's mainLayout, RowStyle Absolute 50), the layout
            // engine subtracts Margin from the allocated row height - Height=50 rendered as
            // 44px, 6px short, which cascaded through Padding into the button FlowLayoutPanel
            // ending up 4px shorter than the 32px buttons it holds (check_layout() caught this
            // as OK/Cancel "extends outside its parent FlowLayoutPanel's bounds" - confirmed
            // via the exact Bounds numbers, not just the finding, before attributing it here).
            Margin = new Padding(0),
            BackColor = p.HeaderBackground,
            Tag = ThemeRole.HeaderBackground,
            Padding = new Padding(16, 8, 16, 8)
        };

        // Right-anchored buttons go in a left-to-right FlowLayoutPanel (secondary added first,
        // primary last so primary ends up rightmost) instead of Dock=Right + Margin: docked
        // children ignore Margin entirely, so the old approach rendered every button flush
        // against its neighbor with no gap.
        var rightGroup = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
        };
        if (secondary != null)
        {
            secondary.Margin = new Padding(0, 0, 8, 0);
            rightGroup.Controls.Add(secondary);
        }
        primary.Margin = new Padding(0);
        rightGroup.Controls.Add(primary);
        panel.Controls.Add(rightGroup);

        if (tertiary != null)
        {
            tertiary.Dock = DockStyle.Left;
            panel.Controls.Add(tertiary);
        }

        return panel;
    }
}
