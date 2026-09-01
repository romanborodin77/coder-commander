using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class CustomShellsForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private ListView _list = null!;
    private ColumnHeader _colName = null!;
    private ColumnHeader _colCommand = null!;
    private Panel _buttonBar = null!;
    private FlowLayoutPanel _leftGroup = null!;
    private RoundedButton _addBtn = null!;
    private RoundedButton _editBtn = null!;
    private RoundedButton _removeBtn = null!;
    private FlowLayoutPanel _rightGroup = null!;
    private RoundedButton _closeBtn = null!;

    /// <summary>Explicit disposal of the control fields (CA2213).</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _list?.Dispose();
            _addBtn?.Dispose();
            _editBtn?.Dispose();
            _removeBtn?.Dispose();
            _closeBtn?.Dispose();
            _leftGroup?.Dispose();
            _rightGroup?.Dispose();
            _buttonBar?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. Column captions are localized in the constructor - a
    /// <see cref="ColumnHeader"/> is not a <see cref="Control"/> and cannot carry a
    /// LocalizationKey.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _list = new ListView();
        _colName = new ColumnHeader();
        _colCommand = new ColumnHeader();
        _buttonBar = new Panel();
        _leftGroup = new FlowLayoutPanel();
        _addBtn = new RoundedButton();
        _editBtn = new RoundedButton();
        _removeBtn = new RoundedButton();
        _rightGroup = new FlowLayoutPanel();
        _closeBtn = new RoundedButton();
        _buttonBar.SuspendLayout();
        _leftGroup.SuspendLayout();
        _rightGroup.SuspendLayout();
        SuspendLayout();
        //
        // _list
        //
        _list.BorderStyle = BorderStyle.None;
        _list.Columns.AddRange(new[] { _colName, _colCommand });
        _list.Dock = DockStyle.Fill;
        _list.FullRowSelect = true;
        _list.Name = "_list";
        _list.UseCompatibleStateImageBehavior = false;
        _list.View = View.Details;
        //
        // _colName
        //
        _colName.Text = "Name";
        _colName.Width = 160;
        //
        // _colCommand
        //
        _colCommand.Text = "Command";
        _colCommand.Width = 280;
        //
        // _buttonBar
        //
        // Right group added before Left: both are edge-docked and WinForms lays docked children out
        // from the highest Controls index down, so the last-added claims its edge first.
        _buttonBar.Controls.Add(_rightGroup);
        _buttonBar.Controls.Add(_leftGroup);
        _buttonBar.Dock = DockStyle.Bottom;
        _buttonBar.Name = "_buttonBar";
        _buttonBar.Padding = new Padding(16, 10, 16, 10);
        _buttonBar.Size = new Size(560, 56);
        _uiMetadata.SetThemeRole(_buttonBar, ThemeRole.HeaderBackground);
        //
        // _leftGroup
        //
        _leftGroup.AutoSize = true;
        _leftGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _leftGroup.BackColor = Color.Transparent;
        _leftGroup.Controls.Add(_addBtn);
        _leftGroup.Controls.Add(_editBtn);
        _leftGroup.Controls.Add(_removeBtn);
        _leftGroup.Dock = DockStyle.Left;
        _leftGroup.FlowDirection = FlowDirection.LeftToRight;
        _leftGroup.Name = "_leftGroup";
        _leftGroup.WrapContents = false;
        //
        // _addBtn
        //
        _addBtn.AutoSize = true;
        _addBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _addBtn.Margin = new Padding(0, 0, 8, 0);
        _addBtn.MinimumSize = new Size(100, 32);
        _addBtn.Name = "_addBtn";
        _addBtn.Padding = new Padding(20, 0, 20, 0);
        _addBtn.Role = ThemeRole.PrimaryButton;
        _addBtn.Text = "Add";
        _uiMetadata.SetLocalizationKey(_addBtn, "Conn.Add");
        //
        // _editBtn
        //
        _editBtn.AutoSize = true;
        _editBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _editBtn.Margin = new Padding(0, 0, 8, 0);
        _editBtn.MinimumSize = new Size(100, 32);
        _editBtn.Name = "_editBtn";
        _editBtn.Padding = new Padding(20, 0, 20, 0);
        _editBtn.Role = ThemeRole.SecondaryButton;
        _editBtn.Text = "Edit";
        _uiMetadata.SetLocalizationKey(_editBtn, "Conn.Edit");
        //
        // _removeBtn
        //
        _removeBtn.AutoSize = true;
        _removeBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _removeBtn.Margin = new Padding(0);
        _removeBtn.MinimumSize = new Size(100, 32);
        _removeBtn.Name = "_removeBtn";
        _removeBtn.Padding = new Padding(20, 0, 20, 0);
        _removeBtn.Role = ThemeRole.SecondaryButton;
        _removeBtn.Text = "Remove";
        _uiMetadata.SetLocalizationKey(_removeBtn, "Conn.Remove");
        //
        // _rightGroup
        //
        _rightGroup.AutoSize = true;
        _rightGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _rightGroup.BackColor = Color.Transparent;
        _rightGroup.Controls.Add(_closeBtn);
        _rightGroup.Dock = DockStyle.Right;
        _rightGroup.FlowDirection = FlowDirection.LeftToRight;
        _rightGroup.Name = "_rightGroup";
        _rightGroup.WrapContents = false;
        //
        // _closeBtn
        //
        _closeBtn.AutoSize = true;
        _closeBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _closeBtn.Margin = new Padding(0);
        _closeBtn.MinimumSize = new Size(100, 32);
        _closeBtn.Name = "_closeBtn";
        _closeBtn.Padding = new Padding(20, 0, 20, 0);
        _closeBtn.Role = ThemeRole.SecondaryButton;
        _closeBtn.Text = "Close";
        _uiMetadata.SetLocalizationKey(_closeBtn, "Common.Close");
        //
        // CustomShellsForm
        //
        CancelButton = _closeBtn;
        ClientSize = new Size(560, 360);
        // Fill before Bottom - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_list);
        Controls.Add(_buttonBar);
        MinimumSize = new Size(420, 280);
        Name = "CustomShellsForm";
        Text = "Custom shells";
        _uiMetadata.SetLocalizationKey(this, "Settings.Terminal.CustomShells.Title");
        _buttonBar.ResumeLayout(false);
        _buttonBar.PerformLayout();
        _leftGroup.ResumeLayout(false);
        _rightGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
