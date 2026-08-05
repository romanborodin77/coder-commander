using CoderCommander.Archives;
using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Application settings dialog with tabbed sections.
/// </summary>
public class SettingsForm : ThemedForm
{
    private readonly ThemedTabControl _tabs;
    private readonly ThemedComboBox _themeCombo;
    private readonly ThemedComboBox _languageCombo;
    private readonly ThemedCheckBox _showHiddenCheck;
    private readonly ThemedCheckBox _showToolbarCheck;
    private readonly ThemedCheckBox _showStatusBarCheck;
    private readonly ThemedCheckBox _showFnButtonsCheck;
    private readonly ThemedCheckBox _dirsFirstCheck;
    private readonly ThemedComboBox _compressionFormatCombo;
    private readonly ThemedComboBox _compressionPresetCombo;
    private readonly List<IArchiveFormat> _compressionFormats;
    private readonly Dictionary<string, CompressionPreset> _workingCompression = new(StringComparer.OrdinalIgnoreCase);
    private readonly ThemedCheckBox _confirmDeleteCheck;
    private readonly ThemedCheckBox _confirmOverwriteCheck;
    private readonly ThemedCheckBox _copyAttrsCheck;
    private readonly ThemedCheckBox _copyTsCheck;
    private readonly ThemedCheckBox _showExtInNameCheck;
    private readonly ThemedComboBox _defaultShellCombo;

    /// <summary>Raised after settings are saved and applied.</summary>
    public event EventHandler? SettingsSaved;

    /// <summary>Initializes the settings dialog with current <see cref="AppSettings"/> values.</summary>
    public SettingsForm()
    {
        var L = LocalizationService.Current;
        Text = L.GetString("Settings.Title");
        ClientSize = new Size(560, 520);
        Resizable = false;

        var p = ThemeService.Current;
        var s = SettingsService.Load();

        _tabs = new ThemedTabControl
        {
            Dock = DockStyle.Fill,
            Font = p.GridFont,
            BackColor = p.Background
        };

        // ── Appearance tab ──
        var appearLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(16),
            BackColor = p.Background
        };
        appearLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        appearLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 9; i++)
            appearLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        appearLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        int row = 0;
        appearLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.Theme")), 0, row);
        _themeCombo = new ThemedComboBox { Dock = DockStyle.Fill };
        _themeCombo.AddItem(L.GetString("Settings.Theme.Dark"));
        _themeCombo.AddItem(L.GetString("Settings.Theme.Light"));
        _themeCombo.SelectedIndex = s.Theme == "Light" ? 1 : 0;
        appearLayout.Controls.Add(_themeCombo, 1, row);
        row++;

        appearLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.Language")), 0, row);
        _languageCombo = new ThemedComboBox { Dock = DockStyle.Fill };
        var languages = LocalizationService.Current.GetAvailableLanguages();
        var currentLangIndex = 0;
        for (int i = 0; i < languages.Count; i++)
        {
            var (code, name) = languages[i];
            _languageCombo.AddItem($"{name} ({code})");
            if (code == s.Language)
                currentLangIndex = i;
        }
        _languageCombo.SelectedIndex = currentLangIndex;
        appearLayout.Controls.Add(_languageCombo, 1, row);
        row++;

        _showHiddenCheck = UiHelpers.CreateCheckBox(L.GetString("Settings.ShowHidden"), s.ShowHidden);
        _showHiddenCheck.Dock = DockStyle.Fill;
        appearLayout.Controls.Add(_showHiddenCheck, 0, row);
        appearLayout.SetColumnSpan(_showHiddenCheck, 2);
        row++;

        _showToolbarCheck = UiHelpers.CreateCheckBox(L.GetString("Settings.ShowToolbar"), s.ShowToolbar);
        _showToolbarCheck.Dock = DockStyle.Fill;
        appearLayout.Controls.Add(_showToolbarCheck, 0, row);
        appearLayout.SetColumnSpan(_showToolbarCheck, 2);
        row++;

        _showStatusBarCheck = UiHelpers.CreateCheckBox(L.GetString("Settings.ShowStatusBar"), s.ShowStatusBar);
        _showStatusBarCheck.Dock = DockStyle.Fill;
        appearLayout.Controls.Add(_showStatusBarCheck, 0, row);
        appearLayout.SetColumnSpan(_showStatusBarCheck, 2);
        row++;

        _showFnButtonsCheck = UiHelpers.CreateCheckBox(L.GetString("Settings.ShowFunctionButtons"), s.ShowFunctionButtons);
        _showFnButtonsCheck.Dock = DockStyle.Fill;
        appearLayout.Controls.Add(_showFnButtonsCheck, 0, row);
        appearLayout.SetColumnSpan(_showFnButtonsCheck, 2);
        row++;

        _dirsFirstCheck = UiHelpers.CreateCheckBox(L.GetString("Settings.DirectoriesFirst"), s.DirectoriesFirst);
        _dirsFirstCheck.Dock = DockStyle.Fill;
        appearLayout.Controls.Add(_dirsFirstCheck, 0, row);
        appearLayout.SetColumnSpan(_dirsFirstCheck, 2);
        row++;

        _showExtInNameCheck = UiHelpers.CreateCheckBox(L.GetString("Settings.ShowExtInName"), s.ShowExtensionInName);
        _showExtInNameCheck.Dock = DockStyle.Fill;
        appearLayout.Controls.Add(_showExtInNameCheck, 0, row);
        appearLayout.SetColumnSpan(_showExtInNameCheck, 2);
        row++;

        _tabs.AddPage(new ThemedTabPage(L.GetString("Settings.Appearance"), appearLayout));

        // ── File Ops tab ──
        var fileOpsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(16),
            BackColor = p.Background
        };
        for (int i = 0; i < 6; i++)
            fileOpsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        fileOpsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _copyAttrsCheck = UiHelpers.CreateCheckBox(L.GetString("Settings.CopyAttributes"), s.CopyAttributes);
        _copyAttrsCheck.Dock = DockStyle.Fill;
        fileOpsLayout.Controls.Add(_copyAttrsCheck, 0, 0);

        _copyTsCheck = UiHelpers.CreateCheckBox(L.GetString("Settings.CopyTimestamps"), s.CopyTimestamps);
        _copyTsCheck.Dock = DockStyle.Fill;
        fileOpsLayout.Controls.Add(_copyTsCheck, 0, 1);

        // Per-format archive compression: a format list scoped to what PackDialogForm can actually
        // create (read-only formats like 7z/RAR have no compression to configure and would just
        // show a blank preset combo) + a preset combo scoped to whichever format is selected.
        // Built entirely from the registry, so a future creatable format gets a row here for free.
        var compressionFormatLabel = new Label
        {
            Text = L.GetString("Settings.ArchiveCompressionFormat"),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = p.Foreground,
            Font = ThemeService.Current.GridFont
        };
        fileOpsLayout.Controls.Add(compressionFormatLabel, 0, 2);

        _compressionFormats = ArchiveFormatRegistry.Creatable.ToList();
        _compressionFormatCombo = new ThemedComboBox { Dock = DockStyle.Fill };
        foreach (var format in _compressionFormats)
            _compressionFormatCombo.AddItem(L.GetString(format.DisplayNameKey));
        fileOpsLayout.Controls.Add(_compressionFormatCombo, 0, 3);

        var compressionPresetLabel = new Label
        {
            Text = L.GetString("Settings.ArchiveCompressionPreset"),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = p.Foreground,
            Font = ThemeService.Current.GridFont
        };
        fileOpsLayout.Controls.Add(compressionPresetLabel, 0, 4);

        _compressionPresetCombo = new ThemedComboBox { Dock = DockStyle.Fill };
        fileOpsLayout.Controls.Add(_compressionPresetCombo, 0, 5);

        foreach (var format in _compressionFormats)
        {
            if (format.SupportedPresets.Count == 0)
                continue; // read-only format - nothing to persist

            if (s.ArchiveCompression.TryGetValue(format.Id, out var presetName) &&
                Enum.TryParse<CompressionPreset>(presetName, out var parsed) &&
                format.SupportedPresets.Contains(parsed))
            {
                _workingCompression[format.Id] = parsed;
            }
            else
            {
                _workingCompression[format.Id] = DefaultPresetFor(format);
            }
        }

        _compressionFormatCombo.SelectedIndexChanged += (_, _) => LoadPresetComboForSelectedFormat();
        _compressionPresetCombo.SelectedIndexChanged += (_, _) => CommitSelectedPreset();
        if (_compressionFormats.Count > 0)
        {
            _compressionFormatCombo.SelectedIndex = 0;
            LoadPresetComboForSelectedFormat();
        }

        _tabs.AddPage(new ThemedTabPage(L.GetString("Settings.FileOps"), fileOpsLayout));

        // ── Confirmations tab ──
        var confLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16),
            BackColor = p.Background
        };
        for (int i = 0; i < 2; i++)
            confLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        confLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _confirmDeleteCheck = UiHelpers.CreateCheckBox(L.GetString("Settings.ConfirmDelete"), s.ConfirmDelete);
        _confirmDeleteCheck.Dock = DockStyle.Fill;
        confLayout.Controls.Add(_confirmDeleteCheck, 0, 0);

        _confirmOverwriteCheck = UiHelpers.CreateCheckBox(L.GetString("Settings.ConfirmOverwrite"), s.ConfirmOverwrite);
        _confirmOverwriteCheck.Dock = DockStyle.Fill;
        confLayout.Controls.Add(_confirmOverwriteCheck, 0, 1);

        _tabs.AddPage(new ThemedTabPage(L.GetString("Settings.Confirmations"), confLayout));

        // ── Terminal tab ──
        var terminalLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(16),
            BackColor = p.Background
        };
        terminalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        terminalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        terminalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        terminalLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        terminalLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.DefaultShell")), 0, 0);
        _defaultShellCombo = new ThemedComboBox { Dock = DockStyle.Fill };
        _defaultShellCombo.AddItem(L.GetString("Terminal.Cmd"));
        _defaultShellCombo.AddItem(L.GetString("Terminal.PowerShell"));
        var defaultShellIndex = s.DefaultShellType == "PowerShell" ? 1 : 0;
        _defaultShellCombo.SelectedIndex = defaultShellIndex;
        terminalLayout.Controls.Add(_defaultShellCombo, 1, 0);

        _tabs.AddPage(new ThemedTabPage(L.GetString("Settings.Terminal"), terminalLayout));

        // Bottom buttons
        var saveBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Save"), accent: true);
        saveBtn.Click += OnSave;
        var cancelBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Cancel"));
        cancelBtn.DialogResult = DialogResult.Cancel;
        var bottomPanel = CreateBottomPanel(saveBtn, cancelBtn);
        bottomPanel.Height = 54;

        // Root layout — avoids Dock.Fill / Dock.Bottom ordering issues
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = p.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.Controls.Add(_tabs, 0, 0);
        root.Controls.Add(bottomPanel, 0, 1);

        Controls.Add(root);

        AcceptButton = saveBtn;
        CancelButton = cancelBtn;
    }

    /// <summary>Returns the default <see cref="CompressionPreset"/> for a format, preferring Balanced.</summary>
    private static CompressionPreset DefaultPresetFor(IArchiveFormat format) =>
        format.SupportedPresets.Contains(CompressionPreset.Balanced) ? CompressionPreset.Balanced
        : format.SupportedPresets.Count > 0 ? format.SupportedPresets[0]
        : CompressionPreset.Balanced;

    /// <summary>Rebuilds the compression preset combo box to match the currently selected format.</summary>
    private void LoadPresetComboForSelectedFormat()
    {
        if (_compressionFormatCombo.SelectedIndex < 0) return;
        var format = _compressionFormats[_compressionFormatCombo.SelectedIndex];
        var L = LocalizationService.Current;

        _compressionPresetCombo.ClearItems();
        foreach (var preset in format.SupportedPresets)
            _compressionPresetCombo.AddItem(L.GetString($"Archive.Compression.{preset}"));

        var current = _workingCompression.TryGetValue(format.Id, out var preset0) ? preset0 : DefaultPresetFor(format);
        var index = -1;
        for (var i = 0; i < format.SupportedPresets.Count; i++)
        {
            if (format.SupportedPresets[i] == current) { index = i; break; }
        }

        _compressionPresetCombo.SelectedIndex = format.SupportedPresets.Count > 0 ? Math.Max(0, index) : -1;
        _compressionPresetCombo.Enabled = format.SupportedPresets.Count > 1;
    }

    /// <summary>Commits the currently selected preset combo value into the working compression dictionary.</summary>
    private void CommitSelectedPreset()
    {
        if (_compressionFormatCombo.SelectedIndex < 0) return;
        var format = _compressionFormats[_compressionFormatCombo.SelectedIndex];
        if (format.SupportedPresets.Count == 0) return;

        var presetIndex = _compressionPresetCombo.SelectedIndex;
        if (presetIndex >= 0 && presetIndex < format.SupportedPresets.Count)
            _workingCompression[format.Id] = format.SupportedPresets[presetIndex];
    }

    /// <summary>Handles the Save button click: persists settings, applies theme and language.</summary>
    private void OnSave(object? sender, EventArgs e)
    {
        CommitSelectedPreset();
        var s = SettingsService.Load();
        s.Theme = _themeCombo.SelectedIndex == 1 ? "Light" : "Dark";
        var langText = _languageCombo.SelectedItem ?? "";
        var langCode = "en";
        var openParen = langText.LastIndexOf('(');
        var closeParen = langText.LastIndexOf(')');
        if (openParen >= 0 && closeParen > openParen)
            langCode = langText[(openParen + 1)..closeParen];
        s.Language = langCode;
        s.ShowHidden = _showHiddenCheck.Checked;
        s.ShowToolbar = _showToolbarCheck.Checked;
        s.ShowStatusBar = _showStatusBarCheck.Checked;
        s.ShowFunctionButtons = _showFnButtonsCheck.Checked;
        s.DirectoriesFirst = _dirsFirstCheck.Checked;
        s.ShowExtensionInName = _showExtInNameCheck.Checked;
        s.ConfirmDelete = _confirmDeleteCheck.Checked;
        s.ConfirmOverwrite = _confirmOverwriteCheck.Checked;
        s.CopyAttributes = _copyAttrsCheck.Checked;
        s.CopyTimestamps = _copyTsCheck.Checked;
        s.ArchiveCompression = _workingCompression.ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        s.DefaultShellType = _defaultShellCombo.SelectedIndex == 1 ? "PowerShell" : "Cmd";
        SettingsService.Save(s);

        // Apply language
        LogService.Info($"SettingsForm: Loading language '{langCode}' from text '{langText}'");
        LocalizationService.Current.LoadLanguage(langCode);
        LogService.Info($"SettingsForm: Language loaded. Current: {LocalizationService.Current.CurrentLanguage}");

        // Apply theme
        ThemeService.ApplyTheme(s.Theme);

        SettingsSaved?.Invoke(this, EventArgs.Empty);
        DialogResult = DialogResult.OK;
        Close();
    }
}
