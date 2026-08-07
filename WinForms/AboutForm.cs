using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// About dialog with animated logo, fade-in effect, and themed interactive elements.
/// </summary>
public class AboutForm : ThemedForm
{
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private readonly PictureBox _logoBox;
    private readonly Panel _logoPanel;
    private readonly Label _appNameLabel;
    private readonly Label _techLabel;
    private readonly Panel _btnPanel;
    private readonly LinkLabel _licenseLink;
    private readonly LinkLabel _githubLink;
    private double _opacity = 0.0;

    /// <summary>Initializes the about dialog with version info, links, and fade-in animation.</summary>
    public AboutForm()
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        Text = L.GetString("About.Title");
        ClientSize = new Size(480, 400);
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = p.Background;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Opacity = 0;

        // Main vertical layout
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0),
            BackColor = p.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));  // Logo area
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Info (fills)
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));   // Links
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));   // Close button

        // ── Logo area with accent bottom border ──
        _logoPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = p.HeaderBackground,
            Tag = ThemeRole.HeaderBackground
        };

        _logoBox = new PictureBox
        {
            Size = new Size(96, 96),
            SizeMode = PictureBoxSizeMode.StretchImage,
            BackColor = Color.Transparent
        };
        // Center the logo icon
        _logoBox.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (_logoBox.Image != null)
            {
                var x = (_logoBox.Width - 80) / 2;
                var y = (20);
                e.Graphics.DrawImage(_logoBox.Image, x, y, 80, 80);
            }
        };
        _logoBox.Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath)?.ToBitmap();
        _logoPanel.Controls.Add(_logoBox);

        _appNameLabel = new Label
        {
            Text = L.GetString("About.AppName"),
            Font = p.TitleFont,
            ForeColor = p.Foreground,
            Dock = DockStyle.Bottom,
            Height = 32,
            TextAlign = ContentAlignment.MiddleCenter,
            Tag = ThemeRole.Title
        };
        _logoPanel.Controls.Add(_appNameLabel);

        // Accent line at bottom of logo area
        var accentLine = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 3,
            BackColor = p.Accent
        };
        _logoPanel.Controls.Add(accentLine);

        root.Controls.Add(_logoPanel, 0, 0);

        // ── Info ──
        var infoPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(32, 16, 32, 8),
            BackColor = p.Background
        };
        infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        infoPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        infoPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        infoPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        infoPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        var versionLabel = new Label
        {
            Text = L.GetString("About.Version", version),
            Font = p.GridFont,
            ForeColor = p.DimForeground,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Tag = ThemeRole.Muted
        };

        var subtitleLabel = new Label
        {
            Text = L.GetString("About.Subtitle"),
            Font = p.GridFont,
            ForeColor = p.Foreground,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Tag = ThemeRole.Body
        };

        var descLabel = new Label
        {
            Text = L.GetString("About.Description"),
            Font = p.GridFont,
            ForeColor = p.DimForeground,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Tag = ThemeRole.Muted
        };

        // MonoFont + Accent doesn't map to any general text role, so this one label is patched
        // directly in ApplyTheme() below - same pattern as _appNameLabel used to need for all
        // three of its properties before Title covered it.
        _techLabel = new Label
        {
            Text = L.GetString("About.Tech"),
            Font = p.MonoFont,
            ForeColor = p.Accent,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };

        infoPanel.Controls.Add(versionLabel, 0, 0);
        infoPanel.Controls.Add(subtitleLabel, 0, 1);
        infoPanel.Controls.Add(descLabel, 0, 2);
        infoPanel.Controls.Add(_techLabel, 0, 3);

        root.Controls.Add(infoPanel, 0, 1);

        // ── Links ─
        var linksPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = p.Background,
            Padding = new Padding(24, 0, 24, 0)
        };

        _licenseLink = CreateLinkLabel("MIT License", p.Accent, p.AccentHover);
        _licenseLink.Click += (_, _) => OpenUrl("https://opensource.org/licenses/MIT");
        linksPanel.Controls.Add(_licenseLink);

        // Without a role, ControlThemer's untagged-Label fallback (ApplyLabelRole's default
        // case) resets ForeColor to the bright p.Foreground on the next live theme switch -
        // this bullet separator would flip from muted to prominent instead of staying muted
        // (found via the dotnet-debugger MCP server's check_layout()).
        var separator1 = new Label
        {
            Text = "\u2022",
            ForeColor = p.DimForeground,
            Font = p.GridFont,
            AutoSize = true,
            Margin = new Padding(14, 4, 14, 0),
            Tag = ThemeRole.Muted
        };
        linksPanel.Controls.Add(separator1);

        _githubLink = CreateLinkLabel("GitHub", p.Accent, p.AccentHover);
        _githubLink.Click += (_, _) => OpenUrl("https://github.com");
        linksPanel.Controls.Add(_githubLink);

        root.Controls.Add(linksPanel, 0, 2);

        // ── Close button panel ──
        _btnPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = p.HeaderBackground,
            Padding = new Padding(20, 8, 20, 8)
        };

        // Top separator line
        var topSep = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = p.GridLine
        };
        _btnPanel.Controls.Add(topSep);

        var closeBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Close"), accent: true);
        closeBtn.Size = new Size(120, 36);
        closeBtn.Anchor = AnchorStyles.None;
        closeBtn.Click += (_, _) => Close();
        _btnPanel.Controls.Add(closeBtn);

        CancelButton = closeBtn;

        // Center button on resize
        _btnPanel.Resize += (_, _) =>
        {
            closeBtn.Location = new Point(
                (_btnPanel.ClientSize.Width - closeBtn.Width) / 2,
                (_btnPanel.ClientSize.Height - closeBtn.Height) / 2);
        };

        root.Controls.Add(_btnPanel, 0, 3);

        Controls.Add(root);

        // ── Fade-in animation ──
        _fadeTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _fadeTimer.Tick += (_, _) =>
        {
            _opacity += 0.08;
            if (_opacity >= 1.0)
            {
                _opacity = 1.0;
                _fadeTimer.Stop();
            }
            Opacity = _opacity;
        };
        _fadeTimer.Start();

        FormClosing += (_, _) =>
        {
            _fadeTimer.Stop();
            _fadeTimer.Dispose();
        };
    }

    protected override void ApplyTheme()
    {
        base.ApplyTheme();
        // _logoPanel/_btnPanel now carry ThemeRole.HeaderBackground and _appNameLabel carries
        // ThemeRole.Title, so ControlThemer's generic pass already re-colors them correctly;
        // _licenseLink/_githubLink are plain LinkLabels, which ControlThemer also handles now.
        // _techLabel's MonoFont+Accent combination doesn't map to any general text role, so it's
        // the one thing still patched by hand here.
        var p = ThemeService.Current;
        _techLabel.Font = p.MonoFont;
        _techLabel.ForeColor = p.Accent;
    }

    /// <summary>Opens a URL in the default browser.</summary>
    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to open {url}: {ex.Message}", ex);
        }
    }

    /// <summary>Creates a themed <see cref="LinkLabel"/> with hover color tracking.</summary>
    private static LinkLabel CreateLinkLabel(string text, Color color, Color hoverColor)
    {
        var p = ThemeService.Current;
        var link = new LinkLabel
        {
            Text = text,
            LinkColor = color,
            ActiveLinkColor = color,
            VisitedLinkColor = color,
            Font = p.LinkFont,
            AutoSize = true,
            Cursor = Cursors.Hand,
            LinkBehavior = LinkBehavior.NeverUnderline
        };
        // Read live from ThemeService.Current rather than the colors captured at construction -
        // otherwise hovering after a theme switch would flip back to the old theme's accent color.
        link.MouseEnter += (_, _) => link.LinkColor = ThemeService.Current.AccentHover;
        link.MouseLeave += (_, _) => link.LinkColor = ThemeService.Current.Accent;
        return link;
    }
}
