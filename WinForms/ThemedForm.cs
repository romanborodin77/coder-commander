using System.ComponentModel;
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
        // At design time, avoid accessing ThemeService which may not be initialized properly in the IDE.
        // Use default values instead - the form will get proper theming when it runs in the application.
        if (!DesignTime.IsActive)
        {
            Font = ThemeService.Current.GridFont;
            BackColor = ThemeService.Current.Background;
            ForeColor = ThemeService.Current.Foreground;
            ThemeService.ThemeChanged += OnThemeChanged;
        }
        Padding = new Padding(0);
    }

    // The three properties below are shadowed for one reason only: to stop the Windows Forms
    // Designer from serializing them into InitializeComponent(). The constructor above assigns all
    // three from the live palette, so the designer would see values that differ from the WinForms
    // defaults and bake them into .Designer.cs as literals - Color.FromArgb(30, 30, 30) and
    // new Font("Segoe UI", 9F) - freezing whichever theme happened to be active in the IDE and, for
    // Font, minting a form-owned instance that gets disposed with the form, which is exactly the
    // shared-instance contract FontCache exists to guarantee. Hiding them also keeps them out of the
    // Property Grid, which is honest: setting a colour there would be futile, since ControlThemer
    // re-applies the palette on every theme switch. Colours are chosen via ThemeRole instead.
    // Child controls need no equivalent treatment - they inherit colour and font ambiently from the
    // form, and the designer does not serialize a property whose value matches its ambient source.

    /// <inheritdoc cref="Control.BackColor"/>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Color BackColor
    {
        get => base.BackColor;
        set => base.BackColor = value;
    }

    /// <inheritdoc cref="Control.ForeColor"/>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Color ForeColor
    {
        get => base.ForeColor;
        set => base.ForeColor = value;
    }

    /// <inheritdoc cref="Control.Font"/>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Font Font
    {
        get => base.Font;
        set => base.Font = value;
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
    /// <param name="name">Sets <see cref="Control.Name"/>, which WinForms' UIA bridge exposes
    /// directly as <c>AutomationId</c> - the standard, no-custom-provider way for a UI test to
    /// address this control by identity instead of by its (possibly localized) text.</param>
    public static Button CreateThemedButton(string text, bool accent = false, string? name = null)
    {
        var p = ThemeService.Current;
        const int hPadding = 20;
        var textWidth = TextRenderer.MeasureText(text, p.GridFont).Width;
        var btn = new RoundedButton
        {
            Name = name ?? "",
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
}
