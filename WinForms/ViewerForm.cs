using CoderCommander.Services;
using System.Text;

namespace CoderCommander.WinForms;

/// <summary>
/// Professional file viewer with support for text, images, and hex modes.
/// Features navigation, zoom, and file information.
/// </summary>
public class ViewerForm : ThemedForm
{
    private string _path;
    private long _fileSize;
    private string? _directory;
    private List<string> _files = new();
    private int _currentIndex;

    private ToolStrip _toolStrip = null!;
    private ThemedTabControl _tabControl = null!;
    private RichTextBox _textBox = null!;
    private PictureBox _pictureBox = null!;
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _lblFileInfo = null!;
    private ToolStripStatusLabel _lblPosition = null!;
    private ToolStripStatusLabel _lblMode = null!;
    private ToolStripStatusLabel _lblZoom = null!;

    private bool _hexMode;
    private bool _wordWrap;
    private float _zoom = 1.0f;
    private ViewerMode _currentMode = ViewerMode.Auto;

    private const long TextSizeLimit = 16 * 1024 * 1024; // 16MB
    private const int HexBytesPerRow = 16;
    private const int HexMaxBytes = 1024 * 1024; // 1MB

    private enum ViewerMode
    {
        Auto,
        Text,
        Hex,
        Image
    }

    public ViewerForm(string path, string? directory = null, List<string>? files = null, int currentIndex = 0)
    {
        _path = path;
        _directory = directory;
        _files = files ?? new List<string>();
        _currentIndex = currentIndex;

        if (File.Exists(path))
        {
            var fi = new FileInfo(path);
            _fileSize = fi.Length;
        }

        var L = LocalizationService.Current;
        Text = $"{L.GetString("View.Title")} — {Path.GetFileName(path)}";
        ClientSize = new Size(1000, 700);
        Resizable = true;
        MinimumSize = new Size(500, 400);

        BuildToolbar();
        BuildTabControl();
        BuildStatusBar();

        // WinForms: Fill must be at index 0 (drawn first, gets remaining space).
        // Top/Bottom drawn on top. Fix docking overlap.
        Controls.SetChildIndex(_tabControl, 0);
        Controls.SetChildIndex(_toolStrip, 1);
        Controls.SetChildIndex(_statusStrip, 2);

        KeyDown += OnViewerKeyDown;
        Load += (_, _) => LoadFile();
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        var p = ThemeService.Current;
        ApplyTheme();
        _textBox.BackColor = p.PanelBackground;
        _textBox.ForeColor = p.Foreground;
        _pictureBox.BackColor = p.PanelBackground;
    }

    private void BuildToolbar()
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        _toolStrip = new ToolStrip
        {
            Dock = DockStyle.Top,
            ImageScalingSize = new Size(16, 16),
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(4, 2, 4, 2),
            Renderer = new ThemeRenderer()
        };

        // Navigation
        _toolStrip.Items.Add(CreateToolButton("View.Toolbar.Previous", "back", (_, _) => NavigateFile(-1)));
        _toolStrip.Items.Add(CreateToolButton("View.Toolbar.Next", "forward", (_, _) => NavigateFile(1)));
        _toolStrip.Items.Add(new ToolStripSeparator());

        // View modes
        var textBtn = new ToolStripButton(L.GetString("View.Text"), ToolbarIcons.Get("view"))
        {
            CheckOnClick = true,
            Checked = !_hexMode
        };
        textBtn.Click += (_, _) => SwitchToMode(false);
        _toolStrip.Items.Add(textBtn);

        var hexBtn = new ToolStripButton(L.GetString("View.Hex"), ToolbarIcons.Get("view"))
        {
            CheckOnClick = true,
            Checked = _hexMode
        };
        hexBtn.Click += (_, _) => SwitchToMode(true);
        _toolStrip.Items.Add(hexBtn);

        _toolStrip.Items.Add(new ToolStripSeparator());

        // Zoom controls
        var zoomOutBtn = new ToolStripButton("-", ToolbarIcons.Get("view"))
        {
            ToolTipText = L.GetString("View.ZoomOut")
        };
        zoomOutBtn.Click += (_, _) => ChangeZoom(-0.1f);
        _toolStrip.Items.Add(zoomOutBtn);

        var zoomLabel = new ToolStripLabel("100%")
        {
            Enabled = false
        };
        _toolStrip.Items.Add(zoomLabel);

        var zoomInBtn = new ToolStripButton("+", ToolbarIcons.Get("view"))
        {
            ToolTipText = L.GetString("View.ZoomIn")
        };
        zoomInBtn.Click += (_, _) => ChangeZoom(0.1f);
        _toolStrip.Items.Add(zoomInBtn);

        _toolStrip.Items.Add(new ToolStripSeparator());

        // Word wrap
        var wordWrapBtn = new ToolStripButton(L.GetString("View.WordWrap"))
        {
            CheckOnClick = true,
            Checked = false
        };
        wordWrapBtn.Click += (_, _) =>
        {
            _wordWrap = wordWrapBtn.Checked;
            _textBox.WordWrap = _wordWrap;
        };
        _toolStrip.Items.Add(wordWrapBtn);

        _toolStrip.Items.Add(new ToolStripSeparator());

        // Close
        var closeBtn = new ToolStripButton(L.GetString("Common.Close"), ToolbarIcons.Get("close"));
        closeBtn.Click += (_, _) => Close();
        _toolStrip.Items.Add(closeBtn);

        Controls.Add(_toolStrip);
    }

    private ToolStripButton CreateToolButton(string textKey, string iconKey, EventHandler onClick)
    {
        var L = LocalizationService.Current;
        var btn = new ToolStripButton(L.GetString(textKey), ToolbarIcons.Get(iconKey))
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ToolTipText = L.GetString(textKey)
        };
        btn.Click += onClick;
        return btn;
    }

    private void BuildTabControl()
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;
        _tabControl = new ThemedTabControl
        {
            Dock = DockStyle.Fill
        };

        // Text/Hex tab
        _textBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = p.PanelBackground,
            ForeColor = p.Foreground,
            BorderStyle = BorderStyle.None,
            Font = p.MonoFont,
            WordWrap = false,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Both
        };
        var textPage = new ThemedTabPage(L.GetString("View.TabText"), _textBox);
        _tabControl.AddPage(textPage);

        // Image tab
        var imagePanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = p.PanelBackground,
            Tag = ThemeRole.PanelBackground
        };
        _pictureBox = new PictureBox
        {
            BackColor = p.PanelBackground,
            SizeMode = PictureBoxSizeMode.AutoSize,
            Cursor = Cursors.SizeAll
        };
        _pictureBox.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                _pictureBox.Cursor = Cursors.Hand;
        };
        _pictureBox.MouseUp += (_, _) => _pictureBox.Cursor = Cursors.SizeAll;
        imagePanel.Controls.Add(_pictureBox);
        var imagePage = new ThemedTabPage(L.GetString("View.TabImage"), imagePanel);
        _tabControl.AddPage(imagePage);
        Controls.Add(_tabControl);
    }

    private void BuildStatusBar()
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        _statusStrip = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            SizingGrip = true,
            Renderer = new ThemeRenderer()
        };

        _lblFileInfo = new ToolStripStatusLabel
        {
            Text = "",
            ForeColor = p.DimForeground,
            Margin = new Padding(4, 0, 8, 0)
        };

        _lblPosition = new ToolStripStatusLabel
        {
            Text = "",
            ForeColor = p.DimForeground,
            Margin = new Padding(4, 0, 8, 0)
        };

        _lblMode = new ToolStripStatusLabel
        {
            Text = L.GetString("View.ModeAuto"),
            ForeColor = p.DimForeground,
            Margin = new Padding(4, 0, 8, 0)
        };

        _lblZoom = new ToolStripStatusLabel
        {
            Text = "100%",
            ForeColor = p.DimForeground,
            Margin = new Padding(4, 0, 8, 0)
        };

        _statusStrip.Items.AddRange(new ToolStripItem[]
        {
            _lblFileInfo,
            new ToolStripSeparator(),
            _lblPosition,
            new ToolStripSeparator(),
            _lblMode,
            new ToolStripSeparator(),
            _lblZoom
        });

        Controls.Add(_statusStrip);
    }

    private void OnViewerKeyDown(object? sender, KeyEventArgs e)
    {
        var L = LocalizationService.Current;

        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Up)
        {
            NavigateFile(-1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Down)
        {
            NavigateFile(1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F5)
        {
            LoadFile();
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.Oemplus)
        {
            ChangeZoom(0.1f);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.OemMinus)
        {
            ChangeZoom(-0.1f);
            e.Handled = true;
        }
    }

    private void NavigateFile(int direction)
    {
        if (_files.Count == 0) return;

        _currentIndex += direction;
        if (_currentIndex < 0) _currentIndex = _files.Count - 1;
        if (_currentIndex >= _files.Count) _currentIndex = 0;

        _path = _files[_currentIndex];
        if (File.Exists(_path))
        {
            var fi = new FileInfo(_path);
            _fileSize = fi.Length;
        }

        var L = LocalizationService.Current;
        Text = $"{L.GetString("View.Title")} — {Path.GetFileName(_path)}";
        LoadFile();
    }

    private void ChangeZoom(float delta)
    {
        _zoom = Math.Max(0.1f, Math.Min(5.0f, _zoom + delta));
        
        if (_pictureBox.Image != null)
        {
            var newSize = new Size(
                (int)(_pictureBox.Image.Width * _zoom),
                (int)(_pictureBox.Image.Height * _zoom)
            );
            _pictureBox.Size = newSize;
        }
        
        _lblZoom.Text = $"{(int)(_zoom * 100)}%";
    }

    private void SwitchToMode(bool hex)
    {
        _hexMode = hex;
        _currentMode = hex ? ViewerMode.Hex : ViewerMode.Text;
        LoadFile();
    }

    private void LoadFile()
    {
        var L = LocalizationService.Current;
        try
        {
            if (!File.Exists(_path))
            {
                _textBox.Text = L.GetString("View.FileNotFound");
                UpdateStatus("");
                return;
            }

            // Auto-detect mode
            if (_currentMode == ViewerMode.Auto)
            {
                var ext = Path.GetExtension(_path).ToLowerInvariant();
                if (IsImageFile(ext))
                {
                    _tabControl.SelectedIndex = 1; // Image tab
                    LoadImage();
                    UpdateStatus(L.GetString("View.ImageMode"));
                    return;
                }
            }

            // Text or Hex mode
            _tabControl.SelectedIndex = 0; // Text/Hex tab

            if (_hexMode)
            {
                LoadHex();
                UpdateStatus(L.GetString("View.HexMode", FormatSize(_fileSize)));
            }
            else
            {
                LoadText();
                UpdateStatus(L.GetString("View.TextMode", FormatSize(_fileSize)));
            }
        }
        catch (Exception ex)
        {
            _textBox.Text = $"{L.GetString("View.Error")}: {ex.Message}";
            LogService.Error($"Viewer load failed: {_path}", ex);
        }
    }

    private bool IsImageFile(string ext)
    {
        return ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".ico" or ".svg" or ".webp" or ".tiff";
    }

    private void LoadImage()
    {
        try
        {
            var image = Image.FromFile(_path);
            _pictureBox.Image = image;
            _zoom = 1.0f;
            _lblZoom.Text = "100%";
        }
        catch (Exception ex)
        {
            var L = LocalizationService.Current;
            StyledMessageBox.Show(L.GetString("View.Error") + ": " + ex.Message,
                L.GetString("Common.Error"), MsgBoxButtons.OK, MsgBoxIcon.Error);
        }
    }

    private void LoadText()
    {
        var L = LocalizationService.Current;

        if (_fileSize > TextSizeLimit)
        {
            _textBox.Text = L.GetString("View.TooBigForText", FormatSize(_fileSize), FormatSize(TextSizeLimit));
            return;
        }

        var bytes = File.ReadAllBytes(_path);
        var encoding = TextEncodingDetector.Detect(bytes, out var preambleLength);
        _textBox.Text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
    }

    private void LoadHex()
    {
        var L = LocalizationService.Current;
        var sb = new StringBuilder();

        if (_fileSize > HexMaxBytes)
        {
            sb.AppendLine(L.GetString("View.HexTruncated", FormatSize(HexMaxBytes), FormatSize(_fileSize)));
            sb.AppendLine();
        }

        var bytes = File.ReadAllBytes(_path);
        var limit = (int)Math.Min(bytes.Length, HexMaxBytes);

        for (int i = 0; i < limit; i += HexBytesPerRow)
        {
            // Offset
            sb.Append($"{i:X8}  ");

            // Hex bytes
            for (int j = 0; j < HexBytesPerRow; j++)
            {
                if (i + j < limit)
                    sb.Append($"{bytes[i + j]:X2} ");
                else
                    sb.Append("   ");
                if (j == 7) sb.Append(' ');
            }

            sb.Append(' ');

            // ASCII
            for (int j = 0; j < HexBytesPerRow && i + j < limit; j++)
            {
                var c = bytes[i + j];
                sb.Append(c >= 0x20 && c < 0x7F ? (char)c : '.');
            }
            sb.AppendLine();
        }

        if (bytes.Length > HexMaxBytes)
            sb.AppendLine($"... ({FormatSize(bytes.Length - HexMaxBytes)} more)");

        _textBox.Text = sb.ToString();
    }

    private void UpdateStatus(string mode)
    {
        var ext = FileSystem.FileEntry.GetExtension(_path).ToUpperInvariant().TrimStart('.');
        _lblFileInfo.Text = $"{Path.GetFileName(_path)} ({FormatSize(_fileSize)})";
        _lblMode.Text = mode;
        _lblPosition.Text = ext;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 0) return "—";
        if (bytes == 0) return "0 B";
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double s = bytes; int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return $"{s:0.##} {u[i]}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeService.ThemeChanged -= OnThemeChanged;
            _pictureBox.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
