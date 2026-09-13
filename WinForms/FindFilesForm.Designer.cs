using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class FindFilesForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private Panel _resultsHost = null!;
    private ListView _results = null!;
    private ColumnHeader _colName = null!;
    private ColumnHeader _colFolder = null!;
    private ColumnHeader _colSize = null!;
    private ColumnHeader _colLine = null!;
    private ColumnHeader _colText = null!;
    private TableLayoutPanel _queryLayout = null!;
    private Label _maskLabel = null!;
    private TextBox _maskBox = null!;
    private Label _textLabel = null!;
    private TextBox _textBox = null!;
    private FlowLayoutPanel _options = null!;
    private ThemedCheckBox _matchCaseCheck = null!;
    private ThemedCheckBox _wholeWordCheck = null!;
    private ThemedCheckBox _subdirectoriesCheck = null!;
    private ThemedCheckBox _regexCheck = null!;
    private Label _sizeLabel = null!;
    private FlowLayoutPanel _sizePanel = null!;
    private NumericUpDown _sizeMinBox = null!;
    private Label _sizeToLabel = null!;
    private NumericUpDown _sizeMaxBox = null!;
    private Label _modifiedLabel = null!;
    private FlowLayoutPanel _datePanel = null!;
    private DateTimePicker _modifiedFromPicker = null!;
    private DateTimePicker _modifiedToPicker = null!;
    private Label _status = null!;
    private Panel _buttonBar = null!;
    private FlowLayoutPanel _buttonGroup = null!;
    private RoundedButton _startBtn = null!;
    private RoundedButton _goToBtn = null!;
    private RoundedButton _feedBtn = null!;
    private RoundedButton _closeBtn = null!;
    private System.Windows.Forms.Timer _flushTimer = null!;

    /// <summary>Explicit disposal of the control fields (CA2213). The flush timer belongs to
    /// <see cref="components"/>, so disposing that covers it.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _results?.Dispose();
            _maskBox?.Dispose();
            _textBox?.Dispose();
            _maskLabel?.Dispose();
            _textLabel?.Dispose();
            _matchCaseCheck?.Dispose();
            _wholeWordCheck?.Dispose();
            _subdirectoriesCheck?.Dispose();
            _regexCheck?.Dispose();
            _status?.Dispose();
            _startBtn?.Dispose();
            _goToBtn?.Dispose();
            _feedBtn?.Dispose();
            _closeBtn?.Dispose();
            _buttonGroup?.Dispose();
            _buttonBar?.Dispose();
            _options?.Dispose();
            _sizeLabel?.Dispose();
            _sizePanel?.Dispose();
            _sizeMinBox?.Dispose();
            _sizeToLabel?.Dispose();
            _sizeMaxBox?.Dispose();
            _modifiedLabel?.Dispose();
            _datePanel?.Dispose();
            _modifiedFromPicker?.Dispose();
            _modifiedToPicker?.Dispose();
            _queryLayout?.Dispose();
            _resultsHost?.Dispose();
            // Owned by the behaviour half; cancelled first because a search may still be running.
            _cancellation?.Cancel();
            _cancellation?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. The four option checkboxes are widened to their own captions in the
    /// constructor - see SizeToText for why AutoSize cannot do it for an owner-drawn control.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _flushTimer = new System.Windows.Forms.Timer(components);
        _resultsHost = new Panel();
        _results = new ListView();
        _colName = new ColumnHeader();
        _colFolder = new ColumnHeader();
        _colSize = new ColumnHeader();
        _colLine = new ColumnHeader();
        _colText = new ColumnHeader();
        _queryLayout = new TableLayoutPanel();
        _maskLabel = new Label();
        _maskBox = new TextBox();
        _textLabel = new Label();
        _textBox = new TextBox();
        _options = new FlowLayoutPanel();
        _matchCaseCheck = new ThemedCheckBox();
        _wholeWordCheck = new ThemedCheckBox();
        _subdirectoriesCheck = new ThemedCheckBox();
        _regexCheck = new ThemedCheckBox();
        _sizeLabel = new Label();
        _sizePanel = new FlowLayoutPanel();
        _sizeMinBox = new NumericUpDown();
        _sizeToLabel = new Label();
        _sizeMaxBox = new NumericUpDown();
        _modifiedLabel = new Label();
        _datePanel = new FlowLayoutPanel();
        _modifiedFromPicker = new DateTimePicker();
        _modifiedToPicker = new DateTimePicker();
        _status = new Label();
        _buttonBar = new Panel();
        _buttonGroup = new FlowLayoutPanel();
        _startBtn = new RoundedButton();
        _goToBtn = new RoundedButton();
        _feedBtn = new RoundedButton();
        _closeBtn = new RoundedButton();
        _resultsHost.SuspendLayout();
        _queryLayout.SuspendLayout();
        _options.SuspendLayout();
        _sizePanel.SuspendLayout();
        _datePanel.SuspendLayout();
        _buttonBar.SuspendLayout();
        _buttonGroup.SuspendLayout();
        SuspendLayout();
        //
        // _resultsHost
        //
        _resultsHost.Controls.Add(_results);
        _resultsHost.Dock = DockStyle.Fill;
        _resultsHost.Name = "_resultsHost";
        _resultsHost.Padding = new Padding(16, 0, 16, 0);
        _uiMetadata.SetThemeRole(_resultsHost, ThemeRole.Background);
        //
        // _results
        //
        _results.BorderStyle = BorderStyle.None;
        _results.Columns.AddRange(new[] { _colName, _colFolder, _colSize, _colLine, _colText });
        _results.Dock = DockStyle.Fill;
        _results.FullRowSelect = true;
        _results.Name = "_results";
        _results.UseCompatibleStateImageBehavior = false;
        _results.View = View.Details;
        //
        // _colName
        //
        _colName.Text = "Name";
        _colName.Width = 200;
        //
        // _colFolder
        //
        _colFolder.Text = "Folder";
        _colFolder.Width = 260;
        //
        // _colSize
        //
        _colSize.Text = "Size";
        _colSize.Width = 90;
        //
        // _colLine
        //
        _colLine.Text = "Line";
        _colLine.Width = 60;
        //
        // _colText
        //
        _colText.Text = "Text";
        _colText.Width = 320;
        //
        // _queryLayout
        //
        // A fixed height, not AutoSize: an auto-sizing Dock=Top panel settles its height after the
        // form's first layout pass, and the Dock=Fill sibling below it is measured before that
        // happens - which pushed the bottom button bar past the client area and clipped the buttons.
        // Six rows of known height plus the padding is a number this dialog can simply state.
        _queryLayout.ColumnCount = 2;
        _queryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        _queryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _queryLayout.Controls.Add(_maskLabel, 0, 0);
        _queryLayout.Controls.Add(_maskBox, 1, 0);
        _queryLayout.Controls.Add(_textLabel, 0, 1);
        _queryLayout.Controls.Add(_textBox, 1, 1);
        _queryLayout.Controls.Add(_sizeLabel, 0, 2);
        _queryLayout.Controls.Add(_sizePanel, 1, 2);
        _queryLayout.Controls.Add(_modifiedLabel, 0, 3);
        _queryLayout.Controls.Add(_datePanel, 1, 3);
        _queryLayout.Controls.Add(_options, 1, 4);
        _queryLayout.Controls.Add(_status, 1, 5);
        _queryLayout.Dock = DockStyle.Top;
        _queryLayout.Name = "_queryLayout";
        _queryLayout.Padding = new Padding(16, 12, 16, 8);
        _queryLayout.RowCount = 6;
        _queryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _queryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _queryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _queryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _queryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        _queryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        _queryLayout.Size = new Size(820, 206); // 12 + 32 + 32 + 32 + 32 + 34 + 24 + 8
        _uiMetadata.SetThemeRole(_queryLayout, ThemeRole.Background);
        //
        // _maskLabel
        //
        _maskLabel.AutoSize = true;
        _maskLabel.Dock = DockStyle.Fill;
        _maskLabel.Name = "_maskLabel";
        _maskLabel.Text = "File mask";
        _maskLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_maskLabel, "Find.Field.Mask");
        _uiMetadata.SetThemeRole(_maskLabel, ThemeRole.Body);
        //
        // _maskBox
        //
        _maskBox.BorderStyle = BorderStyle.FixedSingle;
        _maskBox.Dock = DockStyle.Fill;
        _maskBox.Name = "_maskBox";
        _maskBox.Text = "*.*";
        //
        // _textLabel
        //
        _textLabel.AutoSize = true;
        _textLabel.Dock = DockStyle.Fill;
        _textLabel.Name = "_textLabel";
        _textLabel.Text = "Containing text";
        _textLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_textLabel, "Find.Field.Text");
        _uiMetadata.SetThemeRole(_textLabel, ThemeRole.Body);
        //
        // _textBox
        //
        _textBox.BorderStyle = BorderStyle.FixedSingle;
        _textBox.Dock = DockStyle.Fill;
        _textBox.Name = "_textBox";
        //
        // _options
        //
        _options.BackColor = Color.Transparent;
        _options.Controls.Add(_matchCaseCheck);
        _options.Controls.Add(_wholeWordCheck);
        _options.Controls.Add(_subdirectoriesCheck);
        _options.Controls.Add(_regexCheck);
        _options.Dock = DockStyle.Fill;
        _options.FlowDirection = FlowDirection.LeftToRight;
        _options.Name = "_options";
        _options.WrapContents = false;
        //
        // _matchCaseCheck
        //
        // Widths are assigned in the constructor by SizeToText - ThemedCheckBox is owner-drawn, so
        // AutoSize has nothing to measure and the caption would be silently truncated, differently
        // per language.
        _matchCaseCheck.Margin = new Padding(0, 0, 16, 0);
        _matchCaseCheck.Name = "_matchCaseCheck";
        _matchCaseCheck.Text = "Match case";
        _uiMetadata.SetLocalizationKey(_matchCaseCheck, "Find.MatchCase");
        //
        // _wholeWordCheck
        //
        _wholeWordCheck.Margin = new Padding(0, 0, 16, 0);
        _wholeWordCheck.Name = "_wholeWordCheck";
        _wholeWordCheck.Text = "Whole word";
        _uiMetadata.SetLocalizationKey(_wholeWordCheck, "Find.WholeWord");
        //
        // _subdirectoriesCheck
        //
        _subdirectoriesCheck.Margin = new Padding(0, 0, 16, 0);
        _subdirectoriesCheck.Name = "_subdirectoriesCheck";
        _subdirectoriesCheck.Text = "Subdirectories";
        _uiMetadata.SetLocalizationKey(_subdirectoriesCheck, "Find.Subdirectories");
        //
        // _regexCheck
        //
        _regexCheck.Margin = new Padding(0, 0, 16, 0);
        _regexCheck.Name = "_regexCheck";
        _regexCheck.Text = "Regular expression";
        _uiMetadata.SetLocalizationKey(_regexCheck, "Find.UseRegex");
        //
        // _sizeLabel
        //
        _sizeLabel.AutoSize = true;
        _sizeLabel.Dock = DockStyle.Fill;
        _sizeLabel.Name = "_sizeLabel";
        _sizeLabel.Text = "Size (KB):";
        _sizeLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_sizeLabel, "Find.Field.Size");
        _uiMetadata.SetThemeRole(_sizeLabel, ThemeRole.Body);
        //
        // _sizePanel
        //
        // Both bounds are inclusive KB values; 0 means "no bound" (the constructor clamps the
        // NumericUpDowns at 0). Min has a right margin, the "–" caption carries the gap.
        _sizePanel.BackColor = Color.Transparent;
        _sizePanel.Controls.Add(_sizeMinBox);
        _sizePanel.Controls.Add(_sizeToLabel);
        _sizePanel.Controls.Add(_sizeMaxBox);
        _sizePanel.Dock = DockStyle.Fill;
        _sizePanel.FlowDirection = FlowDirection.LeftToRight;
        _sizePanel.Name = "_sizePanel";
        _sizePanel.WrapContents = false;
        //
        // _sizeMinBox
        //
        _sizeMinBox.Margin = new Padding(0, 5, 0, 0);
        _sizeMinBox.Maximum = 1000000000;
        _sizeMinBox.Name = "_sizeMinBox";
        _sizeMinBox.ThousandsSeparator = true;
        _sizeMinBox.Width = 90;
        //
        // _sizeToLabel
        //
        _sizeToLabel.AutoSize = true;
        _sizeToLabel.Margin = new Padding(6, 9, 6, 0);
        _sizeToLabel.Name = "_sizeToLabel";
        _sizeToLabel.Text = "–";
        _sizeToLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetThemeRole(_sizeToLabel, ThemeRole.Muted);
        //
        // _sizeMaxBox
        //
        _sizeMaxBox.Margin = new Padding(0, 5, 0, 0);
        _sizeMaxBox.Maximum = 1000000000;
        _sizeMaxBox.Name = "_sizeMaxBox";
        _sizeMaxBox.ThousandsSeparator = true;
        _sizeMaxBox.Width = 90;
        //
        // _modifiedLabel
        //
        _modifiedLabel.AutoSize = true;
        _modifiedLabel.Dock = DockStyle.Fill;
        _modifiedLabel.Name = "_modifiedLabel";
        _modifiedLabel.Text = "Modified:";
        _modifiedLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_modifiedLabel, "Find.Field.Modified");
        _uiMetadata.SetThemeRole(_modifiedLabel, ThemeRole.Body);
        //
        // _datePanel
        //
        // Each picker carries its own checkbox (unchecked = the bound is off); the constructor
        // pins Value to today so the short-format text is a real date rather than a designer one.
        _datePanel.BackColor = Color.Transparent;
        _datePanel.Controls.Add(_modifiedFromPicker);
        _datePanel.Controls.Add(_modifiedToPicker);
        _datePanel.Dock = DockStyle.Fill;
        _datePanel.FlowDirection = FlowDirection.LeftToRight;
        _datePanel.Name = "_datePanel";
        _datePanel.WrapContents = false;
        //
        // _modifiedFromPicker
        //
        _modifiedFromPicker.Checked = false;
        _modifiedFromPicker.Format = DateTimePickerFormat.Short;
        _modifiedFromPicker.Margin = new Padding(0, 5, 8, 0);
        _modifiedFromPicker.Name = "_modifiedFromPicker";
        _modifiedFromPicker.ShowCheckBox = true;
        _modifiedFromPicker.Width = 130;
        //
        // _modifiedToPicker
        //
        _modifiedToPicker.Checked = false;
        _modifiedToPicker.Format = DateTimePickerFormat.Short;
        _modifiedToPicker.Margin = new Padding(0, 5, 0, 0);
        _modifiedToPicker.Name = "_modifiedToPicker";
        _modifiedToPicker.ShowCheckBox = true;
        _modifiedToPicker.Width = 130;
        //
        // _status
        //
        // Text is the "searching in <path>" line, built in code.
        //
        // AutoEllipsis, and therefore AutoSize=false, because a path is one unbreakable token: at
        // AutoSize=true the label word-wrapped, and a path too long for what remains of line one
        // moved to line two in its entirety - which this label's height does not show. The dialog
        // then displayed "Searching in:" and no path at all, rather than a truncated one. Same
        // treatment HotkeyBindingsForm._hint and OperationDialogForm._currentFileLabel already use.
        _status.AutoEllipsis = true;
        _status.AutoSize = false;
        _status.Dock = DockStyle.Fill;
        _status.Name = "_status";
        _uiMetadata.SetThemeRole(_status, ThemeRole.Hint);
        //
        // _buttonBar
        //
        _buttonBar.Controls.Add(_buttonGroup);
        _buttonBar.Dock = DockStyle.Bottom;
        _buttonBar.Name = "_buttonBar";
        _buttonBar.Padding = new Padding(16, 10, 16, 10);
        _buttonBar.Size = new Size(820, 56);
        _uiMetadata.SetThemeRole(_buttonBar, ThemeRole.HeaderBackground);
        //
        // _buttonGroup
        //
        _buttonGroup.AutoSize = true;
        _buttonGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _buttonGroup.BackColor = Color.Transparent;
        _buttonGroup.Controls.Add(_startBtn);
        _buttonGroup.Controls.Add(_goToBtn);
        _buttonGroup.Controls.Add(_feedBtn);
        _buttonGroup.Controls.Add(_closeBtn);
        _buttonGroup.Dock = DockStyle.Right;
        _buttonGroup.FlowDirection = FlowDirection.LeftToRight;
        _buttonGroup.Name = "_buttonGroup";
        _buttonGroup.WrapContents = false;
        //
        // _startBtn
        //
        // Caption flips between Start and Stop while a search runs, so it is set in code.
        _startBtn.AutoSize = true;
        _startBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _startBtn.Margin = new Padding(0, 0, 8, 0);
        _startBtn.MinimumSize = new Size(100, 32);
        _startBtn.Name = "_startBtn";
        _startBtn.Padding = new Padding(20, 0, 20, 0);
        _startBtn.Role = ThemeRole.PrimaryButton;
        _startBtn.Text = "Start";
        //
        // _goToBtn
        //
        _goToBtn.AutoSize = true;
        _goToBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _goToBtn.Margin = new Padding(0, 0, 8, 0);
        _goToBtn.MinimumSize = new Size(100, 32);
        _goToBtn.Name = "_goToBtn";
        _goToBtn.Padding = new Padding(20, 0, 20, 0);
        _goToBtn.Role = ThemeRole.SecondaryButton;
        _goToBtn.Text = "Go to file";
        _uiMetadata.SetLocalizationKey(_goToBtn, "Find.GoTo");
        //
        // _feedBtn
        //
        // Left=8 matches the sibling gap convention (see _closeBtn's mirror comment).
        _feedBtn.AutoSize = true;
        _feedBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _feedBtn.Margin = new Padding(8, 0, 0, 0);
        _feedBtn.MinimumSize = new Size(100, 32);
        _feedBtn.Name = "_feedBtn";
        _feedBtn.Padding = new Padding(20, 0, 20, 0);
        _feedBtn.Role = ThemeRole.SecondaryButton;
        _feedBtn.Text = "Show in panel";
        _uiMetadata.SetLocalizationKey(_feedBtn, "Find.FeedToPanel");
        //
        // _closeBtn
        //
        _closeBtn.AutoSize = true;
        _closeBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        // Left=8 matches the inter-button gap of the siblings (their Margin.Right); Top=0 keeps
        // the button on the same optical line - the default (3,3,3,3) margin sank it 3px below
        // the other two (LayoutAuditTests Detector 6/7).
        _closeBtn.Margin = new Padding(8, 0, 0, 0);
        _closeBtn.MinimumSize = new Size(100, 32);
        _closeBtn.Name = "_closeBtn";
        _closeBtn.Padding = new Padding(20, 0, 20, 0);
        _closeBtn.Role = ThemeRole.SecondaryButton;
        _closeBtn.Text = "Close";
        _uiMetadata.SetLocalizationKey(_closeBtn, "Common.Close");
        //
        // FindFilesForm
        //
        AcceptButton = _startBtn;
        // Escape closes, per the convention every dialog here follows.
        CancelButton = _closeBtn;
        ClientSize = new Size(820, 520);
        // Fill first, then every docked sibling - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_resultsHost);
        Controls.Add(_queryLayout);
        Controls.Add(_buttonBar);
        MinimumSize = new Size(620, 400);
        Name = "FindFilesForm";
        Text = "Find files";
        _uiMetadata.SetLocalizationKey(this, "Find.Title");
        _resultsHost.ResumeLayout(false);
        _queryLayout.ResumeLayout(false);
        _queryLayout.PerformLayout();
        _options.ResumeLayout(false);
        _sizePanel.ResumeLayout(false);
        _datePanel.ResumeLayout(false);
        _buttonBar.ResumeLayout(false);
        _buttonGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
