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
    private Label _status = null!;
    private Panel _buttonBar = null!;
    private FlowLayoutPanel _buttonGroup = null!;
    private RoundedButton _startBtn = null!;
    private RoundedButton _goToBtn = null!;
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
            _closeBtn?.Dispose();
            _buttonGroup?.Dispose();
            _buttonBar?.Dispose();
            _options?.Dispose();
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
        _status = new Label();
        _buttonBar = new Panel();
        _buttonGroup = new FlowLayoutPanel();
        _startBtn = new RoundedButton();
        _goToBtn = new RoundedButton();
        _closeBtn = new RoundedButton();
        _resultsHost.SuspendLayout();
        _queryLayout.SuspendLayout();
        _options.SuspendLayout();
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
        // Four rows of known height plus the padding is a number this dialog can simply state.
        _queryLayout.ColumnCount = 2;
        _queryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        _queryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _queryLayout.Controls.Add(_maskLabel, 0, 0);
        _queryLayout.Controls.Add(_maskBox, 1, 0);
        _queryLayout.Controls.Add(_textLabel, 0, 1);
        _queryLayout.Controls.Add(_textBox, 1, 1);
        _queryLayout.Controls.Add(_options, 1, 2);
        _queryLayout.Controls.Add(_status, 1, 3);
        _queryLayout.Dock = DockStyle.Top;
        _queryLayout.Name = "_queryLayout";
        _queryLayout.Padding = new Padding(16, 12, 16, 8);
        _queryLayout.RowCount = 4;
        _queryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _queryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _queryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        _queryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        _queryLayout.Size = new Size(820, 142); // 12 + 32 + 32 + 34 + 24 + 8
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
        // _closeBtn
        //
        _closeBtn.AutoSize = true;
        _closeBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
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
        _buttonBar.ResumeLayout(false);
        _buttonGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
