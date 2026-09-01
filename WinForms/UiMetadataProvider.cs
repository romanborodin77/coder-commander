using System.ComponentModel;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Design-time bridge between the Windows Forms Designer and this app's two runtime-driven UI
/// concerns - theming and localization. Drop one of these on a form and every control on that form
/// gains two extra entries in the Property Grid: <b>ThemeRole</b> and <b>LocalizationKey</b>. This is
/// the same <see cref="IExtenderProvider"/> mechanism <see cref="ToolTip"/> uses to give every
/// control a "ToolTip on toolTip1" property.
///
/// <para><b>Why an extender rather than plain properties.</b> Neither concern can be expressed as a
/// value the designer serializes directly. A colour cannot: the palette is chosen at runtime and
/// swapped live on a theme switch, so a literal in <c>InitializeComponent()</c> would be wrong the
/// moment the user switches themes. Text cannot either: it comes from <c>lang/*.lng</c> at runtime,
/// and a literal would freeze whichever language the IDE happened to show (always English - the
/// designer never calls <see cref="LocalizationService.LoadLanguage"/>). An extender stores the
/// *intent* - which role, which key - and lets runtime resolve it.</para>
///
/// <para><b>ThemeRole writes straight through to <see cref="Control.Tag"/></b>, which is exactly
/// where <see cref="ControlThemer"/> already looks (see <see cref="ThemeRoleExtensions.SetRole"/>).
/// That is deliberate: it means <see cref="ControlThemer"/> needs no changes at all, and the ~76
/// existing hand-written <c>Tag = ThemeRole.X</c> / <c>.SetRole(...)</c> call sites keep working
/// untouched alongside designer-authored ones. <see cref="RoundedButton"/> is the one control that
/// does not need this - it carries a real <c>Role</c> property the designer serializes natively.</para>
/// </summary>
[ProvideProperty("ThemeRole", typeof(Control))]
[ProvideProperty("LocalizationKey", typeof(Control))]
public sealed class UiMetadataProvider : Component, IExtenderProvider
{
    /// <summary>Localization keys keyed by control. Not stored in <see cref="Control.Tag"/> the way
    /// <see cref="ThemeRole"/> is, because Tag is already taken by the role - one <c>object</c> slot
    /// cannot carry both without inventing a composite type that every existing
    /// <c>Tag is ThemeRole</c> check in the codebase would then have to learn about.</summary>
    private readonly Dictionary<Control, string> _localizationKeys = new();

    /// <summary>Creates a provider that is not owned by a container.</summary>
    public UiMetadataProvider()
    {
    }

    /// <summary>Creates a provider owned by <paramref name="container"/> - the constructor the
    /// designer emits (<c>new UiMetadataProvider(this.components)</c>), matching how
    /// <see cref="ToolTip"/> and <see cref="ErrorProvider"/> are generated, so disposing the form
    /// disposes this too.</summary>
    public UiMetadataProvider(IContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        container.Add(this);
    }

    /// <summary>Every <see cref="Control"/> is extendable. A <see cref="Form"/> is deliberately
    /// included: its own <see cref="Form.Text"/> is the window title and needs localizing too.</summary>
    public bool CanExtend(object extendee) => extendee is Control;

    /// <summary>Gets the <see cref="Services.ThemeRole"/> assigned to <paramref name="control"/>, reading
    /// it back out of <see cref="Control.Tag"/> so a role set by older hand-written code is reported
    /// identically to one set here.</summary>
    [Category("Appearance")]
    [DefaultValue(null)]
    [Description("Which palette role paints this control. Read by ControlThemer on every theme switch; " +
                 "leave unset to get the generic default for the control's type.")]
    public ThemeRole? GetThemeRole(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetRole();
    }

    /// <summary>Assigns a <see cref="Services.ThemeRole"/> by writing it into <see cref="Control.Tag"/> -
    /// see this class's own doc comment for why that indirection is the point rather than a compromise.</summary>
    public void SetThemeRole(Control control, ThemeRole? role)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (role is { } r)
            control.SetRole(r);
        else if (control.Tag is ThemeRole)
            control.Tag = null; // only clear a Tag this provider owns, never someone else's
    }

    /// <summary>Gets the localization key assigned to <paramref name="control"/>, or an empty
    /// string.</summary>
    [Category("Localizable")]
    [DefaultValue("")]
    [Description("Key looked up in lang/*.lng to fill this control's Text at runtime, e.g. Common.OK. " +
                 "Whatever Text the designer shows is only a placeholder.")]
    public string GetLocalizationKey(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return _localizationKeys.TryGetValue(control, out var key) ? key : "";
    }

    /// <summary>Assigns the localization key used by <see cref="ApplyLocalization"/>.</summary>
    public void SetLocalizationKey(Control control, string? key)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (string.IsNullOrEmpty(key))
            _localizationKeys.Remove(control);
        else
            _localizationKeys[control] = key;
    }

    /// <summary>
    /// Replaces the designer's placeholder <see cref="Control.Text"/> with the real translation on
    /// every control that was given a key. Call this once, immediately after
    /// <c>InitializeComponent()</c>.
    ///
    /// <para>Safe to call again after a language switch, which is what makes live re-localization
    /// possible for a dialog that stays open - something the current architecture only supports for
    /// <c>MainForm</c> and the file panels, via their hand-maintained <c>Relocalize()</c> methods.</para>
    /// </summary>
    public void ApplyLocalization()
    {
        foreach (var (control, key) in _localizationKeys)
            control.Text = LocalizationService.Current.GetString(key);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _localizationKeys.Clear();
        base.Dispose(disposing);
    }
}
