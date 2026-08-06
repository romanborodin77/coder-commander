using System.Text;
using System.Text.Json;

namespace CoderCommander.Services;

/// <summary>
/// Dumps the live Control tree (Bounds/Margin/Padding/Font/colors/ThemeRole/Dock/Anchor) of the
/// currently active top-level form to a JSON file, for the dotnet-debugger MCP server's layout
/// checker (check_layout()/audit_all_dialogs()). UIA alone can't see this: it exposes screen
/// rects but not Margin/Padding/ThemeRole/exact Font, and owner-drawn controls (RoundedButton,
/// CodeEditorCanvas, ThemedScrollBar) are barely represented in the UIA tree at all.
///
/// Only wired up when CODERCOMMANDER_UI_DEBUG=1 is set in the process environment (see
/// Program.cs) - an ordinary user run never touches this. Triggered by an unmodified F12
/// keypress rather than a hotkey with modifiers: modifier combos posted via the debugger's
/// PostMessage-based input have been empirically unreliable (see
/// .claude/mcp/dotnet-debugger/README.md's "Known limitation"), while plain keys work.
/// </summary>
public static class UiDumpService
{
    public static string DumpPath { get; } = Path.Combine(Path.GetTempPath(), "CoderCommander_ui_dump.json");

    /// <summary>Dumps the currently active top-level form (or, if none is active, the first
    /// open form) to <see cref="DumpPath"/>. Safe to call from a UI-thread key handler only -
    /// touches Control properties directly, no synchronization.</summary>
    public static void DumpActiveFormToFile()
    {
        try
        {
            var form = Form.ActiveForm ?? Application.OpenForms.Cast<Form>().FirstOrDefault();
            if (form == null)
                return;

            var node = DumpControl(form);
            var json = JsonSerializer.Serialize(node, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(DumpPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            LogService.Debug($"UI tree dumped to {DumpPath} ({form.Name}/{form.Text})", "UiDump");
        }
        catch (Exception ex)
        {
            LogService.Error("UiDumpService.DumpActiveFormToFile failed", ex, "UiDump");
        }
    }

    private static Dictionary<string, object?> DumpControl(Control control)
    {
        var role = control.GetRole();
        var screenLocation = control.Parent != null ? control.PointToScreen(Point.Empty) : control.Location;

        var node = new Dictionary<string, object?>
        {
            ["type"] = control.GetType().Name,
            ["name"] = control.Name,
            ["text"] = Truncate(control.Text, 80),
            ["bounds"] = new Dictionary<string, int>
            {
                ["x"] = control.Bounds.X,
                ["y"] = control.Bounds.Y,
                ["width"] = control.Bounds.Width,
                ["height"] = control.Bounds.Height,
            },
            ["screen_bounds"] = new Dictionary<string, int>
            {
                ["left"] = screenLocation.X,
                ["top"] = screenLocation.Y,
                ["right"] = screenLocation.X + control.Width,
                ["bottom"] = screenLocation.Y + control.Height,
            },
            ["margin"] = new[] { control.Margin.Left, control.Margin.Top, control.Margin.Right, control.Margin.Bottom },
            ["padding"] = new[] { control.Padding.Left, control.Padding.Top, control.Padding.Right, control.Padding.Bottom },
            ["dock"] = control.Dock.ToString(),
            ["anchor"] = control.Anchor.ToString(),
            ["visible"] = control.Visible,
            ["enabled"] = control.Enabled,
            ["theme_role"] = role?.ToString(),
            ["font_family"] = control.Font.FontFamily.Name,
            ["font_size"] = control.Font.Size,
            ["font_style"] = control.Font.Style.ToString(),
            ["back_color"] = ColorHex(control.BackColor),
            ["fore_color"] = ColorHex(control.ForeColor),
            // A control whose BackColor/ForeColor was never explicitly set still reports the
            // inherited default (Control.DefaultBackColor/DefaultForeColor) rather than a
            // flag - comparing against those defaults is how the Python-side checker infers
            // "probably never themed" for a control with no ThemeRole tag. BackColor.A == 0
            // (Color.Transparent, common for a Label left to show its parent panel through)
            // is a second, distinct case of "not a real background" - flagged separately so
            // the contrast checker walks up to the parent instead of comparing against a
            // fully see-through color.
            ["is_default_back_color"] = control.BackColor == Control.DefaultBackColor,
            ["is_default_fore_color"] = control.ForeColor == Control.DefaultForeColor,
            ["back_color_transparent"] = control.BackColor.A == 0,
        };

        if (control is Button or Label or LinkLabel && control.Tag is not ThemeRole)
            node["untagged_style_prone"] = true;

        var children = new List<Dictionary<string, object?>>();
        foreach (Control child in control.Controls)
            children.Add(DumpControl(child));
        if (children.Count > 0)
            node["children"] = children;

        return node;
    }

    // 8-digit #AARRGGBB when not fully opaque, so a transparent color (A=0, e.g.
    // Color.Transparent - R=G=B=255 but invisible) doesn't get silently truncated into
    // "#ffffff", indistinguishable from a genuinely opaque white. This was a real bug: the
    // 6-digit-only version made a Label deliberately left BackColor=Transparent (showing its
    // dark parent panel through) look like it had a solid white background, producing a false
    // low-contrast finding in check_layout() - caught by cross-checking against a live
    // get_pixel() sample of the actually rendered pixel, which is never affected by this.
    private static string ColorHex(Color c) => c.A < 255
        ? $"#{c.A:x2}{c.R:x2}{c.G:x2}{c.B:x2}"
        : $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}
