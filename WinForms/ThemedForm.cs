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

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated)
            BeginInvoke(RefreshTheme);
    }

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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeControlThemer.ApplyDarkTitleBar(Handle);
    }

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

    public void RefreshTheme()
    {
        ApplyTheme();
        NativeControlThemer.ApplyDarkTitleBar(Handle);
        Invalidate();
        Update();
    }

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
