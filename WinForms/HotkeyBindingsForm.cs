using CoderCommander.Commands;
using CoderCommander.Services;
using CoderCommander.Terminal.Input;

namespace CoderCommander.WinForms;

/// <summary>
/// App-hotkey editor - lists every default <see cref="HotkeyDef"/> (~30, from
/// <see cref="HotkeyManager.RegisterDefaults"/>) with its current shortcut; double-click a row to
/// capture a new one. Deliberately mirrors <see cref="TerminalKeyBindingsForm"/>'s shape (same
/// capture/conflict-detection/Clear/ResetAll flow) - two editors for the same underlying idea
/// (chord → action) should behave identically, not grow independent quirks.
/// <para>
/// Unlike the terminal editor's full-replacement "Custom" preset, this one is a partial-override
/// table (<see cref="AppSettings.CustomHotkeys"/>): only rows the user actually changed are
/// persisted, keyed by <see cref="HotkeyDef.Id"/> (stable across a rebind, unlike the shortcut
/// itself) rather than by shortcut. A command with two built-in shortcuts (e.g. GoToParent's
/// Backspace and Ctrl+Backspace) shows as two separate rows, each independently rebindable.
/// </para>
/// </summary>
public sealed partial class HotkeyBindingsForm : ThemedForm
{
    private readonly Dictionary<string, Keys?> _working = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Keys> _defaults = new(StringComparer.Ordinal);
    private readonly List<HotkeyDef> _rows;
    private string? _capturingId;

    /// <summary>Result, in <c>AppSettings.CustomHotkeys</c>'s format - only set after the dialog
    /// closes with <see cref="DialogResult.OK"/>. Only contains ids whose working chord differs
    /// from its built-in default (unchanged rows are omitted, not written as a no-op override).</summary>
    public Dictionary<string, string> ResultBindings { get; private set; } = new();

    public HotkeyBindingsForm(IReadOnlyDictionary<string, string> initialCustomBindings)
    {
        ArgumentNullException.ThrowIfNull(initialCustomBindings);

        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        var L = LocalizationService.Current;
        // A ColumnHeader is not a Control and cannot carry a LocalizationKey.
        _colAction.Text = L.GetString("Settings.Hotkeys.Action");
        _colShortcut.Text = L.GetString("Settings.Hotkeys.Shortcut");

        // A throwaway HotkeyManager just to enumerate the canonical default table - RegisterDefaults
        // doesn't touch the CommandEngine it's given, only HandleKey (dispatch) does, and dispatch
        // is never exercised here.
        var defaultsManager = new HotkeyManager(new CommandEngine());
        defaultsManager.RegisterDefaults();
        _rows = defaultsManager.Hotkeys.ToList();
        foreach (var def in _rows)
        {
            _defaults[def.Id] = def.Shortcut;
            _working[def.Id] = def.Shortcut;
        }
        foreach (var (id, chordText) in initialCustomBindings)
        {
            if (!_defaults.ContainsKey(id)) continue; // stale id from an older version - ignore
            _working[id] = chordText.Length == 0 ? null : (TerminalKeyBindings.TryParseChord(chordText, out var c) ? c : _defaults[id]);
        }
        RebuildRows();

        _list.MouseDoubleClick += (_, e) =>
        {
            var item = _list.GetItemAt(e.X, e.Y);
            if (item?.Tag is string id) BeginCapture(id);
        };

        _clearBtn.Click += (_, _) =>
        {
            if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not string id) return;
            _working[id] = null;
            RebuildRows();
        };

        _resetBtn.Click += (_, _) =>
        {
            foreach (var (id, shortcut) in _defaults)
                _working[id] = shortcut;
            RebuildRows();
        };

        _okBtn.Click += (_, _) =>
        {
            ResultBindings = ToSettingsDictionary();
            DialogResult = DialogResult.OK;
            Close();
        };

        KeyDown += OnFormKeyDown;
    }

    private Dictionary<string, string> ToSettingsDictionary()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, chord) in _working)
        {
            var defaultChord = _defaults[id];
            if (chord is { } c)
            {
                if (c != defaultChord)
                    result[id] = TerminalKeyBindings.FormatChord(c);
            }
            else
            {
                result[id] = ""; // explicitly unbound - must be persisted, not omitted
            }
        }
        return result;
    }

    private void RebuildRows()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var def in _rows)
        {
            var chord = _working[def.Id];
            var item = new ListViewItem(FormatCommandLabel(def.CommandId, def.Param)) { Tag = def.Id };
            item.SubItems.Add(chord is { } c ? TerminalKeyBindings.FormatChord(c) : "—");
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        ApplyTheme();
    }

    /// <summary>"cm_GoToParent" + null -&gt; "Go To Parent"; "cm_SetTheme" + "Dark" -&gt;
    /// "Set Theme (Dark)". Same "one readable rendering across languages, not individually
    /// localized" trade-off as <see cref="TerminalKeyBindingsForm.SplitPascalCase"/> - this is a
    /// power-user editor over the same internal <c>CommandIds</c> names the app already uses
    /// everywhere else, not user-facing prose.</summary>
    private static string FormatCommandLabel(string commandId, string? param)
    {
        var name = commandId.StartsWith("cm_", StringComparison.Ordinal) ? commandId[3..] : commandId;
        var label = TerminalKeyBindingsForm.SplitPascalCase(name);
        return string.IsNullOrEmpty(param) ? label : $"{label} ({param})";
    }

    private void BeginCapture(string id)
    {
        _capturingId = id;
        var item = _list.Items.Cast<ListViewItem>().FirstOrDefault(i => (string)i.Tag! == id);
        if (item == null) return;
        item.SubItems[1].Text = LocalizationService.Current.GetString("Settings.Hotkeys.PressKeys");
        _list.Focus();
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (_capturingId is not { } id) return;
        e.Handled = true;
        e.SuppressKeyPress = true;

        if (e.KeyCode == Keys.Escape)
        {
            _capturingId = null;
            RebuildRows();
            return;
        }

        // A bare modifier key (Ctrl/Alt/Shift alone) isn't a usable chord - keep waiting.
        if (e.KeyCode is Keys.ControlKey or Keys.Menu or Keys.ShiftKey) return;

        var chord = e.KeyData;
        var conflictId = _working.FirstOrDefault(kv => kv.Value == chord && kv.Key != id).Key;
        if (conflictId != null)
        {
            var conflictDef = _rows.First(r => r.Id == conflictId);
            var L = LocalizationService.Current;
            var proceed = StyledMessageBox.Show(
                L.GetString("Settings.Hotkeys.ConflictConfirm", TerminalKeyBindings.FormatChord(chord), FormatCommandLabel(conflictDef.CommandId, conflictDef.Param)),
                L.GetString("Common.Confirm"), MsgBoxButtons.YesNo, MsgBoxIcon.Warning, this) == MsgBoxResult.Yes;
            if (!proceed) return;
            _working[conflictId] = null;
        }

        _working[id] = chord;
        _capturingId = null;
        RebuildRows();
    }

    /// <summary>Must be <c>override</c>, not a same-named private method - see
    /// <see cref="TerminalKeyBindingsForm"/>'s identical doc comment for why.</summary>
    protected override void ApplyTheme()
    {
        base.ApplyTheme();
        var p = ThemeService.Current;
        _list.BackColor = p.PanelBackground;
        _list.ForeColor = p.Foreground;
        NativeControlThemer.ApplyDarkScrollbars(_list);
    }

}
