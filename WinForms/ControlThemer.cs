using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Marker for a composite control that re-themes its own subtree in response to
/// <see cref="ThemeService.ThemeChanged"/> - including descendants that aren't currently
/// parented anywhere visible, like a <see cref="ThemedTabControl"/> page that isn't the
/// selected tab right now. <see cref="ControlThemer.ThemeDescendants"/> calls
/// <see cref="RefreshTheme"/> on these instead of walking their <c>Controls</c> collection
/// itself - stepping in directly would either duplicate that work or miss content the control
/// only parents conditionally.
/// </summary>
public interface ISelfThemedControl
{
    /// <summary>Re-themes this control and its own subtree in response to a theme change.</summary>
    void RefreshTheme();
}

/// <summary>
/// Recursively applies the active <see cref="DesignerSafeThemeService.Current"/> palette to a control tree.
/// <see cref="ThemedForm"/> uses this for its own descendants; any <see cref="ISelfThemedControl"/>
/// (e.g. <see cref="ThemedTabControl"/>) can call it too, to theme its own children the same way.
///
/// Recursion previously stopped at a fixed allow-list of container types (TabPage/
/// TableLayoutPanel/FlowLayoutPanel/GroupBox/SplitContainer/Panel), which meant a plain
/// <see cref="UserControl"/> hosting dialog content - most notably <c>SettingsForm</c>'s entire
/// body, which lives inside a <see cref="ThemedTabControl"/> - never got re-themed on a live
/// theme switch. The <c>UserControl</c> branch below closes that gap; <see cref="ISelfThemedControl"/>
/// is the escape hatch for the composite controls that already handle their own re-theming and
/// would otherwise get walked (and re-themed) twice.
/// </summary>
public static class ControlThemer
{
    /// <summary>
    /// Recursively applies the active <see cref="DesignerSafeThemeService.Current"/> palette to every control
    /// in the tree. Delegates to <see cref="ISelfThemedControl.RefreshTheme"/> for composite
    /// controls that manage their own descendant theming.
    /// </summary>
    public static void ThemeDescendants(Control parent)
    {
        var p = DesignerSafeThemeService.Current;

        foreach (Control c in parent.Controls)
        {
            ThemeSingleControl(c, p);

            if (c is ISelfThemedControl self)
            {
                self.RefreshTheme();
            }
            else if (c is TabControl tc)
            {
                foreach (TabPage tp in tc.TabPages)
                {
                    tp.BackColor = p.Background;
                    tp.ForeColor = p.Foreground;
                    ThemeDescendants(tp);
                }
            }
            else if (c is TabPage or TableLayoutPanel or FlowLayoutPanel or GroupBox or SplitContainer or Panel)
            {
                ThemeDescendants(c);
            }
            else if (c is UserControl && c.HasChildren)
            {
                ThemeDescendants(c);
            }
        }
    }

    /// <summary>
    /// Applies theme-specific styling to a single control based on its type (Button, TextBox,
    /// ListView, Panel, etc.). Handles color, font, border, and native chrome theming.
    /// </summary>
    internal static void ThemeSingleControl(Control c, ThemePalette p)
    {
        switch (c)
        {
            case RoundedButton rbtn:
                ApplyButtonRole(rbtn, rbtn.Role, p);
                break;

            case Button btn:
                btn.FlatStyle = FlatStyle.Flat;
                btn.BackColor = p.HeaderBackground;
                btn.ForeColor = p.Foreground;
                btn.FlatAppearance.BorderColor = p.GridLine;
                btn.FlatAppearance.BorderSize = 1;
                btn.FlatAppearance.MouseOverBackColor = p.ToolbarHover;
                btn.Cursor = Cursors.Hand;
                btn.Height = Math.Max(btn.Height, 30);
                if (btn.Padding == Padding.Empty)
                    btn.Padding = new Padding(16, 0, 16, 0);
                break;

            case TextBox tb:
                tb.BackColor = p.PanelBackground;
                tb.ForeColor = p.Foreground;
                tb.BorderStyle = BorderStyle.FixedSingle;
                tb.Font = p.GridFont;
                NativeControlThemer.ApplyDarkScrollbars(tb);
                break;

            case RichTextBox rtb:
                rtb.BackColor = p.PanelBackground;
                rtb.ForeColor = p.Foreground;
                rtb.BorderStyle = BorderStyle.None;
                rtb.Font = p.MonoFont;
                NativeControlThemer.ApplyDarkScrollbars(rtb);
                break;

            case ListView lv:
                lv.BackColor = p.PanelBackground;
                lv.ForeColor = p.Foreground;
                lv.BorderStyle = BorderStyle.None;
                lv.Font = p.GridFont;
                NativeControlThemer.ThemeListView(lv);
                // Only ever a dialog ListView here (Views.FilePanelUserControl's file list
                // themes itself outside ControlThemer and owner-draws its own rows) - safe to
                // add the generic header owner-draw fallback unconditionally.
                NativeControlThemer.ThemeListViewHeader(lv);
                break;

            case TreeView tv:
                tv.BackColor = p.PanelBackground;
                tv.ForeColor = p.Foreground;
                tv.BorderStyle = BorderStyle.None;
                tv.Font = p.GridFont;
                NativeControlThemer.ApplyDarkScrollbars(tv);
                break;

            case ListBox lb:
                lb.BackColor = p.PanelBackground;
                lb.ForeColor = p.Foreground;
                lb.BorderStyle = BorderStyle.FixedSingle;
                lb.Font = p.GridFont;
                NativeControlThemer.ApplyDarkScrollbars(lb);
                break;

            // Most-derived first: StatusStrip/MenuStrip both derive from ToolStrip, and a type
            // pattern match picks the first case that matches, so the general ToolStrip branch
            // has to come last or it would shadow the other two.
            case StatusStrip ss:
                ss.BackColor = p.HeaderBackground;
                ss.ForeColor = p.DimForeground;
                ss.Renderer = new ThemeRenderer();
                ThemeToolStripItems(ss.Items, p);
                break;

            case MenuStrip ms:
                ms.BackColor = p.ToolbarBackground;
                ms.ForeColor = p.HeaderForeground;
                ms.Renderer = new ThemeRenderer();
                ThemeToolStripItems(ms.Items, p);
                break;

            case ToolStrip ts:
                ts.BackColor = p.ToolbarBackground;
                ts.ForeColor = p.HeaderForeground;
                ts.Renderer = new ThemeRenderer();
                ThemeToolStripItems(ts.Items, p);
                break;

            case ThemedCheckBox tcb:
                tcb.ForeColor = p.Foreground;
                tcb.Font = p.GridFont;
                // Same surface-role handling as Panel: a checkbox placed on a PanelBackground
                // grid (e.g. PropertiesForm's attribute list) needs to match that grid, not
                // fall back to the general form Background and show a visible color seam.
                ApplySurfaceRole(tcb, tcb.GetRole(), p, defaultColor: p.Background);
                tcb.Invalidate();
                break;

            case DateTimePicker dtp:
                dtp.BackColor = p.PanelBackground;
                dtp.ForeColor = p.Foreground;
                dtp.Font = p.GridFont;
                NativeControlThemer.ApplyDarkScrollbars(dtp);
                break;

            case CheckBox chk:
                chk.ForeColor = p.Foreground;
                chk.Font = p.GridFont;
                chk.BackColor = Color.Transparent;
                chk.FlatStyle = FlatStyle.Flat;
                chk.FlatAppearance.BorderColor = p.GridLine;
                chk.FlatAppearance.BorderSize = 1;
                chk.FlatAppearance.CheckedBackColor = p.PanelBackground;
                chk.FlatAppearance.MouseOverBackColor = p.ToolbarHover;
                chk.FlatAppearance.MouseDownBackColor = p.ToolbarHover;
                break;

            case RadioButton rb:
                rb.ForeColor = p.Foreground;
                rb.Font = p.GridFont;
                rb.BackColor = Color.Transparent;
                break;

            case TabControl tc2:
                tc2.BackColor = p.Background;
                tc2.ForeColor = p.Foreground;
                tc2.Font = p.GridFont;
                tc2.DrawMode = TabDrawMode.OwnerDrawFixed;
                tc2.ItemSize = new Size(0, 26);
                tc2.Padding = new Point(8, 4);
                tc2.Paint -= OnTabControlPaint;
                tc2.Paint += OnTabControlPaint;
                tc2.DrawItem -= OnTabDrawItem;
                tc2.DrawItem += OnTabDrawItem;
                NativeControlThemer.ApplyDarkScrollbars(tc2);
                break;

            case TabPage tp:
                tp.BackColor = p.Background;
                tp.ForeColor = p.Foreground;
                break;

            // LinkLabel derives from Label, so this also covers plain links unless a dialog
            // overrides the color afterward (as AboutForm does, for its live-hover behavior).
            case LinkLabel link:
                link.BackColor = Color.Transparent;
                link.LinkColor = link.ActiveLinkColor = link.VisitedLinkColor = p.Accent;
                break;

            case Label lbl:
                ApplyLabelRole(lbl, lbl.GetRole(), p);
                break;

            case TableLayoutPanel tlp:
                ApplySurfaceRole(tlp, tlp.GetRole(), p, defaultColor: p.Background);
                break;

            case FlowLayoutPanel flp:
                // Same transparency exception as the Panel case below (e.g. a bottom bar's
                // right-aligned button group, which must show its parent bar's color, not the
                // form's general background).
                if (flp.BackColor == Color.Transparent)
                    break;
                ApplySurfaceRole(flp, flp.GetRole(), p, defaultColor: p.Background);
                break;

            case GroupBox gb:
                gb.BackColor = p.Background;
                gb.ForeColor = p.Foreground;
                gb.Font = p.GridFont;
                break;

            case Panel panel:
                // A panel that explicitly chose to be transparent (e.g. one embedded inside a
                // ToolStrip/MenuStrip host) must keep that — otherwise we'd paint a solid
                // rectangle over its host and break the visual integration. Everything else gets
                // re-themed unconditionally, not just on the first pass while it's still at the
                // WinForms default: once a panel's BackColor is set to a real color (by this same
                // method, or explicitly at construction via ThemeRole), it would otherwise never
                // match SystemColors.Control again and would stay frozen at whatever theme was
                // active the first time this ran.
                if (panel.BackColor == Color.Transparent)
                    break;
                ApplySurfaceRole(panel, panel.GetRole(), p, defaultColor: p.Background);
                break;

            case SplitContainer sc:
                sc.BackColor = p.GridLine;
                break;

            case PictureBox pic:
                if (pic.BackColor != Color.Transparent)
                    pic.BackColor = p.Background;
                break;

            case NumericUpDown nud:
                nud.BackColor = p.PanelBackground;
                nud.ForeColor = p.Foreground;
                nud.Font = p.GridFont;
                nud.BorderStyle = BorderStyle.FixedSingle;
                // Without this the up/down spin buttons (a native child window, same story as
                // ListView's header) stay system-light in dark mode even though the field itself
                // is themed correctly.
                NativeControlThemer.ApplyDarkScrollbars(nud);
                break;
        }
    }

    /// <summary>
    /// Recolors every item in a ToolStrip/MenuStrip/StatusStrip (and, recursively, every
    /// dropdown menu item), which the Renderer alone doesn't reach - ForeColor/Font on a
    /// ToolStripItem are plain properties set once, not something the theme-aware renderer
    /// repaints on its own. Also regenerates the icon of any item tagged with an icon key
    /// (the established `Tag = "someIconKey"` convention used for both toolbar buttons and
    /// menu items), since <see cref="ToolbarIcons"/>'s cache is cleared on every theme switch
    /// and needs every live icon re-fetched with the new palette's colors baked in.
    /// Used by <see cref="CoderCommander.Views.MainForm"/> for its menu bar and both toolbars.
    /// </summary>
    internal static void ThemeToolStripItems(ToolStripItemCollection items, ThemePalette p)
    {
        foreach (ToolStripItem item in items)
        {
            item.ForeColor = p.HeaderForeground;
            item.Font = p.GridFont;
            if (item.Tag is string iconKey)
                item.Image = ToolbarIcons.Get(iconKey);
            if (item is ToolStripMenuItem menuItem)
                ThemeToolStripItems(menuItem.DropDownItems, p);
        }
    }

    /// <summary>Applies a text role (font + color) to a label-like control, tagged via <see cref="ThemeRoleExtensions.SetRole"/>.
    /// Falls back to the old untagged behavior (bold font ⇒ header color, else body) so labels
    /// nobody has migrated to a role yet keep working exactly as before.</summary>
    private static void ApplyLabelRole(Label lbl, ThemeRole? role, ThemePalette p)
    {
        lbl.BackColor = Color.Transparent;
        switch (role)
        {
            case ThemeRole.Title:
                lbl.Font = p.TitleFont;
                lbl.ForeColor = p.Foreground;
                break;
            case ThemeRole.Subtitle:
                lbl.Font = p.SubtitleFont;
                lbl.ForeColor = p.Foreground;
                break;
            case ThemeRole.Section:
                lbl.Font = p.SectionFont;
                lbl.ForeColor = p.HeaderForeground;
                break;
            case ThemeRole.Muted:
                lbl.Font = p.GridFont;
                lbl.ForeColor = p.DimForeground;
                break;
            case ThemeRole.Separator:
                lbl.Font = p.GridFont;
                lbl.ForeColor = p.SeparatorForeground;
                break;
            case ThemeRole.Hint:
                lbl.Font = p.ItalicFont;
                lbl.ForeColor = p.DimForeground;
                break;
            case ThemeRole.Danger:
                lbl.Font = p.GridFont;
                lbl.ForeColor = p.Danger;
                break;
            case ThemeRole.Link:
                lbl.Font = p.GridFont;
                lbl.ForeColor = p.Accent;
                break;
            case ThemeRole.Body:
                lbl.Font = p.GridFont;
                lbl.ForeColor = p.Foreground;
                break;
            case ThemeRole.Emphasis:
                lbl.Font = p.GridFontBold;
                lbl.ForeColor = p.Foreground;
                break;
            default:
                // Untagged - preserve the pre-role heuristic exactly.
                lbl.ForeColor = (lbl.Font?.Bold ?? false) ? p.HeaderForeground : p.Foreground;
                lbl.Font = p.GridFont;
                break;
        }
    }

    private static void ApplySurfaceRole(Control c, ThemeRole? role, ThemePalette p, Color defaultColor)
    {
        c.BackColor = role switch
        {
            ThemeRole.HeaderBackground => p.HeaderBackground,
            ThemeRole.PanelBackground => p.PanelBackground,
            ThemeRole.ToolbarBackground => p.ToolbarBackground,
            ThemeRole.Accent => p.Accent,
            ThemeRole.Background => p.Background,
            _ => defaultColor,
        };
    }

    /// <summary>Applies the theme role (Primary/Secondary/Danger) to a <see cref="RoundedButton"/>.</summary>
    private static void ApplyButtonRole(RoundedButton rbtn, ThemeRole? role, ThemePalette p)
    {
        switch (role)
        {
            case ThemeRole.PrimaryButton:
                rbtn.BackColor = p.Accent;
                rbtn.ForeColor = p.SelectionForeground;
                rbtn.HoverColor = p.AccentHover;
                rbtn.PressedColor = p.AccentHover;
                rbtn.BorderColor = p.Accent;
                break;
            case ThemeRole.ToolbarButton:
                // Returns early on purpose: the shared tail below imposes a border, side padding
                // and a 30px floor, which is the shape of a dialog button. An icon-only button on
                // a tab strip has to stay borderless and exactly its own size, and the rounded
                // hover fill is what makes it read as clickable.
                rbtn.BackColor = p.Background;
                rbtn.ForeColor = p.Foreground;
                rbtn.HoverColor = p.ToolbarHover;
                rbtn.PressedColor = p.Accent;
                rbtn.BorderWidth = 0;
                rbtn.Cursor = Cursors.Hand;
                rbtn.Invalidate();
                return;
            case ThemeRole.DangerButton:
                rbtn.BackColor = p.Danger;
                rbtn.ForeColor = p.SelectionForeground;
                rbtn.HoverColor = ThemeService.DimColor(p.Danger, 85);
                rbtn.PressedColor = ThemeService.DimColor(p.Danger, 70);
                rbtn.BorderColor = p.Danger;
                break;
            default: // SecondaryButton, or a plain RoundedButton nobody assigned a role to
                rbtn.BackColor = p.HeaderBackground;
                rbtn.ForeColor = p.Foreground;
                rbtn.HoverColor = p.ToolbarHover;
                rbtn.PressedColor = p.ToolbarHover;
                rbtn.BorderColor = p.GridLine;
                break;
        }
        rbtn.BorderWidth = 1;
        rbtn.Cursor = Cursors.Hand;
        rbtn.Height = Math.Max(rbtn.Height, 30);
        if (rbtn.Padding == Padding.Empty)
            rbtn.Padding = new Padding(16, 0, 16, 0);
        rbtn.Invalidate();
    }

    /// <summary>Owner-draws a <see cref="TabControl"/>'s tab strip background in the current theme color.</summary>
    private static void OnTabControlPaint(object? sender, PaintEventArgs e)
    {
        if (sender is not TabControl tc) return;
        var p = DesignerSafeThemeService.Current;
        var tabStripHeight = tc.ItemSize.Height + tc.Padding.Y * 2;
        var stripRect = new Rectangle(0, 0, tc.Width, tabStripHeight);
        using var bgBrush = new SolidBrush(p.Background);
        e.Graphics.FillRectangle(bgBrush, stripRect);
    }

    /// <summary>Owner-draws a single tab page item with selected/unselected colors and accent highlight.</summary>
    private static void OnTabDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tc) return;

        var p = DesignerSafeThemeService.Current;
        var tabRect = tc.GetTabRect(e.Index);
        var isSelected = e.Index == tc.SelectedIndex;

        using (var bgBrush = new SolidBrush(isSelected ? p.PanelBackground : p.Background))
            e.Graphics.FillRectangle(bgBrush, tabRect);

        using var borderPen = new Pen(p.GridLine, 1);
        e.Graphics.DrawRectangle(borderPen, tabRect.X, tabRect.Y, tabRect.Width - 1, tabRect.Height - 1);

        if (isSelected)
        {
            using var accentPen = new Pen(p.Accent, 2);
            e.Graphics.DrawLine(accentPen, tabRect.X + 2, tabRect.Y, tabRect.Right - 3, tabRect.Y);
        }

        var textRect = new Rectangle(tabRect.X + 8, tabRect.Y + 4, tabRect.Width - 16, tabRect.Height - 8);
        var textColor = isSelected ? p.Foreground : p.DimForeground;
        TextRenderer.DrawText(
            e.Graphics,
            tc.TabPages[e.Index].Text,
            p.GridFont,
            textRect,
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
        );
    }
}
