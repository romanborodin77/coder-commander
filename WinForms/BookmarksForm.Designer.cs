using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class BookmarksForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private ListView _listView = null!;
    private ColumnHeader _colName = null!;
    private ColumnHeader _colPath = null!;
    private Panel _btnPanel = null!;
    private RoundedButton _closeBtn = null!;
    private FlowLayoutPanel _rightGroup = null!;
    private FlowLayoutPanel _leftGroup = null!;
    private RoundedButton _addBtn = null!;
    private RoundedButton _removeBtn = null!;

    /// <summary>Explicit disposal of the control fields (CA2213).</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _listView?.Dispose();
            _addBtn?.Dispose();
            _removeBtn?.Dispose();
            _closeBtn?.Dispose();
            _rightGroup?.Dispose();
            _leftGroup?.Dispose();
            _btnPanel?.Dispose();
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
        _listView = new ListView();
        _colName = new ColumnHeader();
        _colPath = new ColumnHeader();
        _btnPanel = new Panel();
        _closeBtn = new RoundedButton();
        _rightGroup = new FlowLayoutPanel();
        _leftGroup = new FlowLayoutPanel();
        _addBtn = new RoundedButton();
        _removeBtn = new RoundedButton();
        _btnPanel.SuspendLayout();
        _rightGroup.SuspendLayout();
        _leftGroup.SuspendLayout();
        SuspendLayout();
        //
        // _listView
        //
        _listView.BorderStyle = BorderStyle.None;
        _listView.Columns.AddRange(new[] { _colName, _colPath });
        _listView.Dock = DockStyle.Fill;
        _listView.FullRowSelect = true;
        _listView.Name = "_listView";
        _listView.UseCompatibleStateImageBehavior = false;
        _listView.View = View.Details;
        //
        // _colName
        //
        _colName.Text = "Name";
        _colName.Width = 150;
        //
        // _colPath
        //
        _colPath.Text = "Path";
        _colPath.Width = 400;
        //
        // _btnPanel
        //
        _btnPanel.Controls.Add(_rightGroup);
        _btnPanel.Controls.Add(_leftGroup);
        _btnPanel.Dock = DockStyle.Bottom;
        _btnPanel.Name = "_btnPanel";
        _btnPanel.Padding = new Padding(16, 8, 16, 8);
        _btnPanel.Size = new Size(600, 50);
        _uiMetadata.SetThemeRole(_btnPanel, ThemeRole.HeaderBackground);
        //
        // _rightGroup
        //
        // Docking _closeBtn straight into _btnPanel would stretch it to that panel's
        // inner height (50 less 8px of padding top and bottom = 34px), leaving it
        // visibly taller than the 32px buttons in _leftGroup. A right-docked FlowLayoutPanel
        // lets the button keep its natural size, and honours the Margin that Dock
        // ignores outright - the same shape ConnectionsForm uses for its own Close.
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
        // _leftGroup
        //
        // A FlowLayoutPanel, not two Dock.Left buttons: same-side docking stacks from the last-added
        // control outward, which had silently rendered these as "Remove Add" - and Dock.Left ignores
        // Margin entirely, which the flow panel honours.
        _leftGroup.AutoSize = true;
        _leftGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _leftGroup.BackColor = Color.Transparent;
        _leftGroup.Controls.Add(_addBtn);
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
        _uiMetadata.SetLocalizationKey(_addBtn, "Bookmark.Add");
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
        _uiMetadata.SetLocalizationKey(_removeBtn, "Bookmark.Remove");
        //
        // BookmarksForm
        //
        CancelButton = _closeBtn;
        ClientSize = new Size(600, 400);
        // Fill before Bottom - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_listView);
        Controls.Add(_btnPanel);
        MinimumSize = new Size(400, 280);
        Name = "BookmarksForm";
        Text = "Bookmarks";
        _uiMetadata.SetLocalizationKey(this, "Bookmark.Title");
        _rightGroup.ResumeLayout(false);
        _btnPanel.ResumeLayout(false);
        _leftGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
