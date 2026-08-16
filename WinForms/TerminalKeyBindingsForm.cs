using System.Text;
using CoderCommander.Services;
using CoderCommander.Terminal.Input;

namespace CoderCommander.WinForms;

/// <summary>
/// Custom terminal key-binding editor - lists every rebindable <see cref="TerminalAction"/> with
/// its current chord; double-click a row to capture a new one. Action names are shown as
/// space-split identifiers (<c>"CopyOrInterrupt"</c> -&gt; <c>"Copy Or Interrupt"</c>) rather than
/// individually localized - this is a power-user editor over the same internal names
/// <c>AppSettings.TerminalCustomKeyBindings</c> persists, not user-facing prose elsewhere in the
/// app, so one readable rendering in every language is a reasonable trade against ~25 more
/// translation keys for a screen most users never open.
/// </summary>
public sealed class TerminalKeyBindingsForm : ThemedForm
{
    private readonly ListView _list;
    private readonly Dictionary<TerminalAction, Keys?> _working = new();
    private TerminalAction? _capturingAction;

    /// <summary>Result, in <c>AppSettings.TerminalCustomKeyBindings</c>'s format - only set after
    /// the dialog closes with <see cref="DialogResult.OK"/>.</summary>
    public Dictionary<string, string> ResultBindings { get; private set; } = new();

    public TerminalKeyBindingsForm(IReadOnlyDictionary<string, string> initialCustomBindings)
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        Text = L.GetString("Settings.Terminal.KeyBindings");
        ClientSize = new Size(440, 420);
        MaximizeBox = false;
        MinimizeBox = false;
        KeyPreview = true;

        SeedWorking(initialCustomBindings);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            Font = p.GridFont,
        };
        _list.Columns.Add(L.GetString("Settings.Terminal.KeyBindings.Action"), 220);
        _list.Columns.Add(L.GetString("Settings.Terminal.KeyBindings.Shortcut"), 160);
        _list.MouseDoubleClick += (_, e) =>
        {
            var item = _list.GetItemAt(e.X, e.Y);
            if (item?.Tag is TerminalAction action) BeginCapture(action);
        };
        RebuildRows();

        var hint = UiHelpers.CreateLabel(L.GetString("Settings.Terminal.KeyBindings.Hint"));
        hint.Dock = DockStyle.Top;
        hint.Height = 24;
        hint.ForeColor = p.DimForeground;
        // CreateLabel's AutoSize gets overridden by Dock=Top's width, and unlike
        // WinForms/PropertiesForm.cs's equivalent hint label (F121), AutoEllipsis was never set -
        // Russian's longer text was raw-clipped mid-word ("...для от") instead of degrading to
        // "..." (caught by visual inspection of a live build).
        hint.AutoEllipsis = true;

        var clearBtn = ThemedForm.CreateThemedButton(L.GetString("Settings.Terminal.KeyBindings.Clear"));
        clearBtn.Click += (_, _) =>
        {
            if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not TerminalAction action) return;
            _working[action] = null;
            RebuildRows();
        };

        var resetBtn = ThemedForm.CreateThemedButton(L.GetString("Settings.Terminal.KeyBindings.ResetAll"));
        resetBtn.Click += (_, _) =>
        {
            _working.Clear();
            SeedFromPreset(TerminalKeyBindings.WindowsTerminalPreset());
            RebuildRows();
        };

        var midPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 6, 0, 6)
        };
        midPanel.Controls.Add(clearBtn);
        midPanel.Controls.Add(resetBtn);

        var okBtn = ThemedForm.CreateThemedButton(L.GetString("Common.OK"), accent: true);
        okBtn.Click += (_, _) =>
        {
            ResultBindings = ToSettingsDictionary();
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancelBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Cancel"));
        cancelBtn.DialogResult = DialogResult.Cancel;

        var bottomPanel = CreateBottomPanel(okBtn, cancelBtn);

        // Dock=Fill first (WinForms docking order), then the Top/Bottom edge-docked siblings.
        Controls.Add(_list);
        Controls.Add(hint);
        Controls.Add(midPanel);
        Controls.Add(bottomPanel);

        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        KeyDown += OnFormKeyDown;
    }

    private void SeedWorking(IReadOnlyDictionary<string, string> initialCustomBindings)
    {
        foreach (var action in Enum.GetValues<TerminalAction>())
            if (action != TerminalAction.None)
                _working[action] = null;

        // A brand-new "Custom" table (nothing saved yet) starts from the WindowsTerminal preset,
        // so switching to Custom for the first time doesn't silently unbind everything.
        if (initialCustomBindings.Count == 0)
        {
            SeedFromPreset(TerminalKeyBindings.WindowsTerminalPreset());
            return;
        }

        foreach (var (actionName, chordText) in initialCustomBindings)
            if (Enum.TryParse<TerminalAction>(actionName, out var action) &&
                TerminalKeyBindings.TryParseChord(chordText, out var chord))
                _working[action] = chord;
    }

    private void SeedFromPreset(TerminalKeyBindings preset)
    {
        foreach (var (chord, action) in preset.Bindings)
            _working[action] = chord;
    }

    private Dictionary<string, string> ToSettingsDictionary()
    {
        var result = new Dictionary<string, string>();
        foreach (var (action, chord) in _working)
            if (chord is { } c)
                result[action.ToString()] = TerminalKeyBindings.FormatChord(c);
        return result;
    }

    private void RebuildRows()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var action in Enum.GetValues<TerminalAction>())
        {
            if (action == TerminalAction.None) continue;
            var chord = _working[action];
            var item = new ListViewItem(SplitPascalCase(action.ToString())) { Tag = action };
            item.SubItems.Add(chord is { } c ? TerminalKeyBindings.FormatChord(c) : "—");
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        ApplyTheme();
    }

    internal static string SplitPascalCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
    }

    private void BeginCapture(TerminalAction action)
    {
        _capturingAction = action;
        var idx = _list.Items.IndexOf(_list.Items.Cast<ListViewItem>().First(i => (TerminalAction)i.Tag! == action));
        _list.Items[idx].SubItems[1].Text = LocalizationService.Current.GetString("Settings.Terminal.KeyBindings.PressKeys");
        _list.Focus();
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (_capturingAction is not { } action) return;
        e.Handled = true;
        e.SuppressKeyPress = true;

        if (e.KeyCode == Keys.Escape)
        {
            _capturingAction = null;
            RebuildRows();
            return;
        }

        // A bare modifier key (Ctrl/Alt/Shift alone) isn't a usable chord - keep waiting.
        if (e.KeyCode is Keys.ControlKey or Keys.Menu or Keys.ShiftKey) return;

        var chord = e.KeyData;
        var conflict = _working.FirstOrDefault(kv => kv.Value == chord && kv.Key != action);
        if (conflict.Key != TerminalAction.None)
        {
            var L = LocalizationService.Current;
            var proceed = StyledMessageBox.Show(
                L.GetString("Settings.Terminal.KeyBindings.ConflictConfirm", TerminalKeyBindings.FormatChord(chord), SplitPascalCase(conflict.Key.ToString())),
                L.GetString("Common.Confirm"), MsgBoxButtons.YesNo, MsgBoxIcon.Warning, this) == MsgBoxResult.Yes;
            if (!proceed) return;
            _working[conflict.Key] = null;
        }

        _working[action] = chord;
        _capturingAction = null;
        RebuildRows();
    }

    /// <summary>Must be <c>override</c>, not a same-named private method: <see cref="ThemedForm"/>
    /// re-themes on a live theme switch through <c>RefreshTheme() -&gt; ApplyTheme()</c>, so a
    /// method that merely *hides* the base one is never reached by that path - it only ever ran
    /// from <see cref="RebuildRows"/>. Harmless while this body stayed a subset of what
    /// <c>ControlThemer</c>'s ListView case already does for us, but the declaration said
    /// "per-dialog theme hook" while behaving like a one-shot constructor helper.</summary>
    protected override void ApplyTheme()
    {
        base.ApplyTheme();
        var p = ThemeService.Current;
        _list.BackColor = p.PanelBackground;
        _list.ForeColor = p.Foreground;
        NativeControlThemer.ApplyDarkScrollbars(_list);
    }
}
