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

    /// <summary>
    /// Initializes the viewer form with toolbar, tab control (text/image), and status bar.
    /// Loads the specified file in auto-detect mode.
    /// </summary>
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

    /// <summary>Re-applies the theme to text box and picture box on theme change.</summary>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        var p = ThemeService.Current;
        ApplyTheme();
        _textBox.BackColor = p.PanelBackground;
        _textBox.ForeColor = p.Foreground;
        _pictureBox.BackColor = p.PanelBackground;
    }

    /// <summary>Builds the toolbar with navigation, view mode, zoom, word wrap, and close buttons.</summary>
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

    /// <summary>Creates a toolbar button with localized text and icon.</summary>
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

    /// <summary>Builds the tab control with text/hex and image tabs.</summary>
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

    /// <summary>Builds the status bar with file info, position, mode, and zoom labels.</summary>
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

    /// <summary>Handles keyboard shortcuts: Escape (close), arrows (navigate), F5 (reload), Ctrl+/Ctrl- (zoom).</summary>
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

    /// <summary>Navigates to the next or previous file in the file list (wrapping around).</summary>
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

    /// <summary>Adjusts the image zoom level within the 10%-500% range and updates the zoom label.</summary>
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

    /// <summary>Switches between text and hex display modes and reloads the file.</summary>
    private void SwitchToMode(bool hex)
    {
        _hexMode = hex;
        _currentMode = hex ? ViewerMode.Hex : ViewerMode.Text;
        LoadFile();
    }

    /// <summary>Loads the current file in the appropriate mode (auto-detect, text, hex, or image).</summary>
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

    /// <summary>Returns <c>true</c> if the file extension is a known image format.</summary>
    private bool IsImageFile(string ext)
    {
        return ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".ico" or ".svg" or ".webp" or ".tiff";
    }

    /// <summary>Loads the image file into the picture box and resets zoom to 100%.</summary>
    private void LoadImage()
    {
        try
        {
            var image = Image.FromFile(_path);
            // Dispose the previously loaded image before replacing it - without this, paging
            // through a folder of photos with the ◀/▶ toolbar buttons accumulated one live
            // Bitmap/GDI handle per image for the lifetime of the viewer window.
            _pictureBox.Image?.Dispose();
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

    /// <summary>Loads the file as text with auto-detected encoding, respecting the 16 MB size limit.</summary>
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

    /// <summary>Loads the file in hex dump format (offset, hex bytes, ASCII), truncated at 1 MB.</summary>
    private void LoadHex()
    {
        var L = LocalizationService.Current;
        var sb = new StringBuilder();

        if (_fileSize > HexMaxBytes)
        {
            sb.AppendLine(L.GetString("View.HexTruncated", FormatSize(HexMaxBytes), FormatSize(_fileSize)));
            sb.AppendLine();
        }

        // Bounded read: only the first HexMaxBytes are ever displayed, so only read that much off
        // disk - File.ReadAllBytes here used to load an entire multi-GB ISO/video/dump into
        // memory (freezing the UI thread and risking OutOfMemoryException) just to show its first
        // megabyte.
        var bytes = ReadBoundedBytes(_path, HexMaxBytes);
        var limit = bytes.Length;

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

        if (_fileSize > HexMaxBytes)
            sb.AppendLine($"... ({FormatSize(_fileSize - HexMaxBytes)} more)");

        _textBox.Text = sb.ToString();
    }

    /// <summary>Reads at most <paramref name="maxBytes"/> bytes from the start of the file.</summary>
    private static byte[] ReadBoundedBytes(string path, int maxBytes)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var toRead = (int)Math.Min(fs.Length, maxBytes);
        var buffer = new byte[toRead];
        var read = 0;
        while (read < toRead)
        {
            var n = fs.Read(buffer, read, toRead - read);
            if (n == 0) break; // unexpected EOF - keep whatever was actually read
            read += n;
        }
        return read == toRead ? buffer : buffer[..read];
    }

    /// <summary>Updates the status bar labels with file name, size, mode, and extension.</summary>
    private void UpdateStatus(string mode)
    {
        var ext = FileSystem.FileEntry.GetExtension(_path).ToUpperInvariant().TrimStart('.');
        _lblFileInfo.Text = $"{Path.GetFileName(_path)} ({FormatSize(_fileSize)})";
        _lblMode.Text = mode;
        _lblPosition.Text = ext;
    }

    /// <summary>Formats a byte count into a human-readable string (e.g. "1.5 MB").</summary>
    private static string FormatSize(long bytes)
    {
        if (bytes < 0) return "—";
        if (bytes == 0) return "0 B";
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double s = bytes; int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return $"{s:0.##} {u[i]}";
    }

    /// <summary>Unsubscribes from theme events and disposes the image on disposal.</summary>
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
