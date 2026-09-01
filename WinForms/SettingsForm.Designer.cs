using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class SettingsForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private TableLayoutPanel _root = null!;
    private SettingsNavControl _nav = null!;
    private Panel _bottomPanel = null!;
    private FlowLayoutPanel _buttonGroup = null!;
    private RoundedButton _saveBtn = null!;
    private RoundedButton _cancelBtn = null!;

    /// <summary>
    /// Explicit disposal of every control field (CA2213), including the ~45 settings controls the
    /// constructor builds into <see cref="_nav"/>'s pages.
    ///
    /// <para>Those are redundant at runtime - each one is parented into a page and disposed with it -
    /// but the analyzer only sees a disposable field on this type and requires the call. A form can
    /// carry just one <c>Dispose(bool)</c> override, and by designer convention it lives here, so the
    /// whole list lives here too rather than split across the two halves of the partial class.</para>
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _saveBtn?.Dispose();
            _cancelBtn?.Dispose();
            _buttonGroup?.Dispose();
            _bottomPanel?.Dispose();
            _nav?.Dispose();
            _root?.Dispose();

            // Built by the constructor into the nav pages.
            _compressionFormatCombo?.Dispose();
            _compressionPresetCombo?.Dispose();
            _confirmDeleteCheck?.Dispose();
            _confirmOverwriteCheck?.Dispose();
            _copyAttrsCheck?.Dispose();
            _copyTsCheck?.Dispose();
            _defaultArchiveFormatCombo?.Dispose();
            _defaultShellCombo?.Dispose();
            _deleteOriginalsAfterCombineCheck?.Dispose();
            _deleteOriginalsAfterPackCheck?.Dispose();
            _deleteOriginalsAfterSplitCheck?.Dispose();
            _dirsFirstCheck?.Dispose();
            _extensionAddBox?.Dispose();
            _extensionsListBox?.Dispose();
            _externalEditorArgsBox?.Dispose();
            _externalEditorEnabledCheck?.Dispose();
            _externalEditorPathBox?.Dispose();
            _externalViewerArgsBox?.Dispose();
            _externalViewerEnabledCheck?.Dispose();
            _externalViewerPathBox?.Dispose();
            _flatViewCheck?.Dispose();
            _followPanelCwdCombo?.Dispose();
            _keyBindingPresetCombo?.Dispose();
            _languageCombo?.Dispose();
            _loadShellProfileCheck?.Dispose();
            _monoFontDisplayLabel?.Dispose();
            _showExtInNameCheck?.Dispose();
            _showFnButtonsCheck?.Dispose();
            _showHiddenCheck?.Dispose();
            _showStatusBarCheck?.Dispose();
            _showSystemCheck?.Dispose();
            _showToolbarCheck?.Dispose();
            _skipCompressionCheck?.Dispose();
            _splitPartSizeCombo?.Dispose();
            _splitWriteCrcCheck?.Dispose();
            _themeCombo?.Dispose();
            _uiFontDisplayLabel?.Dispose();
            _verifyCrcAfterCombineCheck?.Dispose();
            _viewerCsvDelimiterCombo?.Dispose();
            _viewerCsvHasHeaderCheck?.Dispose();
            _viewerEncodingCombo?.Dispose();
            _viewerHtmlAllowScriptsCheck?.Dispose();
            _viewerImageFitCheck?.Dispose();
            _viewerWordWrapCheck?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// The frame only - the root grid, the navigation host, and the button bar.
    ///
    /// <para><b>Deliberately a partial conversion.</b> The seven sections (Appearance, Panels, File
    /// operations, Archives, Split/Combine, Viewer/Editor, Terminal, Hotkeys) are built in the
    /// constructor and added through <c>_nav.AddPage</c>, because none of it is layout the designer
    /// could hold: every one of the ~45 controls is seeded from a current <see cref="AppSettings"/>
    /// value, several lists are enumerated at runtime (available languages, creatable archive
    /// formats, discovered shells), and <c>SettingsNavPage</c> is a plain class rather than a
    /// <see cref="Control"/>, so the default code-DOM serializer could not round-trip the pages
    /// even if their contents were static. What the designer does own is the frame: the split
    /// between the nav area and the 54px button bar, and the buttons themselves.</para>
    ///
    /// <para>The root is a two-row TableLayoutPanel rather than Dock=Fill plus Dock=Bottom, which
    /// sidesteps the docking-order pitfall entirely.</para>
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _root = new TableLayoutPanel();
        _nav = new SettingsNavControl();
        _bottomPanel = new Panel();
        _buttonGroup = new FlowLayoutPanel();
        _saveBtn = new RoundedButton();
        _cancelBtn = new RoundedButton();
        _root.SuspendLayout();
        _bottomPanel.SuspendLayout();
        _buttonGroup.SuspendLayout();
        SuspendLayout();
        //
        // _root
        //
        _root.ColumnCount = 1;
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _root.Controls.Add(_nav, 0, 0);
        _root.Controls.Add(_bottomPanel, 0, 1);
        _root.Dock = DockStyle.Fill;
        _root.Name = "_root";
        _root.RowCount = 2;
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
        _uiMetadata.SetThemeRole(_root, ThemeRole.Background);
        //
        // _nav
        //
        // Pages are added in the constructor - see InitializeComponent's own doc comment.
        _nav.Dock = DockStyle.Fill;
        _nav.Name = "_nav";
        //
        // _bottomPanel
        //
        _bottomPanel.Controls.Add(_buttonGroup);
        _bottomPanel.Dock = DockStyle.Fill;
        // Margin = 0: this is an Absolute 54 row, and WinForms' default 3px margin would render the
        // bar 48px tall - 6px short of the buttons it holds.
        _bottomPanel.Margin = new Padding(0);
        _bottomPanel.Name = "_bottomPanel";
        _bottomPanel.Padding = new Padding(16, 8, 16, 8);
        _uiMetadata.SetThemeRole(_bottomPanel, ThemeRole.HeaderBackground);
        //
        // _buttonGroup
        //
        _buttonGroup.AutoSize = true;
        _buttonGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _buttonGroup.BackColor = Color.Transparent;
        _buttonGroup.Controls.Add(_cancelBtn);
        _buttonGroup.Controls.Add(_saveBtn);
        _buttonGroup.Dock = DockStyle.Right;
        _buttonGroup.FlowDirection = FlowDirection.LeftToRight;
        _buttonGroup.Name = "_buttonGroup";
        _buttonGroup.WrapContents = false;
        //
        // _cancelBtn
        //
        _cancelBtn.AutoSize = true;
        _cancelBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _cancelBtn.DialogResult = DialogResult.Cancel;
        _cancelBtn.Margin = new Padding(0, 0, 8, 0);
        _cancelBtn.MinimumSize = new Size(100, 32);
        _cancelBtn.Name = "_cancelBtn";
        _cancelBtn.Padding = new Padding(20, 0, 20, 0);
        _cancelBtn.Role = ThemeRole.SecondaryButton;
        _cancelBtn.Text = "Cancel";
        _uiMetadata.SetLocalizationKey(_cancelBtn, "Common.Cancel");
        //
        // _saveBtn
        //
        _saveBtn.AutoSize = true;
        _saveBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _saveBtn.Margin = new Padding(0);
        _saveBtn.MinimumSize = new Size(100, 32);
        _saveBtn.Name = "_saveBtn";
        _saveBtn.Padding = new Padding(20, 0, 20, 0);
        _saveBtn.Role = ThemeRole.PrimaryButton;
        _saveBtn.Text = "Save";
        _uiMetadata.SetLocalizationKey(_saveBtn, "Common.Save");
        //
        // SettingsForm
        //
        AcceptButton = _saveBtn;
        CancelButton = _cancelBtn;
        Controls.Add(_root);
        // 620x480, matching SettingsService's own MinSettingsWindowWidth/Height. The old (560, 420)
        // did not, despite a doc comment there claiming it did: at that size the Archives section's
        // last row sat right at the AutoScroll viewport boundary and was clipped.
        MinimumSize = new Size(620, 480);
        Name = "SettingsForm";
        Text = "Settings";
        _uiMetadata.SetLocalizationKey(this, "Settings.Title");
        _root.ResumeLayout(false);
        _bottomPanel.ResumeLayout(false);
        _buttonGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
