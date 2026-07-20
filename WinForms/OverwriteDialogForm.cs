using CoderCommander.Operations;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

public class OverwriteDialogForm : ThemedForm
{
    public int Result { get; private set; } = 2;

    public OverwriteDialogForm(string fileName, string sourceInfo, string destInfo)
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        Text = L.GetString("OverwriteDlg.Title");
        ClientSize = new Size(500, 292); // +16 to match the button row's 72→88 growth below
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = p.Background;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0),
            BackColor = p.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // 88, not the original 72: with 2 rows of buttons, panel Padding(12,6,12,8) and each
        // button's Margin(2), 72 left ~25px per button row - under the 30px floor
        // ThemeSingleControl tries to enforce (which is a no-op anyway on a Dock=Fill button
        // inside a TableLayoutPanel cell, since Dock overrides an explicit Height).
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));

        // ── Content ──
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(16, 12, 16, 8),
            BackColor = p.Background
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var fileLabel = new Label
        {
            Text = fileName,
            Font = p.GridFont,
            ForeColor = p.Foreground,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        content.Controls.Add(fileLabel, 0, 0);

        content.Controls.Add(CreateInfoBox(L.GetString("OverwriteDlg.Source"), sourceInfo, p), 0, 1);

        var vsLabel = new Label
        {
            Text = L.GetString("OverwriteDlg.Vs"),
            Font = p.ItalicFont,
            ForeColor = p.DimForeground,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Tag = ThemeRole.Hint
        };
        content.Controls.Add(vsLabel, 0, 2);

        content.Controls.Add(CreateInfoBox(L.GetString("OverwriteDlg.Destination"), destInfo, p), 0, 3);

        root.Controls.Add(content, 0, 0);

        // ── Buttons: 3 cols x 2 rows ──
        var btnPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = p.HeaderBackground,
            Tag = ThemeRole.HeaderBackground,
            Padding = new Padding(12, 6, 12, 8)
        };

        var btnGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = Color.Transparent,
            Padding = new Padding(0)
        };
        btnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        btnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        btnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        btnGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        btnGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var policies = new (string text, int result, bool isDefault, int col, int row)[]
        {
            (L.GetString("Overwrite.Overwrite"), (int)OverwriteAction.Overwrite, true, 0, 0),
            (L.GetString("Overwrite.Skip"), (int)OverwriteAction.Skip, false, 1, 0),
            (L.GetString("Overwrite.Rename"), (int)OverwriteAction.Rename, false, 2, 0),
            (L.GetString("Overwrite.OverwriteAll"), (int)OverwriteAction.OverwriteAll, false, 0, 1),
            (L.GetString("Overwrite.SkipAll"), (int)OverwriteAction.SkipAll, false, 1, 1),
            (L.GetString("Overwrite.OverwriteOlder"), (int)OverwriteAction.OverwriteOlder, false, 2, 1),
        };

        foreach (var (text, result, isDefault, col, row) in policies)
        {
            var btn = ThemedForm.CreateThemedButton(text, accent: isDefault);
            btn.Dock = DockStyle.Fill;
            btn.Margin = new Padding(2);
            btn.Click += (_, _) =>
            {
                Result = result;
                DialogResult = DialogResult.OK;
                Close();
            };
            btnGrid.Controls.Add(btn, col, row);
        }

        btnPanel.Controls.Add(btnGrid);
        root.Controls.Add(btnPanel, 0, 1);

        Controls.Add(root);

        // No policy button maps to "cancel" semantically, but Escape should still close the
        // dialog (the caller already treats a non-OK result as "skip this file").
        var escapeBtn = new Button { DialogResult = DialogResult.Cancel, Visible = false };
        Controls.Add(escapeBtn);
        CancelButton = escapeBtn;
    }

    private static Panel CreateInfoBox(string title, string value, ThemePalette p)
    {
        var box = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = p.PanelBackground,
            Tag = ThemeRole.PanelBackground,
            Padding = new Padding(10, 5, 10, 5)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titleLabel = new Label
        {
            Text = title + ":",
            Font = p.GridFontBold,
            ForeColor = p.Foreground,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Tag = ThemeRole.Emphasis
        };

        var valueLabel = new Label
        {
            Text = value,
            Font = p.GridFont,
            ForeColor = p.Foreground,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(valueLabel, 1, 0);
        box.Controls.Add(layout);
        return box;
    }
}
