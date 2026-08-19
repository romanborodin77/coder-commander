using CoderCommander.Archives;
using CoderCommander.Utils;
using System.Reflection;
using System.Runtime.InteropServices;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// About dialog: the app mark and identity in a banner, then the environment facts someone
/// actually needs when reporting a problem (runtime, OS, architecture, memory, settings folder),
/// all copyable to the clipboard in one click.
/// </summary>
public sealed class AboutForm : ThemedForm
{
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private readonly LogoBanner _banner;
    private readonly List<(Label Caption, Label Value)> _infoRows = new();
    private readonly Panel _btnPanel;
    private readonly Button _copyBtn;
    private double _opacity;

    /// <summary>Initializes the about dialog with version info, environment facts, links, and
    /// the fade-in animation.</summary>
    public AboutForm()
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        Text = L.GetString("About.Title");
        ClientSize = new Size(520, 484);
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = p.Background;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Opacity = 0;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0),
        };
        root.SetRole(ThemeRole.Background);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132)); // banner
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // info grid
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // links
        // 68, not 60: the bar's own margin (3+3), padding (10+10) and 1px separator eat 27px,
        // and the buttons need 40 (34 tall plus 3+3 margin). At 60 the flow panel ended up 33
        // tall and clipped the bottom 4px off both buttons - caught by check_layout, invisible
        // enough by eye to have shipped.
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));  // buttons

        _banner = new LogoBanner { Dock = DockStyle.Fill };
        root.Controls.Add(_banner, 0, 1);   // Dock=Fill sibling first (docking-order pitfall)
        root.Controls.Add(BuildInfoGrid(), 0, 1);
        root.SetRow(_banner, 0);

        root.Controls.Add(BuildLinks(), 0, 2);

        _btnPanel = BuildButtonBar(out _copyBtn);
        root.Controls.Add(_btnPanel, 0, 3);

        Controls.Add(root);

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

    // ── Content ─────────────────────────────────────────────────────────────────────────────

    private static string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    private static string BuildConfiguration =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Release";

    /// <summary>The facts worth pasting into a bug report - deliberately environment, not
    /// marketing, and including the two capabilities that actually vary between machines and
    /// change how this app behaves (whether the OS can host the ConPTY terminal, and which
    /// archive formats this build registered). Regenerated on each call so Memory is current.</summary>
    private IEnumerable<(string CaptionKey, string Value)> EnvironmentFacts()
    {
        var L = LocalizationService.Current;

        yield return ("About.Runtime", RuntimeInformation.FrameworkDescription);
        yield return ("About.Os", RuntimeInformation.OSDescription);
        yield return ("About.Architecture",
            $"{RuntimeInformation.OSArchitecture} / {RuntimeInformation.ProcessArchitecture}");
        yield return ("About.Display", $"{DeviceDpi} dpi · {DeviceDpi * 100 / 96}%");
        yield return ("About.Terminal", OsVersion.IsConPtySupported
            ? $"ConPTY · build {Environment.OSVersion.Version.Build}"
            : $"{L.GetString("About.NotAvailable")} · build {OsVersion.MinConPtyBuild}+");
        var formats = string.Join(", ", ArchiveFormatRegistry.Registered.Select(f => f.Id));
        yield return ("About.Formats",
            formats.Length > 0 ? formats : L.GetString("About.NotAvailable"));
        yield return ("About.Memory", FormatUtils.FormatSize(GC.GetTotalMemory(forceFullCollection: false)));
        yield return ("About.ConfigFolder", SettingsFolder());
    }

    private static string SettingsFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoderCommander");

    private Control BuildInfoGrid()
    {
        var L = LocalizationService.Current;
        var facts = EnvironmentFacts().ToList();

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            // One extra, percent-sized row below the facts absorbs the leftover height. Without
            // it TableLayoutPanel hands the slack to the last *fact* row, which drifts away from
            // the rest of the list instead of the block staying together at the top.
            RowCount = facts.Count + 1,
            Padding = new Padding(28, 14, 28, 6),
        };
        grid.SetRole(ThemeRole.Background);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (int i = 0; i < facts.Count; i++)
        {
            var (captionKey, value) = facts[i];
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

            var caption = new Label
            {
                Text = L.GetString(captionKey),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
            };
            caption.SetRole(ThemeRole.Muted);

            var val = new Label
            {
                Text = value,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
            };
            val.SetRole(ThemeRole.Body);
            // The full value is often wider than the dialog (OS description, settings path),
            // and AutoEllipsis alone would just hide it.
            _toolTip.SetToolTip(val, value);

            grid.Controls.Add(caption, 0, i);
            grid.Controls.Add(val, 1, i);
            _infoRows.Add((caption, val));
        }
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        return grid;
    }

    private readonly ToolTip _toolTip = new();

    private Control BuildLinks()
    {
        var p = ThemeService.Current;
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(28, 0, 28, 0),
        };
        panel.SetRole(ThemeRole.Background);

        var license = CreateLinkLabel("MIT License");
        license.Click += (_, _) => OpenUrl("https://opensource.org/licenses/MIT");
        panel.Controls.Add(license);

        var separator = new Label
        {
            Text = "•",
            AutoSize = true,
            Margin = new Padding(14, 3, 14, 0),
        };
        // Without a role, ControlThemer's untagged-Label fallback resets ForeColor to the bright
        // p.Foreground on the next live theme switch, so this bullet would stop reading as a
        // separator and compete with the links.
        separator.SetRole(ThemeRole.Muted);
        panel.Controls.Add(separator);

        var github = CreateLinkLabel("GitHub");
        github.Click += (_, _) => OpenUrl("https://github.com");
        panel.Controls.Add(github);

        var separator2 = new Label
        {
            Text = "•",
            AutoSize = true,
            Margin = new Padding(14, 3, 14, 0),
        };
        separator2.SetRole(ThemeRole.Muted);
        panel.Controls.Add(separator2);

        var folder = CreateLinkLabel(LocalizationService.Current.GetString("About.ConfigFolder"));
        folder.Click += (_, _) => OpenUrl(SettingsFolder());
        panel.Controls.Add(folder);

        return panel;
    }

    private Panel BuildButtonBar(out Button copyBtn)
    {
        var L = LocalizationService.Current;
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 10, 20, 10),
        };
        panel.SetRole(ThemeRole.HeaderBackground);

        var topSep = new Panel { Dock = DockStyle.Top, Height = 1 };
        topSep.SetRole(ThemeRole.PanelBackground);

        // Margin is ignored on a control docked straight into a Panel, so the buttons live in a
        // docked FlowLayoutPanel to get the gap between them.
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            // Without GrowAndShrink the panel keeps its GrowOnly default and doesn't tighten to
            // the buttons, so they end up reported (and laid out) past its edge - the same
            // combination StyledMessageBoxForm.BuildButtonBar uses for this exact reason.
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
        };
        buttons.SetRole(ThemeRole.HeaderBackground);

        // Height-only override (not Size) - CreateThemedButton already measures each button's own
        // text and sizes its Width to fit; a fixed Size here previously clobbered that back down to
        // a hardcoded width, truncating "Скопировать сведения" ("Copy info") under Russian to
        // "Скопировать све..." (caught by visual inspection of a live build).
        copyBtn = ThemedForm.CreateThemedButton(L.GetString("About.CopyInfo"));
        copyBtn.Height = 34;
        copyBtn.Margin = new Padding(0, 3, 10, 3);
        var captured = copyBtn;
        copyBtn.Click += (_, _) => CopyDiagnosticsToClipboard(captured);

        var closeBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Close"), accent: true);
        closeBtn.Height = 34;
        closeBtn.Margin = new Padding(0, 3, 0, 3);
        closeBtn.Click += (_, _) => Close();
        CancelButton = closeBtn;
        AcceptButton = closeBtn;

        buttons.Controls.Add(copyBtn);
        buttons.Controls.Add(closeBtn);

        panel.Controls.Add(buttons);
        panel.Controls.Add(topSep);
        return panel;
    }

    /// <summary>Puts the same facts the dialog shows on the clipboard as plain text, so a bug
    /// report doesn't depend on the reporter retyping a version string correctly.</summary>
    private void CopyDiagnosticsToClipboard(Button button)
    {
        var L = LocalizationService.Current;
        var lines = new List<string>
        {
            $"{L.GetString("About.AppName")} {AppVersion} ({BuildConfiguration})",
        };
        lines.AddRange(EnvironmentFacts().Select(f => $"{L.GetString(f.CaptionKey)}: {f.Value}"));

        if (!ClipboardHelper.TrySetClipboard(string.Join(Environment.NewLine, lines)))
            return;

        // Inline confirmation rather than a message box: the feedback belongs where the click
        // happened, and a modal over the About dialog would be noise for a copy.
        var original = button.Text;
        button.Text = L.GetString("About.Copied");
        button.Enabled = false;
        // Self-disposes from its own Tick handler below, at most 1400ms after this call - the
        // analyzer can't trace disposal happening inside the timer's own event.
#pragma warning disable CA2000
        var revert = new System.Windows.Forms.Timer { Interval = 1400 };
#pragma warning restore CA2000
        revert.Tick += (s, _) =>
        {
            revert.Stop();
            revert.Dispose();
            if (button.IsDisposed) return;
            button.Text = original;
            button.Enabled = true;
        };
        revert.Start();
    }

    protected override void ApplyTheme()
    {
        base.ApplyTheme();
        // Every label/panel here carries a ThemeRole, so ControlThemer's generic pass handles
        // them; the banner paints itself from the palette and only needs a repaint.
        _banner?.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fadeTimer?.Dispose();
            _toolTip.Dispose();
            _banner?.Dispose();
            _btnPanel?.Dispose();
        }
        base.Dispose(disposing);
    }

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

    private static LinkLabel CreateLinkLabel(string text)
    {
        var p = ThemeService.Current;
        var link = new LinkLabel
        {
            Text = text,
            LinkColor = p.Accent,
            ActiveLinkColor = p.Accent,
            VisitedLinkColor = p.Accent,
            Font = p.LinkFont,
            AutoSize = true,
            Cursor = Cursors.Hand,
            LinkBehavior = LinkBehavior.NeverUnderline,
        };
        // Read live from ThemeService.Current rather than the colors captured at construction -
        // otherwise hovering after a theme switch would flip back to the old theme's accent.
        link.MouseEnter += (_, _) => link.LinkColor = ThemeService.Current.AccentHover;
        link.MouseLeave += (_, _) => link.LinkColor = ThemeService.Current.Accent;
        return link;
    }

    // ── Banner ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The dialog's header: the app mark, name, tagline and version, over a header-coloured
    /// surface with an oversized, very faint copy of the mark bled off the right edge. Owner-drawn
    /// as one control so the watermark can sit behind the text without a stack of overlapping
    /// transparent labels.
    /// </summary>
    private sealed class LogoBanner : Control
    {
        private const int LogoSize = 72;

        public LogoBanner()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = ThemeService.Current;
            var L = LocalizationService.Current;
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var bg = new SolidBrush(p.HeaderBackground))
                g.FillRectangle(bg, ClientRectangle);

            // Watermark: the whole mark again, oversized but faint, in the palette's gloss
            // overlay (white-on-dark, black-on-light) so it reads as an emboss in either theme
            // rather than a hardcoded wash that only works on one. Sized to stay a complete,
            // recognisable { / } - cropping it to a fragment just looked like a paint glitch.
            var markSize = (int)(Height * 1.35f);
            using (var watermark = AppLogo.RenderGlyph(markSize, p.GlossOverlay, strokeWidth: 1.8f))
                g.DrawImage(watermark, Width - markSize - 18, (Height - markSize) / 2, markSize, markSize);

            var logoY = (Height - LogoSize) / 2;
            using (var logo = AppLogo.Render(LogoSize))
                g.DrawImage(logo, 28, logoY, LogoSize, LogoSize);

            int textX = 28 + LogoSize + 20;
            int textW = Math.Max(40, Width - textX - 20);

            var flags = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, L.GetString("About.AppName"), p.TitleFont,
                new Rectangle(textX, logoY - 2, textW, 28), p.Foreground, flags);
            TextRenderer.DrawText(g, L.GetString("About.Subtitle"), p.GridFont,
                new Rectangle(textX, logoY + 26, textW, 22), p.DimForeground, flags);
            TextRenderer.DrawText(g, $"{L.GetString("About.Version", AppVersion)} · {BuildConfiguration}",
                p.GridFont, new Rectangle(textX, logoY + 48, textW, 22), p.Accent, flags);

            using var accent = new Pen(p.Accent, 3f);
            g.DrawLine(accent, 0, Height - 2, Width, Height - 2);
        }
    }
}
