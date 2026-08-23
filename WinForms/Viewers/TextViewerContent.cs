using System.Threading;
using CoderCommander.Services;
using CoderCommander.Viewers;
using CoderCommander.WinForms;

namespace CoderCommander.WinForms.Viewers;

/// <summary>
/// Content shared in shape (not instance) by every text-family universal format - Text, ASCII,
/// Binary and Hex all produce a <see cref="TextPayload"/> and render into a plain
/// <see cref="RichTextBox"/>, so each format's <c>CreateContent</c> just returns a fresh instance
/// of this same class. Doubles as the <see cref="IViewerSearchTarget"/> the shared find bar
/// searches - <see cref="View"/> being a bare <see cref="RichTextBox"/> makes that a thin,
/// direct implementation rather than needing a separate adapter class.
/// </summary>
internal sealed class TextViewerContent : IViewerContent, IViewerSearchTarget
{
    private readonly RichTextBox _textView;
    private RichTextBoxScrollbarOverlay? _scrollOverlay;
    private readonly ToolStripButton _findBtn;
    private readonly ToolStripButton _wordWrapBtn;
    private readonly ToolStripDropDownButton? _encodingBtn;
    private readonly List<(ToolStripMenuItem Item, string Value)> _encodingItems = new();
    private readonly AppSettings _settings;
    private readonly ViewerContentContext _ctx;
    private readonly ToolStripItem[] _toolbarItems;

    public Control View => _textView;
    public IReadOnlyList<ToolStripItem> ToolbarItems => _toolbarItems;
    public IViewerSearchTarget? SearchTarget => this;
    public string? StatusText { get; private set; }

    // Never changes outside RenderAsync (no zoom/rotate-style post-render mutation for text),
    // so this is an explicit no-op accessor rather than a field-like event - the latter would
    // trip CS0067 ("event is never used") since nothing here would ever invoke it.
    public event EventHandler? StatusChanged { add { } remove { } }

    /// <summary><paramref name="supportsEncoding"/> is true only for the "text" format's
    /// instance - ASCII/Binary/Hex share this same class but don't decode through an
    /// <see cref="System.Text.Encoding"/> at all, so an encoding picker on their toolbar would be
    /// dead UI.</summary>
    public TextViewerContent(ViewerContentContext ctx, bool supportsEncoding = false)
    {
        _ctx = ctx;
        _settings = ctx.Settings;
        var p = ThemeService.Current;
        var L = LocalizationService.Current;

        _textView = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = p.PanelBackground,
            ForeColor = p.Foreground,
            BorderStyle = BorderStyle.None,
            Font = p.MonoFont,
            WordWrap = _settings.ViewerWordWrap,
            DetectUrls = false,
            HideSelection = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            Visible = false
        };
        // SetWindowTheme's dark-scrollbar trick (NativeControlThemer.ApplyDarkScrollbars, used
        // for every other native-scrollbar control in the app) does not darken a RichEdit
        // control's scrollbar - confirmed live, regardless of when it's called. RichTextBoxScrollbarOverlay
        // covers the native bar with a themed sibling instead (same technique
        // ListViewScrollbarOverlay already uses for the file list's ListView) - deferred until the
        // handle exists and the control is actually parented, same as that overlay's own wiring.
        _textView.HandleCreated += (_, _) =>
        {
            if (_scrollOverlay == null && _textView.Parent != null)
                _scrollOverlay = new RichTextBoxScrollbarOverlay(_textView);
        };

        _findBtn = ViewerToolbarFactory.CreateToolButton("View.Search", "search", (_, _) => ctx.ShowFindBar());

        _wordWrapBtn = new ToolStripButton(L.GetString("View.WordWrap"))
        {
            CheckOnClick = true,
            Checked = _settings.ViewerWordWrap
        };
        _wordWrapBtn.Click += (_, _) =>
        {
            _settings.ViewerWordWrap = _wordWrapBtn.Checked;
            _textView.WordWrap = _wordWrapBtn.Checked;
            SettingsService.Save(_settings);
        };

        if (supportsEncoding)
        {
            _encodingBtn = new ToolStripDropDownButton(L.GetString("View.Encoding"));
            AddEncodingOption(L, "View.Encoding.Auto", "");
            foreach (var entry in EncodingCatalog.Entries)
                AddEncodingOption(L, entry.DisplayNameKey, entry.Id);
            RefreshEncodingChecks();

            _toolbarItems = [_findBtn, _wordWrapBtn, _encodingBtn];
        }
        else
        {
            _toolbarItems = [_findBtn, _wordWrapBtn];
        }
    }

    private void AddEncodingOption(LocalizationService L, string labelKey, string idValue)
    {
        var item = new ToolStripMenuItem(L.GetString(labelKey));
        item.Click += (_, _) =>
        {
            _settings.ViewerEncodingOverride = idValue;
            SettingsService.Save(_settings);
            RefreshEncodingChecks();
            _ctx.Reload();
        };
        _encodingBtn!.DropDownItems.Add(item);
        _encodingItems.Add((item, idValue));
    }

    private void RefreshEncodingChecks()
    {
        foreach (var (item, value) in _encodingItems)
            item.Checked = string.Equals(_settings.ViewerEncodingOverride, value, StringComparison.Ordinal);
    }

    public Task RenderAsync(ViewerPayload payload, CancellationToken ct)
    {
        switch (payload)
        {
            case TextPayload t:
                _textView.Text = t.Text;
                StatusText = t.StatusText;
                break;
            case ViewerErrorPayload e:
                _textView.Text = e.Message;
                StatusText = "";
                break;
        }
        return Task.CompletedTask;
    }

    public void ApplyTheme()
    {
        var p = ThemeService.Current;
        _textView.BackColor = p.PanelBackground;
        _textView.ForeColor = p.Foreground;
    }

    // ── IViewerSearchTarget ─────────────────────────────────────────────────────────────────
    public string GetSearchText() => _textView.Text;
    public int CurrentOffset => _textView.SelectionStart;

    public void SelectRange(int start, int length)
    {
        _textView.Select(start, length);
        _textView.ScrollToCaret();
    }

    public void FocusContent() => _textView.Focus();

    // ── Disposal ─────────────────────────────────────────────────────────────────────────────
    // _textView/_findBtn/_wordWrapBtn are added to ViewerForm's own Controls/ToolStrip.Items
    // collections at construction time and disposed transitively when the form closes - the
    // same already-accepted CA2213 ownership pattern documented in CoderCommander.csproj (a
    // control owned by a parent collection doesn't need a second, redundant Dispose() call here).
    // _scrollOverlay is the one thing here that DOES need an explicit Dispose (it owns a polling
    // Timer, not just Controls) - stopped before _textView itself goes away.
    public void Dispose()
    {
        _scrollOverlay?.Dispose();
        _textView.Dispose();
        _findBtn.Dispose();
        _wordWrapBtn.Dispose();
        _encodingBtn?.Dispose();
        GC.SuppressFinalize(this);
    }
}
