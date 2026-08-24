using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Always-on-top HUD for <see cref="LayoutEditModeService"/> - shows the currently-selected
/// control's geometry (Type/Name/Bounds/Margin/Padding/Dock/Anchor, plus its TableLayoutPanel
/// row/column Absolute size when applicable) and a Copy button that mirrors the Ctrl+C export
/// shortcut. Pinned to a screen corner, deliberately far from where this app's own dialogs center,
/// so it can never itself overlap (and corrupt) a <see cref="LayoutEditHighlight"/> XOR frame.
///
/// Inherits <see cref="ThemedForm"/>, not a bare <see cref="Form"/> - CLAUDE.md has no dev-tooling
/// carve-out for that rule, and ThemedForm's own constructor already gives dark-title-bar/live
/// re-theming for free; only a handful of properties need overriding afterward to turn a normal
/// centered dialog into a borderless always-on-top HUD.
/// </summary>
internal sealed class LayoutEditHud : ThemedForm
{
    private readonly Label _typeLabel;
    private readonly Label _nameLabel;
    private readonly Label _boundsLabel;
    private readonly Label _marginLabel;
    private readonly Label _paddingLabel;
    private readonly Label _dockAnchorLabel;
    private readonly Label _tableLabel;

    public LayoutEditHud()
    {
        Text = "Layout Edit Mode";
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Resizable = false;
        ClientSize = new Size(320, 220);

        var p = ThemeService.Current;
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.Right - Width - 8, wa.Top + 8);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(12),
            BackColor = p.HeaderBackground,
        };
        layout.SetRole(ThemeRole.HeaderBackground);

        var title = UiHelpers.CreateLabel("Layout Edit Mode - F11 off, Esc deselect, Ctrl+C copy", bold: true);
        _typeLabel = UiHelpers.CreateLabel("");
        _nameLabel = UiHelpers.CreateLabel("");
        _boundsLabel = UiHelpers.CreateLabel("");
        _marginLabel = UiHelpers.CreateLabel("");
        _paddingLabel = UiHelpers.CreateLabel("");
        _dockAnchorLabel = UiHelpers.CreateLabel("");
        _tableLabel = UiHelpers.CreateLabel("");

        foreach (var lbl in new[] { _typeLabel, _nameLabel, _boundsLabel, _marginLabel, _paddingLabel, _dockAnchorLabel, _tableLabel })
            lbl.Tag = ThemeRole.Muted;

        var copyBtn = CreateThemedButton("Copy snippet (Ctrl+C)");
        copyBtn.Margin = new Padding(0, 8, 0, 0);
        copyBtn.Click += (_, _) => LayoutEditModeService.ExportToClipboard();

        layout.Controls.Add(title);
        layout.Controls.Add(_typeLabel);
        layout.Controls.Add(_nameLabel);
        layout.Controls.Add(_boundsLabel);
        layout.Controls.Add(_marginLabel);
        layout.Controls.Add(_paddingLabel);
        layout.Controls.Add(_dockAnchorLabel);
        layout.Controls.Add(_tableLabel);
        layout.Controls.Add(copyBtn);

        Controls.Add(layout);

        UpdateFor((Control?)null);
    }

    /// <summary>Explicit disposal of the label fields (CA2213) - same accepted pattern as every
    /// other dialog in this codebase (e.g. CustomShellEditForm): added to Controls at construction,
    /// still disposed by name here, since the analyzer doesn't infer ownership from Controls.Add.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _typeLabel.Dispose();
            _nameLabel.Dispose();
            _boundsLabel.Dispose();
            _marginLabel.Dispose();
            _paddingLabel.Dispose();
            _dockAnchorLabel.Dispose();
            _tableLabel.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Repaints every row for the newly-selected control (or clears them all when
    /// <paramref name="selected"/> is null). Called by <see cref="LayoutEditModeService"/> on
    /// selection change and after every nudge, so the HUD always reflects live state with no
    /// polling. Shows the CURRENT geometry only - the baseline snapshot is compared separately, in
    /// <see cref="LayoutEditModeService.ExportToClipboard"/>, not displayed live here.</summary>
    public void UpdateFor(Control? selected)
    {
        if (selected is null)
        {
            _typeLabel.Text = "(no selection - click a control)";
            _nameLabel.Text = _boundsLabel.Text = _marginLabel.Text = _paddingLabel.Text =
                _dockAnchorLabel.Text = _tableLabel.Text = "";
            return;
        }

        _typeLabel.Text = $"Type: {selected.GetType().Name}";
        _nameLabel.Text = $"Name: {(string.IsNullOrEmpty(selected.Name) ? "(none)" : selected.Name)}";
        _boundsLabel.Text = $"Bounds: {selected.Bounds.X},{selected.Bounds.Y} {selected.Bounds.Width}x{selected.Bounds.Height}";
        _marginLabel.Text = $"Margin: {selected.Margin.Left},{selected.Margin.Top},{selected.Margin.Right},{selected.Margin.Bottom}";
        _paddingLabel.Text = $"Padding: {selected.Padding.Left},{selected.Padding.Top},{selected.Padding.Right},{selected.Padding.Bottom}";
        _dockAnchorLabel.Text = $"Dock: {selected.Dock}   Anchor: {selected.Anchor}   AutoSize: {selected.AutoSize}";

        if (selected.Parent is TableLayoutPanel tlp)
        {
            var pos = tlp.GetPositionFromControl(selected);
            var rowText = pos.Row >= 0 && pos.Row < tlp.RowStyles.Count
                ? DescribeStyle(tlp.RowStyles[pos.Row].SizeType, tlp.RowStyles[pos.Row].Height)
                : "?";
            var colText = pos.Column >= 0 && pos.Column < tlp.ColumnStyles.Count
                ? DescribeStyle(tlp.ColumnStyles[pos.Column].SizeType, tlp.ColumnStyles[pos.Column].Width)
                : "?";
            _tableLabel.Text = $"Row {pos.Row}: {rowText}   Col {pos.Column}: {colText}";
        }
        else
        {
            _tableLabel.Text = "";
        }
    }

    private static string DescribeStyle(SizeType type, float value) => type == SizeType.Absolute
        ? $"Absolute {value}px"
        : $"{type} - not nudgeable";

    /// <summary>Same job as <see cref="UpdateFor(Control?)"/>, for a
    /// <see cref="Services.LayoutEditModeService.SelectedItem"/> - a ToolStripItem has no Dock/
    /// Anchor/TableLayoutPanel cell concept, so those rows are blanked instead of populated.</summary>
    public void UpdateFor(ToolStripItem? selected)
    {
        if (selected is null)
        {
            UpdateFor((Control?)null);
            return;
        }

        _typeLabel.Text = $"Type: {selected.GetType().Name} (ToolStripItem)";
        _nameLabel.Text = $"Name: {(string.IsNullOrEmpty(selected.Name) ? "(none)" : selected.Name)}";
        _boundsLabel.Text = $"Bounds: {selected.Bounds.X},{selected.Bounds.Y} {selected.Bounds.Width}x{selected.Bounds.Height}";
        _marginLabel.Text = $"Margin: {selected.Margin.Left},{selected.Margin.Top},{selected.Margin.Right},{selected.Margin.Bottom}";
        _paddingLabel.Text = $"Padding: {selected.Padding.Left},{selected.Padding.Top},{selected.Padding.Right},{selected.Padding.Bottom}";
        _dockAnchorLabel.Text = "No Dock/Anchor - positioned by the ToolStrip's own layout";
        _tableLabel.Text = "";
    }
}
