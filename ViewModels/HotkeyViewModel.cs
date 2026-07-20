using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.ViewModels;

/// <summary>
/// ViewModel вкладки «Горячие клавиши» в диалоге настроек.
/// ViewModel for the "Hotkeys" tab in the settings dialog.
/// </summary>
public class HotkeyViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Текущие привязки горячих клавиш.
    /// Current hotkey bindings.
    /// </summary>
    public ObservableCollection<HotkeyItem> Hotkeys { get; } = new();

    /// <summary>
    /// Режим захвата клавиши (ожидание нажатия).
    /// Key capture mode (waiting for a key press).
    /// </summary>
    private bool _isCapturing;
    public bool IsCapturing
    {
        get => _isCapturing;
        set { _isCapturing = value; OnPropertyChanged(); OnPropertyChanged(nameof(CapturingDisplay)); }
    }

    /// <summary>
    /// Действие, для которого идёт захват клавиши.
    /// The action currently being captured.
    /// </summary>
    private HotkeyItem? _capturingItem;
    public HotkeyItem? CapturingItem
    {
        get => _capturingItem;
        set { _capturingItem = value; OnPropertyChanged(); OnPropertyChanged(nameof(CapturingDisplay)); }
    }

    /// <summary>
    /// Текст отображения текущего захвата.
    /// Display text for the current capture state.
    /// </summary>
    public string CapturingDisplay =>
        IsCapturing && CapturingItem is not null
            ? string.Format(LocalizationService.Current.GetString("Hotkey.CapturingFor"), HotkeyActionRegistry.GetDisplayName(CapturingItem.Action))
            : "";

    /// <summary>
    /// Создаёт независимую копию элемента хоткея (чтобы правки в диалоге
    /// не трогали кэшированные настройки до нажатия «Сохранить»).
    /// Creates an independent copy of a hotkey item (so dialog edits
    /// don't touch the cached settings until "Save" is clicked).
    /// </summary>
    private static HotkeyItem Clone(HotkeyItem hk) => new()
    {
        Action = hk.Action,
        DisplayName = hk.DisplayName,
        Key = hk.Key,
        Modifiers = hk.Modifiers,
        Category = hk.Category,
        Description = hk.Description
    };

    /// <summary>
    /// Загружает хоткеи из настроек (с клонированием — отмена без сохранения не протекает в кэш).
    /// Loads hotkeys from settings (cloned — cancelling without save doesn't leak into the cache).
    /// </summary>
    public void LoadFrom(AppSettings s)
    {
        Hotkeys.Clear();
        var list = s.Hotkeys.Count > 0 ? s.Hotkeys : SettingsService.GetDefaultHotkeys();
        foreach (var hk in list)
            Hotkeys.Add(Clone(hk));
    }

    /// <summary>
    /// Сохраняет текущие хоткеи в настройки (с клонированием).
    /// Saves current hotkeys to settings (cloned).
    /// </summary>
    public void SaveTo(AppSettings s)
    {
        s.Hotkeys = Hotkeys.Select(Clone).ToList();
    }

    /// <summary>
    /// Сбрасывает хоткеи к умолчанию.
    /// Resets hotkeys to defaults.
    /// </summary>
    public void ResetToDefaults()
    {
        CapturingItem = null;
        IsCapturing = false;
        Hotkeys.Clear();
        foreach (var hk in SettingsService.GetDefaultHotkeys())
            Hotkeys.Add(hk);
        OnPropertyChanged(nameof(Hotkeys));
    }

    /// <summary>
    /// Начинает захват клавиши для указанного действия.
    /// Starts key capture for the specified hotkey item.
    /// </summary>
    public void StartCapture(HotkeyItem item)
    {
        CapturingItem = item;
        IsCapturing = true;
    }

    /// <summary>
    /// Обрабатывает нажатие клавиши в режиме захвата.
    /// Handles a key press during capture mode.
    /// </summary>
    public bool HandleKeyDown(Key key, ModifierKeys modifiers)
    {
        if (!IsCapturing || CapturingItem is null) return false;

        // Escape — отмена захвата
        // Escape — cancel capture
        if (key == Key.Escape)
        {
            IsCapturing = false;
            CapturingItem = null;
            return true;
        }

        // Чистые модификаторы не завершают захват — ждём основную клавишу.
        // Bare modifiers don't end capture — wait for the main key.
        if (key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
            return false;

        var newKey = key.ToString();
        var newMods = FormatModifiers(modifiers);

        // Конфликт: та же комбинация уже назначена другому действию — снимаем её.
        // Conflict: the same combo is bound to another action — unbind it there.
        foreach (var other in Hotkeys)
        {
            if (ReferenceEquals(other, CapturingItem)) continue;
            if (other.Key == newKey && other.Modifiers == newMods)
            {
                other.Key = "";
                other.Modifiers = "";
            }
        }

        CapturingItem.Key = newKey;
        CapturingItem.Modifiers = newMods;

        IsCapturing = false;
        CapturingItem = null;
        return true;
    }

    /// <summary>
    /// Форматирует модификаторы в строку.
    /// Formats modifier keys to a string.
    /// </summary>
    private static string FormatModifiers(ModifierKeys mods)
    {
        if (mods == ModifierKeys.None) return "";
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        return string.Join("+", parts);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
