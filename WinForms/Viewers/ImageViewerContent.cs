using System.Threading;
using CoderCommander.Services;
using CoderCommander.Viewers;
using CoderCommander.WinForms;

namespace CoderCommander.WinForms.Viewers;

/// <summary>
/// Image content: fit-to-window/actual-size/wheel-zoom, 90°-quarter rotation, and drag-pan -
/// ported verbatim from the pre-rewrite <c>ViewerForm</c>'s image state and handlers, just moved
/// into its own <see cref="IViewerContent"/> instead of living as fields directly on the form.
/// </summary>
internal sealed class ImageViewerContent : IViewerContent
{
    private readonly Panel _scrollPanel;
    private readonly PictureBox _pictureBox;
    private readonly AppSettings _settings;
    private readonly ToolStripItem[] _toolbarItems;

    private Image? _originalImage;
    private Image? _displayImage;
    private int _rotationQuarters;
    private float _zoom = 1.0f;
    private bool _fitToWindow;
    private bool _isPanning;
    private Point _panStart;

    public Control View => _scrollPanel;
    public IReadOnlyList<ToolStripItem> ToolbarItems => _toolbarItems;
    public IViewerSearchTarget? SearchTarget => null; // no meaningful text search over an image
    public string? StatusText { get; private set; }
    public event EventHandler? StatusChanged;

    public ImageViewerContent(ViewerContentContext ctx)
    {
        _settings = ctx.Settings;
        _fitToWindow = _settings.ViewerImageFitToWindow;
        var p = ThemeService.Current;

        _scrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = p.PanelBackground,
            Tag = ThemeRole.PanelBackground,
            Visible = false
        };
        // Re-fit whenever the panel is resized - otherwise "fit to window" would only ever
        // reflect the size at the moment the image was loaded.
        _scrollPanel.Resize += (_, _) => { if (_fitToWindow) ApplyFitOrZoom(); };
        _scrollPanel.MouseWheel += (_, e) => ChangeZoom(e.Delta > 0 ? 0.1f : -0.1f);

        _pictureBox = new PictureBox
        {
            BackColor = p.PanelBackground,
            SizeMode = PictureBoxSizeMode.Zoom,
            Cursor = Cursors.SizeAll
        };
        _pictureBox.MouseDown += OnImageMouseDown;
        _pictureBox.MouseMove += OnImageMouseMove;
        _pictureBox.MouseUp += OnImageMouseUp;
        _pictureBox.MouseWheel += (_, e) => ChangeZoom(e.Delta > 0 ? 0.1f : -0.1f);
        _scrollPanel.Controls.Add(_pictureBox);

        // CA2000's escape analysis can't trace disposal through "packed into a field array, then
        // added to the caller's ToolStrip.Items collection" - same class of false positive as the
        // one documented at MainForm.OpenDirectoryTree() for FormClosed-disposed dialogs. These six
        // buttons are disposed transitively by ViewerForm's _toolStrip once it (and therefore its
        // Items) is disposed - the same already-accepted ownership pattern CA2213 waives elsewhere
        // in this codebase for controls owned by a parent Controls/Items collection.
#pragma warning disable CA2000
        var zoomOutBtn = ViewerToolbarFactory.CreateIconButton("zoom_out", "View.ZoomOut", (_, _) => ChangeZoom(-0.1f));
        var zoomInBtn = ViewerToolbarFactory.CreateIconButton("zoom_in", "View.ZoomIn", (_, _) => ChangeZoom(0.1f));
        var zoomFitBtn = ViewerToolbarFactory.CreateIconButton("zoom_fit", "View.ZoomFit", (_, _) => SetFitToWindow(true));
        var zoomActualBtn = ViewerToolbarFactory.CreateIconButton("zoom_actual", "View.ZoomActual", (_, _) => SetActualSize());
        var rotateCcwBtn = ViewerToolbarFactory.CreateIconButton("rotate_ccw", "View.RotateCCW", (_, _) => RotateImage(false));
        var rotateCwBtn = ViewerToolbarFactory.CreateIconButton("rotate_cw", "View.RotateCW", (_, _) => RotateImage(true));
#pragma warning restore CA2000
        _toolbarItems = [zoomOutBtn, zoomInBtn, zoomFitBtn, zoomActualBtn, rotateCcwBtn, rotateCwBtn];
    }

    public Task RenderAsync(ViewerPayload payload, CancellationToken ct)
    {
        switch (payload)
        {
            case ImagePayload img:
                ApplyImage(img.Image);
                break;
            case ViewerErrorPayload err:
                ClearImage();
                // err.Modal distinguishes "the user asked for this file specifically" (worth an
                // explicit dialog) from "Prev/Next landed on a file this format can't show" (routine
                // navigation, not an error worth a popup) - holding Right through a folder of mixed
                // file types must not fire one StyledMessageBox per keystroke. The inline status
                // text still reports what happened either way.
                StatusText = err.Message;
                if (err.Modal)
                {
                    StyledMessageBox.Show(err.Message, LocalizationService.Current.GetString("View.Error"),
                        MsgBoxButtons.OK, MsgBoxIcon.Error);
                }
                break;
        }
        return Task.CompletedTask;
    }

    private void ApplyImage(Image image)
    {
        _originalImage?.Dispose();
        if (_displayImage != null && !ReferenceEquals(_displayImage, _originalImage))
            _displayImage.Dispose();

        _originalImage = image;
        _rotationQuarters = 0;
        _fitToWindow = _settings.ViewerImageFitToWindow;
        RebuildDisplayImage();
    }

    private void ClearImage()
    {
        _originalImage?.Dispose();
        if (_displayImage != null && !ReferenceEquals(_displayImage, _originalImage))
            _displayImage.Dispose();
        _originalImage = null;
        _displayImage = null;
        _pictureBox.Image = null;
    }

    /// <summary>Rebuilds <see cref="_displayImage"/> from <see cref="_originalImage"/> at the
    /// current rotation. At 0° it's just a reference to the original (no clone/dispose churn for
    /// the common case); any other rotation clones first - the original decoded image is never
    /// mutated.</summary>
    private void RebuildDisplayImage()
    {
        if (_originalImage == null) return;

        if (_displayImage != null && !ReferenceEquals(_displayImage, _originalImage))
            _displayImage.Dispose();

        if (_rotationQuarters == 0)
        {
            _displayImage = _originalImage;
        }
        else
        {
            var clone = (Image)_originalImage.Clone();
            clone.RotateFlip(_rotationQuarters switch
            {
                1 => RotateFlipType.Rotate90FlipNone,
                2 => RotateFlipType.Rotate180FlipNone,
                3 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone,
            });
            _displayImage = clone;
        }

        _pictureBox.Image = _displayImage;
        ApplyFitOrZoom();
    }

    /// <summary>Rotates a quarter turn. Purely a per-image display fix, not a session preference -
    /// resets to 0° whenever a new image is applied (<see cref="ApplyImage"/>), never persisted.</summary>
    private void RotateImage(bool clockwise)
    {
        if (_originalImage == null) return;
        _rotationQuarters = ((_rotationQuarters + (clockwise ? 1 : -1)) % 4 + 4) % 4;
        RebuildDisplayImage();
    }

    /// <summary>Fit-to-window math: scale so the whole image fits the viewport, clamped so a
    /// smaller-than-viewport image is never upscaled past 100%.</summary>
    private void ApplyFitOrZoom()
    {
        if (_displayImage == null) return;

        if (_fitToWindow)
        {
            var viewport = _scrollPanel.ClientSize;
            if (viewport.Width > 0 && viewport.Height > 0)
            {
                var zw = viewport.Width / (float)_displayImage.Width;
                var zh = viewport.Height / (float)_displayImage.Height;
                _zoom = Math.Min(1.0f, Math.Min(zw, zh));
            }
        }
        _zoom = Math.Clamp(_zoom, 0.1f, 5.0f);

        _pictureBox.Size = new Size(
            Math.Max(1, (int)(_displayImage.Width * _zoom)),
            Math.Max(1, (int)(_displayImage.Height * _zoom)));

        var L = LocalizationService.Current;
        StatusText = $"{L.GetString("View.ImageMode")} — {_displayImage.Width}x{_displayImage.Height}px, {(int)(_zoom * 100)}%";
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetFitToWindow(bool fit)
    {
        _fitToWindow = fit;
        _settings.ViewerImageFitToWindow = fit;
        SettingsService.Save(_settings);
        ApplyFitOrZoom();
    }

    private void SetActualSize()
    {
        _fitToWindow = false;
        _settings.ViewerImageFitToWindow = false;
        SettingsService.Save(_settings);
        _zoom = 1.0f;
        ApplyFitOrZoom();
    }

    /// <summary>Adjusts zoom by <paramref name="delta"/> (±10% per call), switching off
    /// fit-to-window - manual zoom always overrides it.</summary>
    private void ChangeZoom(float delta)
    {
        if (_displayImage == null) return;
        _fitToWindow = false;
        // Persisted like SetActualSize/SetFitToWindow already do for their own toggle - without
        // this, ViewerImageFitToWindow could only ever be written true (from the Fit button), so
        // the setting's documented "vs. last manual zoom" half never actually happened: every new
        // image snapped back to fit-to-window regardless of how the previous one was left.
        _settings.ViewerImageFitToWindow = false;
        SettingsService.Save(_settings);
        _zoom = Math.Clamp(_zoom + delta, 0.1f, 5.0f);
        ApplyFitOrZoom();
    }

    private void OnImageMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _isPanning = true;
        _panStart = e.Location;
        _pictureBox.Cursor = Cursors.Hand;
    }

    /// <summary>Note: <see cref="Panel.AutoScrollPosition"/>'s getter returns the NEGATIVE of the
    /// actual scroll offset while its setter expects a positive offset from the top-left - a
    /// well-known WinForms sign-flip gotcha.</summary>
    private void OnImageMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        var dx = e.X - _panStart.X;
        var dy = e.Y - _panStart.Y;
        var current = _scrollPanel.AutoScrollPosition;
        _scrollPanel.AutoScrollPosition = new Point(-current.X - dx, -current.Y - dy);
    }

    private void OnImageMouseUp(object? sender, MouseEventArgs e)
    {
        _isPanning = false;
        _pictureBox.Cursor = Cursors.SizeAll;
    }

    public void ApplyTheme()
    {
        var p = ThemeService.Current;
        _pictureBox.BackColor = p.PanelBackground;
        _scrollPanel.BackColor = p.PanelBackground;
    }

    // ── Disposal ─────────────────────────────────────────────────────────────────────────────
    // _scrollPanel/_pictureBox/toolbar buttons are owned transitively by ViewerForm's own
    // Controls/ToolStrip.Items collections (same accepted CA2213 pattern as TextViewerContent).
    // _originalImage/_displayImage are GDI+ resources, NOT owned by any WinForms collection, and
    // are the one thing this class must dispose itself.
    public void Dispose()
    {
        _originalImage?.Dispose();
        if (_displayImage != null && !ReferenceEquals(_displayImage, _originalImage))
            _displayImage.Dispose();
        GC.SuppressFinalize(this);
    }
}
