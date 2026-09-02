using CoderCommander.Archives;
using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Application settings dialog with a left-hand section navigator (<see cref="SettingsNavControl"/>,
/// VS Code-style - replaced the earlier horizontal tab strip, which ran out of room once the
/// section count grew past what a single row of tab labels could hold without wrapping or
/// truncating). Resizable, with its size persisted the same way <c>MainForm</c> persists its own
/// (<see cref="AppSettings.SettingsWindowWidth"/>/<c>Height</c>, written in
/// <see cref="OnFormClosing"/> regardless of Save vs Cancel - window size is a UI preference, not
/// part of the settings the dialog edits).
/// </summary>
public sealed partial class SettingsForm : ThemedForm
{
    private readonly ThemedComboBox _themeCombo;
    private readonly ThemedComboBox _languageCombo;
    private readonly ThemedCheckBox _showHiddenCheck;
    private readonly ThemedCheckBox _showSystemCheck;
    private readonly ThemedCheckBox _showToolbarCheck;
    private readonly ThemedCheckBox _showStatusBarCheck;
    private readonly ThemedCheckBox _showFnButtonsCheck;
    private readonly ThemedCheckBox _dirsFirstCheck;
    private readonly ThemedCheckBox _flatViewCheck;
    private readonly ThemedCheckBox _quickViewRemoteCheck;
    private readonly Label _uiFontDisplayLabel;
    private readonly Label _monoFontDisplayLabel;
    private string _workingUiFontFamily;
    private float _workingUiFontSize;
    private string _workingMonoFontFamily;
    private float _workingMonoFontSize;
    private readonly ThemedComboBox _compressionFormatCombo;
    private readonly ThemedComboBox _compressionPresetCombo;
    private readonly List<IArchiveFormat> _compressionFormats;
    private readonly Dictionary<string, CompressionPreset> _workingCompression = new(StringComparer.OrdinalIgnoreCase);
    private readonly ThemedComboBox _defaultArchiveFormatCombo;
    private readonly ThemedCheckBox _skipCompressionCheck;
    private readonly ThemedCheckBox _deleteOriginalsAfterPackCheck;
    private readonly ListBox _extensionsListBox;
    private readonly TextBox _extensionAddBox;
    private readonly List<string> _workingExtensions;
    /// <summary>Same preset bytes as <see cref="SplitDialogForm"/>.PresetSizes, minus its trailing
    /// "custom size" sentinel (0) - this combo has no free-typed custom option.</summary>
    private static readonly long[] SplitPartSizePresets =
    {
        1_474_560, 100L * 1024 * 1024, 650L * 1024 * 1024, 700L * 1024 * 1024, 4_700_000_000
    };
    private readonly ThemedComboBox _splitPartSizeCombo;
    private readonly ThemedCheckBox _splitWriteCrcCheck;
    private readonly ThemedCheckBox _deleteOriginalsAfterSplitCheck;
    private readonly ThemedCheckBox _verifyCrcAfterCombineCheck;
    private readonly ThemedCheckBox _deleteOriginalsAfterCombineCheck;
    private readonly ThemedCheckBox _confirmDeleteCheck;
    private readonly ThemedCheckBox _confirmOverwriteCheck;
    private readonly ThemedCheckBox _copyAttrsCheck;
    private readonly ThemedCheckBox _copyTsCheck;
    private readonly ThemedCheckBox _showExtInNameCheck;
    private readonly ThemedCheckBox _viewerWordWrapCheck;
    private readonly ThemedCheckBox _viewerImageFitCheck;
    private readonly ThemedComboBox _viewerCsvDelimiterCombo;
    private readonly ThemedCheckBox _viewerCsvHasHeaderCheck;
    private readonly ThemedComboBox _viewerEncodingCombo;
    private readonly ThemedCheckBox _viewerHtmlAllowScriptsCheck;
    private readonly ThemedCheckBox _externalViewerEnabledCheck;
    private readonly TextBox _externalViewerPathBox;
    private readonly TextBox _externalViewerArgsBox;
    private readonly ThemedCheckBox _externalEditorEnabledCheck;
    private readonly TextBox _externalEditorPathBox;
    private readonly TextBox _externalEditorArgsBox;
    private readonly ThemedComboBox _defaultShellCombo;
    private readonly ThemedComboBox _keyBindingPresetCombo;
    private readonly ThemedComboBox _followPanelCwdCombo;
    private readonly ThemedCheckBox _loadShellProfileCheck;
    private IReadOnlyList<Terminal.Shells.ShellDescriptor> _availableShells = Array.Empty<Terminal.Shells.ShellDescriptor>();
    private Dictionary<string, string> _customKeyBindings;
    private Dictionary<string, string> _customHotkeys;

    /// <summary>Raised after settings are saved and applied.</summary>
    public event EventHandler? SettingsSaved;

    /// <summary>Mirrors the F3 CSV viewer's own delimiter choices (<c>AppSettings.ViewerCsvDelimiter</c>:
    /// "auto"/","/";"/"\t"/"|") in combo-index order, paired with the exact localization key that
    /// toolbar already uses.</summary>
    private static readonly (string Value, string Key)[] CsvDelimiterOptions =
    {
        ("auto", "View.Csv.Delimiter.Auto"),
        (",", "View.Csv.Delimiter.Comma"),
        (";", "View.Csv.Delimiter.Semicolon"),
        ("\t", "View.Csv.Delimiter.Tab"),
        ("|", "View.Csv.Delimiter.Pipe"),
    };

    /// <summary>Initializes the settings dialog with current <see cref="AppSettings"/> values.</summary>
    public SettingsForm()
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        var L = LocalizationService.Current;
        var s = SettingsService.Load();

        // Restored size, so it cannot be a designer constant. MinimumSize is - see
        // InitializeComponent.
        ClientSize = new Size(s.SettingsWindowWidth, s.SettingsWindowHeight);
        // Set here rather than in the designer: ThemedForm.Resizable is this app's own property,
        // applied in OnLoad rather than a real FormBorderStyle the designer could round-trip.
        Resizable = true;

        var p = ThemeService.Current;
        // Working copy - the Customize dialog mutates this in place; only persisted on Save.
        _customKeyBindings = new Dictionary<string, string>(s.TerminalCustomKeyBindings);

        // ── Appearance section ──
        var appearLayout = CreateSectionLayout(rows: 9, columns: 2);
        // 170, matching the Archives, Viewer and Terminal sections. At 130 this was the one caption
        // column narrower than the rest, and the longest Russian caption here ("Моноширинный
        // шрифт:") needs 146 - so it wrapped onto two lines while the same dialog's other sections
        // had room to spare. Widening also lines every section's controls up with each other.
        appearLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        appearLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        appearLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.Theme")), 0, row);
        _themeCombo = new ThemedComboBox { Dock = DockStyle.Fill };
        _themeCombo.AddItem(L.GetString("Settings.Theme.Dark"));
        _themeCombo.AddItem(L.GetString("Settings.Theme.Light"));
        _themeCombo.AddItem(L.GetString("Settings.Theme.System"));
        _themeCombo.SelectedIndex = s.Theme == "Light" ? 1 : s.Theme == "System" ? 2 : 0;
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

        _showToolbarCheck = AddFullWidthCheck(appearLayout, row++, "Settings.ShowToolbar", s.ShowToolbar);
        _showStatusBarCheck = AddFullWidthCheck(appearLayout, row++, "Settings.ShowStatusBar", s.ShowStatusBar);
        _showFnButtonsCheck = AddFullWidthCheck(appearLayout, row++, "Settings.ShowFunctionButtons", s.ShowFunctionButtons);

        // No label in column 0 for these two: the button caption already reads "Customize Toolbar",
        // and putting the very same localized string in the label beside it printed it twice on one
        // row. It also broke the rows below - at the section's uniform 32px row height, "Customize
        // Function Bar" wrapped onto a second line that ran straight into the "UI font:" label
        // under it. A button that names its own action needs no separate caption.
        var editToolbarBtn = ThemedForm.CreateThemedButton(L.GetString("Settings.Toolbar.EditToolbar"));
        editToolbarBtn.Click += (_, _) => OnEditToolbarLayout(isFunctionBar: false);
        appearLayout.Controls.Add(editToolbarBtn, 1, row);
        row++;

        var editFnBarBtn = ThemedForm.CreateThemedButton(L.GetString("Settings.Toolbar.EditFunctionBar"));
        editFnBarBtn.Click += (_, _) => OnEditToolbarLayout(isFunctionBar: true);
        appearLayout.Controls.Add(editFnBarBtn, 1, row);
        row++;

        // UI/monospace fonts - working copies so Cancel discards a font picked via FontDialog,
        // same pattern as _workingCompression/_workingExtensions above. "" / 0 = built-in default
        // (Segoe UI 9pt / Consolas 9.5pt), matching AppSettings.UiFontFamily's own sentinel.
        _workingUiFontFamily = s.UiFontFamily;
        _workingUiFontSize = (float)s.UiFontSize;
        _workingMonoFontFamily = s.MonoFontFamily;
        _workingMonoFontSize = (float)s.MonoFontSize;

        // Font picker rows are stacked (label above buttons - see BuildFontPickerRow's doc comment
        // for why) and need more than the section's uniform 32px row height.
        appearLayout.RowStyles[row] = new RowStyle(SizeType.Absolute, 72);
        appearLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.UiFont")), 0, row);
        _uiFontDisplayLabel = UiHelpers.CreateLabel(FormatFontDisplay(_workingUiFontFamily, _workingUiFontSize, "Segoe UI", 9F));
        _uiFontDisplayLabel.TextAlign = ContentAlignment.MiddleLeft;
        var uiFontRow = BuildFontPickerRow(_uiFontDisplayLabel,
            onChange: () => PickFont(ref _workingUiFontFamily, ref _workingUiFontSize, "Segoe UI", 9F, _uiFontDisplayLabel),
            onReset: () => { _workingUiFontFamily = ""; _workingUiFontSize = 0; _uiFontDisplayLabel.Text = FormatFontDisplay("", 0, "Segoe UI", 9F); });
        appearLayout.Controls.Add(uiFontRow, 1, row);
        row++;

        appearLayout.RowStyles[row] = new RowStyle(SizeType.Absolute, 72);
        appearLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.MonoFont")), 0, row);
        _monoFontDisplayLabel = UiHelpers.CreateLabel(FormatFontDisplay(_workingMonoFontFamily, _workingMonoFontSize, "Consolas", 9.5F));
        _monoFontDisplayLabel.TextAlign = ContentAlignment.MiddleLeft;
        var monoFontRow = BuildFontPickerRow(_monoFontDisplayLabel,
            onChange: () => PickFont(ref _workingMonoFontFamily, ref _workingMonoFontSize, "Consolas", 9.5F, _monoFontDisplayLabel),
            onReset: () => { _workingMonoFontFamily = ""; _workingMonoFontSize = 0; _monoFontDisplayLabel.Text = FormatFontDisplay("", 0, "Consolas", 9.5F); });
        appearLayout.Controls.Add(monoFontRow, 1, row);
        row++;

        _nav.AddPage(new SettingsNavPage(L.GetString("Settings.Appearance"), appearLayout, "Settings.Nav.Appearance"));

        // ── Panels section ──
        var panelsLayout = CreateSectionLayout(rows: 6);
        int prow = 0;
        _showHiddenCheck = AddFullWidthCheck(panelsLayout, prow++, "Settings.ShowHidden", s.ShowHidden);
        _showSystemCheck = AddFullWidthCheck(panelsLayout, prow++, "Settings.ShowSystem", s.ShowSystem);
        _dirsFirstCheck = AddFullWidthCheck(panelsLayout, prow++, "Settings.DirectoriesFirst", s.DirectoriesFirst);
        _showExtInNameCheck = AddFullWidthCheck(panelsLayout, prow++, "Settings.ShowExtInName", s.ShowExtensionInName);
        // Startup default only - Ctrl+P/View menu toggle Flat View live per-session without
        // touching this (see MainForm.OpenSettings's fix for the round-trip bug this used to have,
        // audit finding G056).
        _flatViewCheck = AddFullWidthCheck(panelsLayout, prow++, "Settings.FlatView", s.FlatView);
        // QuickViewRemoteEnabled had no control anywhere in the app: it was read by the panel and
        // written by nothing, so it could only ever hold its default and Quick View stayed off for
        // remote files no matter what the user wanted.
        _quickViewRemoteCheck = AddFullWidthCheck(panelsLayout, prow++, "Settings.QuickViewRemote", s.QuickViewRemoteEnabled);

        _nav.AddPage(new SettingsNavPage(L.GetString("Settings.Panels"), panelsLayout, "Settings.Nav.Panels"));

        // ── File Operations section (confirmations folded in - two checkboxes on their own
        //    didn't earn a separate section, and both are file-operation behavior) ──
        var fileOpsLayout = CreateSectionLayout(rows: 4);
        int frow = 0;
        _copyAttrsCheck = AddFullWidthCheck(fileOpsLayout, frow++, "Settings.CopyAttributes", s.CopyAttributes);
        _copyTsCheck = AddFullWidthCheck(fileOpsLayout, frow++, "Settings.CopyTimestamps", s.CopyTimestamps);
        _confirmDeleteCheck = AddFullWidthCheck(fileOpsLayout, frow++, "Settings.ConfirmDelete", s.ConfirmDelete);
        _confirmOverwriteCheck = AddFullWidthCheck(fileOpsLayout, frow++, "Settings.ConfirmOverwrite", s.ConfirmOverwrite);

        _nav.AddPage(new SettingsNavPage(L.GetString("Settings.FileOps"), fileOpsLayout, "Settings.Nav.FileOps"));

        // ── Archives section ──
        // Custom layout (not CreateSectionLayout) - the filler row hosts a rich list editor for
        // AlreadyCompressedExtensions, not a single control, and every fixed row needs the 2-column
        // label|control shape the compression combos already used.
        var archivesLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(16),
            BackColor = p.Background
        };
        archivesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        archivesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++)
            archivesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        archivesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Default format for new archives (PackDialogForm's own preselection) - distinct from the
        // per-format compression list below, which configures every creatable format at once.
        archivesLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.DefaultArchiveFormat")), 0, 0);
        _compressionFormats = ArchiveFormatRegistry.Creatable.ToList();
        _defaultArchiveFormatCombo = new ThemedComboBox { Dock = DockStyle.Fill };
        foreach (var format in _compressionFormats)
            _defaultArchiveFormatCombo.AddItem(L.GetString(format.DisplayNameKey));
        var defaultFormatIndex = _compressionFormats.FindIndex(f => string.Equals(f.Id, s.DefaultArchiveFormat, StringComparison.OrdinalIgnoreCase));
        if (_compressionFormats.Count > 0)
            _defaultArchiveFormatCombo.SelectedIndex = Math.Max(0, defaultFormatIndex);
        archivesLayout.Controls.Add(_defaultArchiveFormatCombo, 1, 0);

        // Per-format archive compression: a format list scoped to what PackDialogForm can actually
        // create (read-only formats like 7z/RAR have no compression to configure and would just
        // show a blank preset combo) + a preset combo scoped to whichever format is selected.
        // Built entirely from the registry, so a future creatable format gets a row here for free.
        archivesLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.ArchiveCompressionFormat")), 0, 1);
        _compressionFormatCombo = new ThemedComboBox { Dock = DockStyle.Fill };
        foreach (var format in _compressionFormats)
            _compressionFormatCombo.AddItem(L.GetString(format.DisplayNameKey));
        archivesLayout.Controls.Add(_compressionFormatCombo, 1, 1);

        archivesLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.ArchiveCompressionPreset")), 0, 2);
        _compressionPresetCombo = new ThemedComboBox { Dock = DockStyle.Fill };
        archivesLayout.Controls.Add(_compressionPresetCombo, 1, 2);

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

        _skipCompressionCheck = AddFullWidthCheck(archivesLayout, 3, "Settings.SkipCompressionForCompressedFiles", s.SkipCompressionForCompressedFiles);
        _deleteOriginalsAfterPackCheck = AddFullWidthCheck(archivesLayout, 4, "Settings.DeleteOriginalsAfterPack", s.DeleteOriginalsAfterPack);

        // Already-compressed extensions editor - list + add/remove + restore built-in defaults.
        // Working copy so Cancel discards edits, same pattern as _workingCompression above.
        _workingExtensions = s.AlreadyCompressedExtensions.Count > 0
            ? new List<string>(new HashSet<string>(s.AlreadyCompressedExtensions, StringComparer.OrdinalIgnoreCase))
            : new List<string>(Operations.PackOperation.DefaultAlreadyCompressedExtensions);

        // F141: the list used to be a Percent(100) row, greedily claiming every pixel
        // SettingsNavControl's _contentPanel happened to hand this section - which has NO
        // AutoScroll of its own (only the nav *sidebar* does; a page's content is Dock=Fill,
        // which sizes to whatever's visible and clips the rest with nothing to scroll it back into
        // view). That made the button row's visibility a function of the dialog's total window
        // height, and even after bumping the default size (F140) it was still getting clipped at
        // the very edge - a live screenshot at 2x zoom is what caught it, not eyeballing a live
        // run. Fixed at the root: every row here is now Absolute except a genuine filler row placed
        // *after* the buttons, so the button row's 64px is guaranteed regardless of window height -
        // any extra space goes to the harmless filler, never subtracted from what the buttons need.
        var extensionsGroup = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = p.Background
        };
        extensionsGroup.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        extensionsGroup.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        // AutoSize, not a hand-picked pixel height: a wrapping FlowLayoutPanel's real height
        // depends on how many lines its children wrap onto, which depends on the current language's
        // button-label widths - exactly the thing a magic number gets wrong. Two earlier attempts
        // here (30px, then 64px) both left the second line's buttons overflowing their own parent's
        // bounds (the panel measured 58px while its content needed 73px), which WinForms clips
        // silently rather than growing to fit.
        extensionsGroup.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        extensionsGroup.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        extensionsGroup.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.AlreadyCompressedExtensions")), 0, 0);

        _extensionsListBox = new ListBox { Dock = DockStyle.Fill, Name = "ArchivesExtensionsList" };
        RefreshExtensionsListBox();
        extensionsGroup.Controls.Add(_extensionsListBox, 0, 1);

        // AutoSize + Dock=Top (not Fill): Fill would size to the cell instead of reporting the
        // height its own wrapped content actually needs, which is what the AutoSize row above
        // measures against.
        var extensionsButtonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _extensionAddBox = UiHelpers.CreateTextBox(name: "ArchivesExtensionAddBox");
        _extensionAddBox.Width = 90;
        _extensionAddBox.Margin = new Padding(0, 2, 8, 0);
        var addExtBtn = ThemedForm.CreateThemedButton(L.GetString("Settings.AlreadyCompressedExtensions.Add"));
        addExtBtn.Click += (_, _) => OnAddExtension();
        var removeExtBtn = ThemedForm.CreateThemedButton(L.GetString("Settings.AlreadyCompressedExtensions.Remove"));
        removeExtBtn.Click += (_, _) => OnRemoveSelectedExtension();
        var restoreExtBtn = ThemedForm.CreateThemedButton(L.GetString("Settings.AlreadyCompressedExtensions.RestoreDefaults"));
        restoreExtBtn.Click += (_, _) => OnRestoreDefaultExtensions();
        EqualizeWidths(addExtBtn, removeExtBtn, restoreExtBtn);
        extensionsButtonRow.Controls.Add(_extensionAddBox);
        extensionsButtonRow.Controls.Add(addExtBtn);
        extensionsButtonRow.Controls.Add(removeExtBtn);
        extensionsButtonRow.Controls.Add(restoreExtBtn);
        extensionsGroup.Controls.Add(extensionsButtonRow, 0, 2);

        archivesLayout.Controls.Add(extensionsGroup, 0, 5);
        archivesLayout.SetColumnSpan(extensionsGroup, 2);

        _nav.AddPage(new SettingsNavPage(L.GetString("Settings.Archives"), archivesLayout, "Settings.Nav.Archives"));

        // ── Split/Combine section ── (same preset list as SplitDialogForm.PresetSizes, minus the
        // "custom size" sentinel entry - a persisted default only makes sense for a fixed preset).
        var splitLayout = CreateSectionLayout(rows: 5, columns: 2);
        splitLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Split.PartSize")), 0, 0);
        _splitPartSizeCombo = new ThemedComboBox { Dock = DockStyle.Fill };
        _splitPartSizeCombo.AddItems(
            L.GetString("Split.Preset.Floppy"),
            L.GetString("Split.Preset.100Mb"),
            L.GetString("Split.Preset.Cd650"),
            L.GetString("Split.Preset.Cd700"),
            L.GetString("Split.Preset.Dvd"));
        var splitPresetIndex = Array.IndexOf(SplitPartSizePresets, s.DefaultSplitPartSizeBytes);
        _splitPartSizeCombo.SelectedIndex = splitPresetIndex >= 0 ? splitPresetIndex : 1; // 100 MB default
        splitLayout.Controls.Add(_splitPartSizeCombo, 1, 0);

        _splitWriteCrcCheck = AddFullWidthCheck(splitLayout, 1, "Split.WriteCrc", s.SplitWriteCrcDefault);
        _deleteOriginalsAfterSplitCheck = AddFullWidthCheck(splitLayout, 2, "Split.DeleteSource", s.DeleteOriginalsAfterSplit);
        _verifyCrcAfterCombineCheck = AddFullWidthCheck(splitLayout, 3, "Combine.VerifyCrc", s.VerifyCrcAfterCombine);
        _deleteOriginalsAfterCombineCheck = AddFullWidthCheck(splitLayout, 4, "Combine.DeleteParts", s.DeleteOriginalsAfterCombine);

        _nav.AddPage(new SettingsNavPage(L.GetString("Settings.SplitCombine"), splitLayout, "Settings.Nav.SplitCombine"));

        // ── Viewer/Editor section ──
        // Every setting here already existed and was persisted before this section did - only
        // reachable via the F3 viewer's own toolbars, with no visible default anywhere (see the
        // settings-expansion plan's "Ф3" gap list). Reuses the exact same localization keys the F3
        // toolbars already use (View.WordWrap, View.Csv.*, View.Encoding.*, View.ZoomFit) rather
        // than duplicating them under a Settings.* prefix - same string, same meaning, one place to
        // translate.
        var viewerLayout = CreateSectionLayout(rows: 13, columns: 2);
        viewerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        viewerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _viewerWordWrapCheck = AddFullWidthCheck(viewerLayout, 0, "View.WordWrap", s.ViewerWordWrap);
        _viewerImageFitCheck = AddFullWidthCheck(viewerLayout, 1, "View.ZoomFit", s.ViewerImageFitToWindow);

        viewerLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("View.Csv.Delimiter")), 0, 2);
        _viewerCsvDelimiterCombo = new ThemedComboBox { Dock = DockStyle.Fill };
        // Index <-> stored value mapping kept in one place (this array) rather than a switch in
        // both directions, matching the ArchiveFormatRegistry-driven combos above.
        foreach (var (_, key) in CsvDelimiterOptions)
            _viewerCsvDelimiterCombo.AddItem(L.GetString(key));
        var csvDelimiterIndex = Array.FindIndex(CsvDelimiterOptions, o => o.Value == s.ViewerCsvDelimiter);
        _viewerCsvDelimiterCombo.SelectedIndex = Math.Max(0, csvDelimiterIndex);
        viewerLayout.Controls.Add(_viewerCsvDelimiterCombo, 1, 2);

        _viewerCsvHasHeaderCheck = AddFullWidthCheck(viewerLayout, 3, "View.Csv.HasHeader", s.ViewerCsvHasHeader);

        viewerLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("View.Encoding")), 0, 4);
        _viewerEncodingCombo = new ThemedComboBox { Dock = DockStyle.Fill };
        _viewerEncodingCombo.AddItem(L.GetString("View.Encoding.Auto"));
        foreach (var entry in EncodingCatalog.Entries)
            _viewerEncodingCombo.AddItem(L.GetString(entry.DisplayNameKey));
        var encodingIndex = 0;
        if (!string.IsNullOrEmpty(s.ViewerEncodingOverride))
        {
            for (var i = 0; i < EncodingCatalog.Entries.Count; i++)
            {
                if (EncodingCatalog.Entries[i].Id != s.ViewerEncodingOverride) continue;
                encodingIndex = i + 1; // +1 for the leading "Auto-detect" entry
                break;
            }
        }
        _viewerEncodingCombo.SelectedIndex = encodingIndex;
        viewerLayout.Controls.Add(_viewerEncodingCombo, 1, 4);

        _viewerHtmlAllowScriptsCheck = AddFullWidthCheck(viewerLayout, 5, "Settings.ViewerHtmlAllowScripts", s.ViewerHtmlAllowScripts);

        // AutoSize=false + a fixed height lets Label's default word-wrap fill the row instead of
        // measuring for a single line and truncating. Sized generously (4 wrapped lines' worth) -
        // the Russian warning text is long enough to need 3+ lines at this section's narrow
        // effective width (nav column + padding leave well under half the dialog's own width), and
        // a Label silently clips anything past its bounds instead of scrolling, so under-sizing
        // this would hide part of a security-relevant warning rather than just look cramped.
        var htmlScriptWarning = UiHelpers.CreateLabel(L.GetString("Settings.ViewerHtmlAllowScriptsWarning"));
        htmlScriptWarning.SetRole(ThemeRole.Danger);
        htmlScriptWarning.Dock = DockStyle.Fill;
        htmlScriptWarning.AutoSize = false;
        // This row's own height, overridden from CreateSectionLayout's uniform 32px - the warning
        // needs 3+ wrapped lines at this section's narrow effective width (see the comment that
        // used to sit here), the only row in this section that isn't a single checkbox/combo line.
        viewerLayout.RowStyles[6] = new RowStyle(SizeType.Absolute, 76);
        viewerLayout.Controls.Add(htmlScriptWarning, 0, 6);
        viewerLayout.SetColumnSpan(htmlScriptWarning, 2);

        // External viewer (F3) - only reachable for a file on a native-path filesystem (checked at
        // launch time in MainForm.OnView, not here); a stale/missing path silently falls back to
        // the built-in viewer (ExternalToolLauncher.TryLaunch), never blocks F3.
        _externalViewerEnabledCheck = AddFullWidthCheck(viewerLayout, 7, "Settings.ExternalViewerEnabled", s.ExternalViewerEnabled);

        viewerLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.ExternalToolPath")), 0, 8);
        _externalViewerPathBox = UiHelpers.CreateTextBox(s.ExternalViewerPath, "ExternalViewerPathBox");
        viewerLayout.Controls.Add(BuildPathPickerRow(_externalViewerPathBox), 1, 8);

        viewerLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.ExternalToolArgs")), 0, 9);
        _externalViewerArgsBox = UiHelpers.CreateTextBox(s.ExternalViewerArgs, "ExternalViewerArgsBox");
        _externalViewerArgsBox.Dock = DockStyle.Fill;
        viewerLayout.Controls.Add(_externalViewerArgsBox, 1, 9);

        // External editor (F4) - same shape as the viewer block above.
        _externalEditorEnabledCheck = AddFullWidthCheck(viewerLayout, 10, "Settings.ExternalEditorEnabled", s.ExternalEditorEnabled);

        viewerLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.ExternalToolPath")), 0, 11);
        _externalEditorPathBox = UiHelpers.CreateTextBox(s.ExternalEditorPath, "ExternalEditorPathBox");
        viewerLayout.Controls.Add(BuildPathPickerRow(_externalEditorPathBox), 1, 11);

        viewerLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.ExternalToolArgs")), 0, 12);
        _externalEditorArgsBox = UiHelpers.CreateTextBox(s.ExternalEditorArgs, "ExternalEditorArgsBox");
        _externalEditorArgsBox.Dock = DockStyle.Fill;
        viewerLayout.Controls.Add(_externalEditorArgsBox, 1, 12);

        _nav.AddPage(new SettingsNavPage(L.GetString("Settings.Editor"), viewerLayout, "Settings.Nav.Editor"));

        // ── Terminal section ──
        var terminalLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(16),
            BackColor = p.Background
        };
        terminalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        terminalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        terminalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        // AutoSize, not a hand-picked height - same reasoning as the Archives extensions button
        // row: the key-binding combo + "Customize…" button wrap onto a second line at this
        // section's width (the left-hand nav control permanently took ~176px out of it), and how
        // much height that needs depends on the current language's label widths. Two earlier
        // fixed values (56px, 70px) each left the wrapped line clipped or overlapping.
        terminalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        terminalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        terminalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        terminalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        terminalLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Populated synchronously with whatever ShellCatalog already has cached (instant if
        // MainForm's startup pre-warm already ran, which it always has by the time a user opens
        // Settings) and refreshed asynchronously otherwise - never blocks dialog construction.
        terminalLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.DefaultShell")), 0, 0);
        _defaultShellCombo = new ThemedComboBox { Dock = DockStyle.Fill };
        terminalLayout.Controls.Add(_defaultShellCombo, 1, 0);
        PopulateShellComboAsync(s.DefaultShellType);

        terminalLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.Terminal.KeyBindingPreset")), 0, 1);
        var keyBindingRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _keyBindingPresetCombo = new ThemedComboBox { Width = 160, Margin = new Padding(0, 0, 8, 4) };
        _keyBindingPresetCombo.AddItem(L.GetString("Settings.Terminal.KeyBindingPreset.WindowsTerminal"));
        _keyBindingPresetCombo.AddItem(L.GetString("Settings.Terminal.KeyBindingPreset.Classic"));
        _keyBindingPresetCombo.AddItem(L.GetString("Settings.Terminal.KeyBindingPreset.Custom"));
        _keyBindingPresetCombo.SelectedIndex = s.TerminalKeyBindingPreset switch { "Classic" => 1, "Custom" => 2, _ => 0 };
        var customizeBtn = ThemedForm.CreateThemedButton(L.GetString("Settings.Terminal.Customize"));
        customizeBtn.Click += OnCustomizeKeyBindings;
        keyBindingRow.Controls.Add(_keyBindingPresetCombo);
        keyBindingRow.Controls.Add(customizeBtn);
        terminalLayout.Controls.Add(keyBindingRow, 1, 1);

        terminalLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.Terminal.FollowPanelCwd")), 0, 2);
        _followPanelCwdCombo = new ThemedComboBox { Dock = DockStyle.Fill };
        _followPanelCwdCombo.AddItem(L.GetString("Settings.Terminal.FollowPanelCwd.Never"));
        _followPanelCwdCombo.AddItem(L.GetString("Settings.Terminal.FollowPanelCwd.OnOpen"));
        _followPanelCwdCombo.AddItem(L.GetString("Settings.Terminal.FollowPanelCwd.Always"));
        _followPanelCwdCombo.SelectedIndex = s.TerminalFollowPanelCwd switch { "Never" => 0, "Always" => 2, _ => 1 };
        terminalLayout.Controls.Add(_followPanelCwdCombo, 1, 2);

        _loadShellProfileCheck = UiHelpers.CreateCheckBox(L.GetString("Settings.Terminal.LoadShellProfile"), s.TerminalLoadShellProfile);
        _loadShellProfileCheck.Dock = DockStyle.Fill;
        terminalLayout.Controls.Add(_loadShellProfileCheck, 0, 3);
        terminalLayout.SetColumnSpan(_loadShellProfileCheck, 2);

        terminalLayout.Controls.Add(UiHelpers.CreateLabel(L.GetString("Settings.Terminal.CustomShells")), 0, 4);
        var manageShellsBtn = ThemedForm.CreateThemedButton(L.GetString("Settings.Terminal.CustomShells.Manage"));
        manageShellsBtn.Click += OnManageCustomShells;
        terminalLayout.Controls.Add(manageShellsBtn, 1, 4);

        _nav.AddPage(new SettingsNavPage(L.GetString("Settings.Terminal"), terminalLayout, "Settings.Nav.Terminal"));

        // ── Hotkeys section ──
        // Working copy - the Customize dialog mutates this in place; only persisted on Save, same
        // pattern as _customKeyBindings above (the terminal's own analogous editor).
        _customHotkeys = new Dictionary<string, string>(s.CustomHotkeys, StringComparer.Ordinal);

        var hotkeysLayout = CreateSectionLayout(rows: 2);
        var hotkeysHint = UiHelpers.CreateLabel(L.GetString("Settings.Hotkeys.SectionHint"));
        hotkeysHint.Dock = DockStyle.Fill;
        hotkeysHint.AutoEllipsis = false;
        hotkeysHint.AutoSize = false;
        hotkeysLayout.RowStyles[0] = new RowStyle(SizeType.Absolute, 48);
        hotkeysLayout.Controls.Add(hotkeysHint, 0, 0);

        var hotkeysCustomizeBtn = ThemedForm.CreateThemedButton(L.GetString("Settings.Hotkeys.Customize"));
        hotkeysCustomizeBtn.Click += OnCustomizeHotkeys;
        // AutoSize row + Dock=Top/AutoSize panel, not CreateSectionLayout's uniform 32px: a 32px
        // button plus its margins doesn't fit a 32px row, which clipped the button's bottom edge.
        hotkeysLayout.RowStyles[1] = new RowStyle(SizeType.AutoSize);
        var hotkeysBtnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        hotkeysBtnRow.Controls.Add(hotkeysCustomizeBtn);
        hotkeysLayout.Controls.Add(hotkeysBtnRow, 0, 1);

        _nav.AddPage(new SettingsNavPage(L.GetString("Settings.Hotkeys"), hotkeysLayout, "Settings.Nav.Hotkeys"));

        // The root grid, the nav host and the button bar all come from InitializeComponent - only
        // the Save handler is wired here (Cancel closes via its own DialogResult).
        _saveBtn.Click += OnSave;
        FormClosing += OnFormClosing;
    }

    /// <summary>Builds a section layout with <paramref name="rows"/> fixed 32px rows plus one
    /// flexible filler row - the shape every section in this dialog shares. <paramref name="columns"/>
    /// must match how many columns the caller actually populates - <c>TableLayoutPanel.ColumnCount</c>
    /// doesn't auto-grow from adding <c>ColumnStyles</c> alone, and leaving it at the 1-column
    /// default while placing controls in column 1 left every row's real height ambiguous, which
    /// rendered as large uneven gaps between checkboxes instead of tightly packed 32px rows
    /// (caught by visual inspection of a live build).</summary>
    private static TableLayoutPanel CreateSectionLayout(int rows, int columns = 1)
    {
        var p = ThemeService.Current;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = columns,
            RowCount = rows + 1,
            Padding = new Padding(16),
            BackColor = p.Background
        };
        for (int i = 0; i < rows; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return layout;
    }

    /// <summary>Adds a full-width checkbox row (column-spanned when the layout has 2 columns) at
    /// <paramref name="row"/> and returns it.</summary>
    private static ThemedCheckBox AddFullWidthCheck(TableLayoutPanel layout, int row, string labelKey, bool initial)
    {
        var L = LocalizationService.Current;
        var check = UiHelpers.CreateCheckBox(L.GetString(labelKey), initial);
        check.Dock = DockStyle.Fill;
        layout.Controls.Add(check, 0, row);
        if (layout.ColumnCount > 1)
            layout.SetColumnSpan(check, layout.ColumnCount);
        return check;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // Window size is a UI preference, not part of the settings this dialog edits - persisted
        // regardless of Save vs Cancel, same reasoning MainForm's own OnFormClosing uses for its
        // WindowWidth/WindowHeight.
        var s = SettingsService.Load();
        s.SettingsWindowWidth = Width;
        s.SettingsWindowHeight = Height;
        SettingsService.Save(s);
    }

    private async void PopulateShellComboAsync(string preferredShellId)
    {
        IReadOnlyList<Terminal.Shells.ShellDescriptor> shells;
        try
        {
            shells = await Terminal.Shells.ShellCatalog.DiscoverAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LogService.Error("SettingsForm: shell discovery failed", ex);
            return;
        }
        if (IsDisposed) return;

        _availableShells = shells;
        var L = LocalizationService.Current;
        _defaultShellCombo.ClearItems();
        var selectedIndex = 0;
        for (var i = 0; i < shells.Count; i++)
        {
            var shell = shells[i];
            var name = shell.DisplayNameArg != null
                ? L.GetString(shell.DisplayNameKey, shell.DisplayNameArg)
                : L.GetString(shell.DisplayNameKey);
            _defaultShellCombo.AddItem(name);
            if (shell.Id == preferredShellId) selectedIndex = i;
        }
        if (shells.Count > 0)
            _defaultShellCombo.SelectedIndex = selectedIndex;
    }

    private void OnCustomizeKeyBindings(object? sender, EventArgs e)
    {
        // Editing custom bindings only makes sense once "Custom" is actually selected - switch to
        // it automatically rather than silently discarding what the user is about to configure.
        if (_keyBindingPresetCombo.SelectedIndex != 2)
            _keyBindingPresetCombo.SelectedIndex = 2;

        using var dlg = new TerminalKeyBindingsForm(_customKeyBindings);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _customKeyBindings = dlg.ResultBindings;
    }

    private void OnEditToolbarLayout(bool isFunctionBar)
    {
        using var dlg = new ToolbarButtonsForm(isFunctionBar);
        dlg.ShowDialog(this);
    }

    private void OnManageCustomShells(object? sender, EventArgs e)
    {
        using var dlg = new CustomShellsForm();
        dlg.ShowDialog(this);
        // The Default Shell combo may now need to gain/lose/rename an entry - re-populate against
        // whatever's currently selected, same as the initial call at dialog construction.
        var currentId = _defaultShellCombo.SelectedIndex >= 0 && _defaultShellCombo.SelectedIndex < _availableShells.Count
            ? _availableShells[_defaultShellCombo.SelectedIndex].Id
            : SettingsService.Load().DefaultShellType;
        PopulateShellComboAsync(currentId);
    }

    private void OnCustomizeHotkeys(object? sender, EventArgs e)
    {
        using var dlg = new HotkeyBindingsForm(_customHotkeys);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _customHotkeys = dlg.ResultBindings;
    }

    /// <summary>Returns the default <see cref="CompressionPreset"/> for a format, preferring Balanced.</summary>
    private static CompressionPreset DefaultPresetFor(IArchiveFormat format) =>
        format.SupportedPresets.Contains(CompressionPreset.Balanced) ? CompressionPreset.Balanced
        : format.SupportedPresets.Count > 0 ? format.SupportedPresets[0]
        : CompressionPreset.Balanced;

    /// <summary>Guards <see cref="CommitSelectedPreset"/> against firing while
    /// <see cref="LoadPresetComboForSelectedFormat"/> is (re)building the preset combo's item
    /// list - <see cref="ThemedComboBox.AddItem"/> auto-selects index 0 the moment the first item
    /// is added, which raises <c>SelectedIndexChanged</c> mid-population, before the real default/
    /// working value has even been computed. Without this guard, that spurious event let
    /// <see cref="CommitSelectedPreset"/> commit whatever preset happened to be at index 0 (Store)
    /// into <see cref="_workingCompression"/> for every format, silently overwriting the correct
    /// default - the Archives section always showed "None (store only)" instead of "Balanced" on a
    /// fresh install, caught by an offscreen screenshot, not by eye on a live run.</summary>
    private bool _isLoadingPresetCombo;

    /// <summary>Rebuilds the compression preset combo box to match the currently selected format.</summary>
    private void LoadPresetComboForSelectedFormat()
    {
        if (_compressionFormatCombo.SelectedIndex < 0) return;
        var format = _compressionFormats[_compressionFormatCombo.SelectedIndex];
        var L = LocalizationService.Current;

        _isLoadingPresetCombo = true;
        try
        {
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
        finally
        {
            _isLoadingPresetCombo = false;
        }
    }

    /// <summary>Commits the currently selected preset combo value into the working compression dictionary.</summary>
    private void CommitSelectedPreset()
    {
        if (_isLoadingPresetCombo) return;
        if (_compressionFormatCombo.SelectedIndex < 0) return;
        var format = _compressionFormats[_compressionFormatCombo.SelectedIndex];
        if (format.SupportedPresets.Count == 0) return;

        var presetIndex = _compressionPresetCombo.SelectedIndex;
        if (presetIndex >= 0 && presetIndex < format.SupportedPresets.Count)
            _workingCompression[format.Id] = format.SupportedPresets[presetIndex];
    }

    /// <summary>Widens every button in a related group to the widest one's size, so a row of
    /// buttons reads as one set instead of three differently-sized boxes.
    /// <para><see cref="ThemedForm.CreateThemedButton"/> sizes each button to its own text, which
    /// is right for a lone button (and is what keeps a long Russian label from truncating) but
    /// looks visibly ragged when several sit side by side - "Add" 80px next to "Remove" 94px next
    /// to "Restore defaults" 135px. Equalizing upward never truncates: the widest button already
    /// fits its own text, so every other one gets strictly more room than it needs.</para></summary>
    private static void EqualizeWidths(params Button[] buttons)
    {
        if (buttons.Length == 0) return;
        var widest = buttons.Max(b => b.Width);
        foreach (var b in buttons)
            b.Width = widest;
    }

    /// <summary>Builds a path row: the path text box (fill, hand-editable) plus a "Browse…" button
    /// that opens a native <see cref="OpenFileDialog"/> scoped to executables. Native picker, not a
    /// themed one - same reasoning as <c>DifferForm.Browse</c>'s own file picker: a real Windows
    /// file dialog is the right tool for "pick a file from the real local disk", themed or not.</summary>
    private Control BuildPathPickerRow(TextBox pathBox)
    {
        var L = LocalizationService.Current;
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = ThemeService.Current.Background };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        pathBox.Dock = DockStyle.Fill;
        row.Controls.Add(pathBox, 0, 0);

        var browseBtn = ThemedForm.CreateThemedButton(L.GetString("Settings.ExternalToolBrowse"));
        browseBtn.Margin = new Padding(4, 2, 0, 2);
        browseBtn.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = L.GetString("Settings.ExternalToolBrowseFilter") };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                pathBox.Text = dlg.FileName;
        };
        row.Controls.Add(browseBtn, 1, 0);

        return row;
    }

    /// <summary>Builds a font row: the display label on its own line, "Change…"/"Reset" on the
    /// line below. Stacked rather than a single label+2-buttons row - this section's label column
    /// only leaves ~220px for the whole picker, and with the two buttons taking their own space in
    /// a side-by-side layout, TableLayoutPanel starved the label's Percent column down to almost
    /// nothing (rendered as just "S."/"C." in a live build, even with AutoSize=false - the columns
    /// interact in a way a single extra row sidesteps entirely by giving the label the full row
    /// width with nothing competing for it).</summary>
    private static Control BuildFontPickerRow(Label displayLabel, Action onChange, Action onReset)
    {
        var L = LocalizationService.Current;
        var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = ThemeService.Current.Background };
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        // AutoSize, not 30px: CreateThemedButton builds 32px-tall buttons, so a 30px row clipped
        // 2px off the bottom of every Change…/Reset pair - subtle enough to read as a rendering
        // artifact rather than a layout bug, which is exactly how it was reported.
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        displayLabel.AutoSize = false;
        displayLabel.AutoEllipsis = true;
        displayLabel.Dock = DockStyle.Fill;
        stack.Controls.Add(displayLabel, 0, 0);

        // Dock=Top + AutoSize so the row reports the height its buttons actually need, which is
        // what the AutoSize RowStyle above measures against (Fill would size to the cell instead).
        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        var changeBtn = ThemedForm.CreateThemedButton(L.GetString("Settings.Font.Change"));
        changeBtn.Margin = new Padding(0, 0, 8, 0);
        changeBtn.Click += (_, _) => onChange();
        var resetBtn = ThemedForm.CreateThemedButton(L.GetString("Settings.Font.Reset"));
        // Control's stock Margin is (3,3,3,3); changeBtn zeroes its top/bottom above so the two
        // buttons share the same 32px height at the same Y - leaving resetBtn's default 3px top
        // margin unset here pushed it 3px lower than changeBtn in the FlowLayoutPanel (measured
        // pixel-for-pixel on a live screenshot, not assumed).
        resetBtn.Margin = new Padding(0);
        resetBtn.Click += (_, _) => onReset();
        EqualizeWidths(changeBtn, resetBtn);
        buttonRow.Controls.Add(changeBtn);
        buttonRow.Controls.Add(resetBtn);
        stack.Controls.Add(buttonRow, 0, 1);

        return stack;
    }

    /// <summary>"Family, 9.5pt" for an explicit override, or "Family, 9pt (default)" for the "" / 0
    /// sentinel - so Appearance always shows what font is actually in effect, never a blank row.</summary>
    private static string FormatFontDisplay(string family, float size, string defaultFamily, float defaultSize)
    {
        var L = LocalizationService.Current;
        // Invariant culture, not the OS locale's number format - "9.5pt" must read the same
        // regardless of Windows' regional settings, the way every other size/unit string in this
        // app already does (this one used the default culture and rendered "9,5pt" under a
        // Russian-locale OS even with English selected as the app's own display language).
        return string.IsNullOrWhiteSpace(family) || size <= 0
            ? L.GetString("Settings.Font.DefaultLabel", $"{defaultFamily}, {defaultSize.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}pt")
            : $"{family}, {size.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}pt";
    }

    /// <summary>Opens the native <see cref="FontDialog"/> (family/size only - no bold/italic/color,
    /// which every <see cref="ThemePalette"/> role already decides on its own) seeded with the
    /// current working value, and writes the result back into <paramref name="family"/>/
    /// <paramref name="size"/> plus the display label on OK. A field passed by <c>ref</c> from
    /// inside a button-click lambda is legal here (fields aren't "captured" the way local variables
    /// are - the lambda reaches them through <c>this</c>), unlike trying to ref a captured local.</summary>
    private void PickFont(ref string family, ref float size, string defaultFamily, float defaultSize, Label displayLabel)
    {
        var currentFamily = string.IsNullOrWhiteSpace(family) ? defaultFamily : family;
        var currentSize = size > 0 ? size : defaultSize;

        using var previewFont = SafeCreateFont(currentFamily, currentSize, defaultFamily);
        using var dlg = new FontDialog
        {
            Font = previewFont,
            ShowEffects = false,
            ShowColor = false,
            FontMustExist = true,
            MinSize = 6,
            MaxSize = 36
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        family = dlg.Font.Name;
        size = dlg.Font.Size;
        displayLabel.Text = FormatFontDisplay(family, size, defaultFamily, defaultSize);
    }

    /// <summary>Same "fall back rather than throw" contract as <c>FontCache.CreateFont</c> (kept
    /// separate rather than reusing it - that one is <c>internal</c> to <c>Services</c> and always
    /// falls back to Segoe UI specifically, not to whichever default this call site wants). GDI+
    /// doesn't actually throw for an unavailable family - <c>new Font(name, size)</c> silently
    /// substitutes a fallback and reports the substitute's own name back, so detecting the failure
    /// means comparing <see cref="Font.Name"/> against what was asked for, same as FontCache does.</summary>
    private static Font SafeCreateFont(string family, float size, string fallbackFamily)
    {
        var font = new Font(family, size);
        if (string.Equals(font.Name, family, StringComparison.OrdinalIgnoreCase)) return font;
        font.Dispose();
        return new Font(fallbackFamily, size);
    }

    private void RefreshExtensionsListBox()
    {
        _extensionsListBox.Items.Clear();
        foreach (var ext in _workingExtensions)
            _extensionsListBox.Items.Add(ext);
    }

    /// <summary>Adds the text box's content as a new extension - normalizes to a leading dot (the
    /// form every other extension list in the app uses, see <c>CleanAlreadyCompressedExtensions</c>)
    /// and skips a case-insensitive duplicate rather than adding a second entry for it.</summary>
    private void OnAddExtension()
    {
        var text = _extensionAddBox.Text.Trim();
        if (text.Length == 0) return;
        if (!text.StartsWith('.')) text = "." + text;

        // Reject extensions containing invalid filename characters — they would break
        // PackOperation's extension matching later.
        if (text.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            StyledMessageBox.Show(
                LocalizationService.Current.GetString("Settings.ExtensionsInvalidChars"),
                LocalizationService.Current.GetString("Common.Error"),
                MsgBoxButtons.OK, MsgBoxIcon.Warning, this);
            return;
        }

        if (!_workingExtensions.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            _workingExtensions.Add(text);
            RefreshExtensionsListBox();
        }
        _extensionAddBox.Clear();
        _extensionAddBox.Focus();
    }

    private void OnRemoveSelectedExtension()
    {
        var index = _extensionsListBox.SelectedIndex;
        if (index < 0 || index >= _workingExtensions.Count) return;
        _workingExtensions.RemoveAt(index);
        RefreshExtensionsListBox();
    }

    private void OnRestoreDefaultExtensions()
    {
        _workingExtensions.Clear();
        _workingExtensions.AddRange(Operations.PackOperation.DefaultAlreadyCompressedExtensions);
        RefreshExtensionsListBox();
    }

    /// <summary>Handles the Save button click: persists settings, applies theme and language.</summary>
    private void OnSave(object? sender, EventArgs e)
    {
        CommitSelectedPreset();
        var s = SettingsService.Load();
        s.Theme = _themeCombo.SelectedIndex switch { 1 => "Light", 2 => "System", _ => "Dark" };
        var langText = _languageCombo.SelectedItem ?? "";
        var langCode = "en";
        var openParen = langText.LastIndexOf('(');
        var closeParen = langText.LastIndexOf(')');
        if (openParen >= 0 && closeParen > openParen)
            langCode = langText[(openParen + 1)..closeParen];
        s.Language = langCode;
        s.ShowHidden = _showHiddenCheck.Checked;
        s.ShowSystem = _showSystemCheck.Checked;
        s.FlatView = _flatViewCheck.Checked;
        s.QuickViewRemoteEnabled = _quickViewRemoteCheck.Checked;
        s.ShowToolbar = _showToolbarCheck.Checked;
        s.ShowStatusBar = _showStatusBarCheck.Checked;
        s.ShowFunctionButtons = _showFnButtonsCheck.Checked;
        s.UiFontFamily = _workingUiFontFamily;
        s.UiFontSize = _workingUiFontSize;
        s.MonoFontFamily = _workingMonoFontFamily;
        s.MonoFontSize = _workingMonoFontSize;
        s.DirectoriesFirst = _dirsFirstCheck.Checked;
        s.ShowExtensionInName = _showExtInNameCheck.Checked;
        s.ConfirmDelete = _confirmDeleteCheck.Checked;
        s.ConfirmOverwrite = _confirmOverwriteCheck.Checked;
        s.CopyAttributes = _copyAttrsCheck.Checked;
        s.CopyTimestamps = _copyTsCheck.Checked;
        s.ArchiveCompression.Clear();
        foreach (var kv in _workingCompression)
            s.ArchiveCompression[kv.Key] = kv.Value.ToString();
        if (_defaultArchiveFormatCombo.SelectedIndex >= 0 && _defaultArchiveFormatCombo.SelectedIndex < _compressionFormats.Count)
            s.DefaultArchiveFormat = _compressionFormats[_defaultArchiveFormatCombo.SelectedIndex].Id;
        s.SkipCompressionForCompressedFiles = _skipCompressionCheck.Checked;
        s.DeleteOriginalsAfterPack = _deleteOriginalsAfterPackCheck.Checked;
        s.AlreadyCompressedExtensions.Clear();
        s.AlreadyCompressedExtensions.AddRange(_workingExtensions);
        s.DefaultSplitPartSizeBytes = SplitPartSizePresets[Math.Clamp(_splitPartSizeCombo.SelectedIndex, 0, SplitPartSizePresets.Length - 1)];
        s.SplitWriteCrcDefault = _splitWriteCrcCheck.Checked;
        s.DeleteOriginalsAfterSplit = _deleteOriginalsAfterSplitCheck.Checked;
        s.VerifyCrcAfterCombine = _verifyCrcAfterCombineCheck.Checked;
        s.DeleteOriginalsAfterCombine = _deleteOriginalsAfterCombineCheck.Checked;
        s.ViewerWordWrap = _viewerWordWrapCheck.Checked;
        s.ViewerImageFitToWindow = _viewerImageFitCheck.Checked;
        if (_viewerCsvDelimiterCombo.SelectedIndex >= 0 && _viewerCsvDelimiterCombo.SelectedIndex < CsvDelimiterOptions.Length)
            s.ViewerCsvDelimiter = CsvDelimiterOptions[_viewerCsvDelimiterCombo.SelectedIndex].Value;
        s.ViewerCsvHasHeader = _viewerCsvHasHeaderCheck.Checked;
        // Index 0 is the leading "Auto-detect" entry (empty override); anything after maps 1:1
        // (offset by 1) onto EncodingCatalog.Entries.
        s.ViewerEncodingOverride = _viewerEncodingCombo.SelectedIndex > 0 && _viewerEncodingCombo.SelectedIndex - 1 < EncodingCatalog.Entries.Count
            ? EncodingCatalog.Entries[_viewerEncodingCombo.SelectedIndex - 1].Id
            : "";
        s.ViewerHtmlAllowScripts = _viewerHtmlAllowScriptsCheck.Checked;
        s.ExternalViewerEnabled = _externalViewerEnabledCheck.Checked;
        s.ExternalViewerPath = _externalViewerPathBox.Text.Trim();
        s.ExternalViewerArgs = _externalViewerArgsBox.Text;
        s.ExternalEditorEnabled = _externalEditorEnabledCheck.Checked;
        s.ExternalEditorPath = _externalEditorPathBox.Text.Trim();
        s.ExternalEditorArgs = _externalEditorArgsBox.Text;
        if (_defaultShellCombo.SelectedIndex >= 0 && _defaultShellCombo.SelectedIndex < _availableShells.Count)
            s.DefaultShellType = _availableShells[_defaultShellCombo.SelectedIndex].Id;
        s.TerminalKeyBindingPreset = _keyBindingPresetCombo.SelectedIndex switch { 1 => "Classic", 2 => "Custom", _ => "WindowsTerminal" };
        s.TerminalCustomKeyBindings.Clear();
        foreach (var kv in _customKeyBindings)
            s.TerminalCustomKeyBindings[kv.Key] = kv.Value;
        s.TerminalFollowPanelCwd = _followPanelCwdCombo.SelectedIndex switch { 0 => "Never", 2 => "Always", _ => "OnOpen" };
        s.TerminalLoadShellProfile = _loadShellProfileCheck.Checked;
        s.CustomHotkeys.Clear();
        foreach (var kv in _customHotkeys)
            s.CustomHotkeys[kv.Key] = kv.Value;
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
