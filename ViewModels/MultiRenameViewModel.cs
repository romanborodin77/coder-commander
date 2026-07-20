using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Services;

namespace CoderCommander.ViewModels;

/// <summary>
/// ViewModel окна мульти-переименования (ph2.3, exp.yml).
/// ViewModel for the multi-rename window (ph2.3).
/// Поддерживает токенизатор масок, режим «найти/заменить» (regex), live-preview,
/// переменные [V:name], пресеты (JSON) и применение через движок <see cref="MultiRenameEngine"/>.
/// Supports mask tokenizer, find/replace (regex) mode, live-preview, [V:name] variables,
/// JSON presets and applying via <see cref="MultiRenameEngine"/>.
/// </summary>
public partial class MultiRenameViewModel : ObservableObject
{
    /// <summary>Строка предпросмотра: исходное → новое имя. / Preview row: original → new name.</summary>
    public sealed record RenamePreviewItem(string OriginalName, string NewName, bool Changed);

    /// <summary>Переменная маски [V:name]. / Mask variable [V:name].</summary>
    public sealed partial class VariableItem : ObservableObject
    {
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private string _value = "";
    }

    /// <summary>Сериализуемый пресет маски. / Serializable mask preset.</summary>
    private sealed record Preset(
        string Mask, bool UseRegex, string Find, string Replace,
        bool CaseSensitive, int CounterStart, int CounterStep, int CounterWidth,
        int NameStyle, string BadCharReplacement, bool EnableLogging, string LogPath);

    private readonly IReadOnlyList<MultiRenameEngine.SourceFile> _files;
    private readonly DispatcherTimer _debounce;

    /// <summary>Строки предпросмотра. / Preview rows.</summary>
    public ObservableCollection<RenamePreviewItem> Items { get; } = new();

    /// <summary>Переменные маски [V:name]. / Mask variables [V:name].</summary>
    public ObservableCollection<VariableItem> Variables { get; } = new();

    /// <summary>Имена сохранённых пресетов. / Saved preset names.</summary>
    public ObservableCollection<string> PresetNames { get; } = new();

    /// <summary>Список стилей регистра для ComboBox. / Name style list for ComboBox.</summary>
    public IReadOnlyList<string> NameStyles { get; } = new[]
    {
        L10n("MultiRename.StyleAsIs"),
        L10n("MultiRename.StyleUpper"),
        L10n("MultiRename.StyleLower"),
        L10n("MultiRename.StyleTitle"),
    };

    [ObservableProperty] private string _mask = "[N] ([C]).[E]";
    [ObservableProperty] private bool _useRegexMode;
    [ObservableProperty] private string _findPattern = "";
    [ObservableProperty] private string _replacePattern = "";
    [ObservableProperty] private bool _caseSensitive;
    [ObservableProperty] private int _counterStart = 1;
    [ObservableProperty] private int _counterStep = 1;
    [ObservableProperty] private int _counterWidth;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private int _changedCount;
    [ObservableProperty] private bool _hasVariables;
    [ObservableProperty] private string _selectedPreset = "";
    [ObservableProperty] private int _selectedNameStyle;
    [ObservableProperty] private string _badCharReplacement = "_";
    [ObservableProperty] private bool _enableLogging;
    [ObservableProperty] private string _logPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoderCommander", "rename_log.txt");

    /// <summary>Успешно применено (окно может закрыться). / Applied successfully (window may close).</summary>
    [ObservableProperty] private bool _applied;

    /// <summary>Вызывается после успешного применения, чтобы закрыть окно. / Raised after a successful apply to close the window.</summary>
    public event Action? ApplyCompleted;

    public MultiRenameViewModel(IReadOnlyList<MultiRenameEngine.SourceFile> files)
    {
        _files = files;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Rebuild(); };

        RefreshPresetNames();
        SyncVariables();
        Rebuild();
    }

    // ── Реакция на изменения параметров: пересчёт с debounce ──────────────────

    partial void OnMaskChanged(string value) { SyncVariables(); ScheduleRebuild(); }
    partial void OnUseRegexModeChanged(bool value) => ScheduleRebuild();
    partial void OnFindPatternChanged(string value) => ScheduleRebuild();
    partial void OnReplacePatternChanged(string value) => ScheduleRebuild();
    partial void OnCaseSensitiveChanged(bool value) => ScheduleRebuild();
    partial void OnCounterStartChanged(int value) => ScheduleRebuild();
    partial void OnCounterStepChanged(int value) => ScheduleRebuild();
    partial void OnCounterWidthChanged(int value) => ScheduleRebuild();
    partial void OnSelectedNameStyleChanged(int value) => ScheduleRebuild();
    partial void OnBadCharReplacementChanged(string value) => ScheduleRebuild();

    private void ScheduleRebuild()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    // ── Переменные маски ───────────────────────────────────────────────────────

    /// <summary>Синхронизирует список переменных с маской (сохраняет введённые значения). / Syncs variables with the mask (preserves entered values).</summary>
    private void SyncVariables()
    {
        var required = MultiRenameEngine.ExtractVariables(Mask);
        // удаляем отсутствующие / remove gone
        for (int i = Variables.Count - 1; i >= 0; i--)
            if (!required.Contains(Variables[i].Name, StringComparer.OrdinalIgnoreCase))
                Variables.RemoveAt(i);
        // добавляем новые / add new
        foreach (var name in required)
            if (!Variables.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
                Variables.Add(new VariableItem { Name = name, Value = "" });

        HasVariables = Variables.Count > 0;
    }

    private IReadOnlyDictionary<string, string> VariableDict()
        => Variables.ToDictionary(v => v.Name, v => v.Value ?? "", StringComparer.OrdinalIgnoreCase);

    // ── Пересчёт предпросмотра ─────────────────────────────────────────────────

    private MultiRenameEngine.RenameOptions CurrentOptions() => new()
    {
        Mask = Mask,
        UseRegex = UseRegexMode,
        Find = FindPattern,
        Replace = ReplacePattern,
        CaseSensitive = CaseSensitive,
        CounterStart = CounterStart,
        CounterStep = CounterStep,
        CounterWidth = CounterWidth,
        Style = (CoderCommander.Services.NameStyle)SelectedNameStyle,
        BadCharReplacement = string.IsNullOrEmpty(BadCharReplacement) ? "_" : BadCharReplacement,
        EnableLogging = EnableLogging,
        LogPath = LogPath,
    };

    private void Rebuild()
    {
        var opts = CurrentOptions();
        var plan = MultiRenameEngine.BuildPlan(_files, opts, VariableDict());
        Items.Clear();
        int changed = 0;
        foreach (var p in plan)
        {
            if (p.Changed) changed++;
            Items.Add(new RenamePreviewItem(p.OriginalName, p.NewName, p.Changed));
        }
        ChangedCount = changed;
        Status = string.Format(L10n("MultiRename.PreviewStatus"), changed, _files.Count);
    }

    // ── Команды ────────────────────────────────────────────────────────────────

    /// <summary>Применяет переименование к выделенным файлам. / Applies the rename to the selected files.</summary>
    [RelayCommand]
    private void Apply()
    {
        var opts = CurrentOptions();
        var plan = MultiRenameEngine.BuildPlan(_files, opts, VariableDict());
        var (result, details) = MultiRenameEngine.Apply(plan);

        // Логирование (опционально). / Optional logging.
        if (opts.EnableLogging)
            MultiRenameEngine.WriteLog(opts.LogPath, details, result);

        if (result.Failed == 0)
            Status = string.Format(L10n("MultiRename.Done"), result.Renamed);
        else
            Status = string.Format(L10n("MultiRename.DoneErrors"), result.Renamed, result.Failed);

        if (result.Failed == 0) { Applied = true; ApplyCompleted?.Invoke(); }
    }

    /// <summary>Сохраняет текущие настройки маски как пресет. / Saves current mask settings as a preset.</summary>
    [RelayCommand]
    private void SavePreset()
    {
        var name = PromptInput(L10n("MultiRename.PresetSaveTitle"), L10n("MultiRename.PresetSavePrompt"), SelectedPreset);
        if (string.IsNullOrWhiteSpace(name)) return;

        var presets = LoadPresets();
        presets[name] = new Preset(Mask, UseRegexMode, FindPattern, ReplacePattern,
            CaseSensitive, CounterStart, CounterStep, CounterWidth,
            SelectedNameStyle, BadCharReplacement, EnableLogging, LogPath);
        SavePresets(presets);
        RefreshPresetNames();
        SelectedPreset = name;
        Status = string.Format(L10n("MultiRename.PresetSaved"), name);
    }

    /// <summary>Загружает выбранный пресет в параметры. / Loads the selected preset into the options.</summary>
    [RelayCommand]
    private void LoadPreset()
    {
        if (string.IsNullOrWhiteSpace(SelectedPreset)) return;
        var presets = LoadPresets();
        if (!presets.TryGetValue(SelectedPreset, out var p)) return;

        Mask = p.Mask;
        UseRegexMode = p.UseRegex;
        FindPattern = p.Find;
        ReplacePattern = p.Replace;
        CaseSensitive = p.CaseSensitive;
        CounterStart = p.CounterStart;
        CounterStep = p.CounterStep;
        CounterWidth = p.CounterWidth;
        SelectedNameStyle = p.NameStyle;
        BadCharReplacement = string.IsNullOrEmpty(p.BadCharReplacement) ? "_" : p.BadCharReplacement;
        EnableLogging = p.EnableLogging;
        LogPath = string.IsNullOrEmpty(p.LogPath) ? CurrentOptions().LogPath : p.LogPath;
        SyncVariables();
        Rebuild();
        Status = string.Format(L10n("MultiRename.PresetLoaded"), SelectedPreset);
    }

    /// <summary>Удаляет выбранный пресет. / Deletes the selected preset.</summary>
    [RelayCommand]
    private void DeletePreset()
    {
        if (string.IsNullOrWhiteSpace(SelectedPreset)) return;
        var presets = LoadPresets();
        if (presets.Remove(SelectedPreset))
        {
            SavePresets(presets);
            RefreshPresetNames();
            Status = string.Format(L10n("MultiRename.PresetDeleted"), SelectedPreset);
            SelectedPreset = "";
        }
    }

    // ── Пресеты (JSON рядом с настройками) ─────────────────────────────────────

    private static string PresetsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoderCommander", "multirename_presets.json");

    private Dictionary<string, Preset> LoadPresets()
    {
        try
        {
            if (!File.Exists(PresetsPath)) return new Dictionary<string, Preset>();
            var json = File.ReadAllText(PresetsPath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, Preset>>(json);
            return dict ?? new Dictionary<string, Preset>();
        }
        catch { return new Dictionary<string, Preset>(); }
    }

    private void SavePresets(Dictionary<string, Preset> presets)
    {
        try
        {
            var dir = Path.GetDirectoryName(PresetsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(PresetsPath, JsonSerializer.Serialize(presets,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Status = string.Format(L10n("MultiRename.PresetError"), ex.Message);
        }
    }

    private void RefreshPresetNames()
    {
        PresetNames.Clear();
        foreach (var name in LoadPresets().Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            PresetNames.Add(name);
    }

    // ── Утилиты ─────────────────────────────────────────────────────────────────

    private static string L10n(string key) => LocalizationService.Current.GetString(key);

    /// <summary>Минимальный диалог ввода строки. / Minimal single-line input dialog.</summary>
    private static string? PromptInput(string title, string prompt, string def = "")
    {
        var w = new Window
        {
            Title = title,
            Width = 420,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = Application.Current.MainWindow,
        };
        var sp = new System.Windows.Controls.StackPanel { Margin = new Thickness(15) };
        sp.Children.Add(new System.Windows.Controls.TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
        var tb = new System.Windows.Controls.TextBox { Text = def };
        sp.Children.Add(tb);
        var btns = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = new System.Windows.Controls.Button { Content = L10n("Dialog.Ok"), Width = 75, IsDefault = true };
        var cancel = new System.Windows.Controls.Button { Content = L10n("Dialog.Cancel"), Width = 75, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        btns.Children.Add(ok);
        btns.Children.Add(cancel);
        sp.Children.Add(btns);
        w.Content = sp;

        string? result = null;
        ok.Click += (_, _) => { result = tb.Text; w.DialogResult = true; };
        cancel.Click += (_, _) => { w.DialogResult = false; };
        return w.ShowDialog() == true ? result : null;
    }
}
