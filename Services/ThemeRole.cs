namespace CoderCommander.Services;

/// <summary>
/// A stylistic role a control can be tagged with (via <see cref="ThemeRoleExtensions.SetRole"/>,
/// stored in <see cref="Control.Tag"/>) so <see cref="WinForms.ControlThemer.ThemeSingleControl"/>
/// knows how to re-theme it on every theme switch instead of falling back to a generic default
/// that overwrites whatever the dialog deliberately chose at construction time.
///
/// This supersedes <see cref="PanelThemeRole"/> - the same mechanism, extended to labels,
/// buttons and text roles rather than just three panel background variants. Existing
/// <c>Tag = PanelThemeRole.X</c> call sites migrate mechanically to <c>Tag = ThemeRole.X</c>;
/// the member names below were kept identical to the old enum for that reason.
/// </summary>
public enum ThemeRole
{
    // ── Surfaces (Panel / TableLayoutPanel background) ──
    Background,
    PanelBackground,
    HeaderBackground,
    ToolbarBackground,
    Accent,

    // ── Text (Label / LinkLabel foreground + font) ──
    Title,
    Subtitle,
    Section,
    Body,
    /// <summary>Bold GridFont, regular (non-header) foreground - inline emphasized field labels
    /// like "Source:" / "Destination:". <see cref="UiHelpers.CreateLabel"/> tags every
    /// <c>bold: true</c> label with this automatically.</summary>
    Emphasis,
    /// <summary>Regular (non-italic) font, dimmed color - secondary/descriptive text. Distinct
    /// from <see cref="Hint"/>, which is italic (matches the italic hint style already used for
    /// overwrite-conflict explanations).</summary>
    Muted,
    Hint,
    Danger,
    Link,

    // ── Buttons (RoundedButton color scheme) ──
    PrimaryButton,
    SecondaryButton,
    DangerButton,
}

/// <summary>
/// Reads/writes a control's <see cref="ThemeRole"/> through its <see cref="Control.Tag"/>.
/// Safe to mix with other <c>Tag</c> uses elsewhere in the app (icon keys on
/// <see cref="ToolStripItem"/>, domain objects on <see cref="ListViewItem"/>) because those are
/// always a different runtime type - the `is ThemeRole` pattern match below only ever matches a
/// control that was actually tagged through this helper.
/// </summary>
public static class ThemeRoleExtensions
{
    public static void SetRole(this Control control, ThemeRole role) => control.Tag = role;

    public static ThemeRole? GetRole(this Control control) => control.Tag is ThemeRole role ? role : null;
}
