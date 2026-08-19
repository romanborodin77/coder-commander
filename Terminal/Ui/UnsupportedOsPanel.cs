using CoderCommander.Services;
using CoderCommander.Utils;
using CoderCommander.WinForms;

namespace CoderCommander.Terminal.Ui;

/// <summary>
/// Shown in place of the terminal canvas when <see cref="OsVersion.IsConPtySupported"/> is false
/// (Windows before build 17763/version 1809 - no ConPTY API). There is deliberately no fallback to
/// the old pipe-based implementation; per the approved rewrite plan this is the only code path for
/// an unsupported OS.
/// </summary>
internal sealed class UnsupportedOsPanel : Panel
{
    private readonly Label _messageLabel;

    public UnsupportedOsPanel()
    {
        Dock = DockStyle.Fill;

        _messageLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(24),
        };
        Controls.Add(_messageLabel);

        ApplyTheme();
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    private void ApplyTheme()
    {
        if (IsDisposed) return;
        var p = ThemeService.Current;
        BackColor = p.Background;
        _messageLabel.BackColor = p.Background;
        _messageLabel.ForeColor = p.DimForeground;
        _messageLabel.Font = p.GridFont;
        _messageLabel.Text = LocalizationService.Current.GetString("Terminal.UnsupportedOs");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeService.ThemeChanged -= OnThemeChanged;
            _messageLabel?.Dispose();
        }
        base.Dispose(disposing);
    }
}
