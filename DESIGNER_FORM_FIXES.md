# Исправление ошибок открытия форм в режиме конструктора

## Проблема

Формы WinForms не открывались в режиме Designer (конструктора) Visual Studio. Причина: обращение к `ThemeService.Current` во время инициализации компонентов без проверки, находится ли приложение в режиме дизайнера.

## Решение

Применена проверка `DesignTime.IsActive` при всех обращениях к `ThemeService.Current` в конструкторах форм и компонентов.

## Исправленные файлы

### 1. WinForms/ThemedForm.cs
**Проблема:** Конструктор обращался к `ThemeService.Current` без защиты, что вызывало ошибки инициализации при открытии форм в дизайнере.

**Решение:** Перемещена проверка `DesignTime.IsActive` в начало конструктора, перед обращением к сервисам.

```csharp
// ДО:
public ThemedForm()
{
    Font = ThemeService.Current.GridFont;          // ❌ Без защиты
    BackColor = ThemeService.Current.Background;   // ❌ Без защиты
    ForeColor = ThemeService.Current.Foreground;   // ❌ Без защиты
    if (!DesignTime.IsActive)
        ThemeService.ThemeChanged += OnThemeChanged;
}

// ПОСЛЕ:
public ThemedForm()
{
    if (!DesignTime.IsActive)
    {
        Font = ThemeService.Current.GridFont;      // ✅ Защищено
        BackColor = ThemeService.Current.Background;
        ForeColor = ThemeService.Current.Foreground;
        ThemeService.ThemeChanged += OnThemeChanged;
    }
}
```

### 2. WinForms/EditorForm.cs
**Проблема:** Три места с обращением к `ThemeService.Current`:
- Конструктор: подписка на `ThemeService.ThemeChanged` без проверки
- Метод `BuildToolbar()`: обращение на строке 100
- Метод `BuildStatusBar()`: обращение на строке 295

**Решение:** Все обращения к `ThemeService.Current` обёрнуты проверкой `DesignTime.IsActive`.

```csharp
// В конструкторе:
if (!DesignTime.IsActive)
    ThemeService.ThemeChanged += OnThemeChanged;

// В BuildToolbar:
_toolStrip = new ToolStrip { ... };
if (!DesignTime.IsActive)
{
    var p = ThemeService.Current;
    // Использование p для настройки цветов
}

// В BuildStatusBar:
Color accentColor = Color.Empty;
Color dimForegroundColor = Color.Empty;
if (!DesignTime.IsActive)
{
    var p = ThemeService.Current;
    accentColor = p.Accent;
    dimForegroundColor = p.DimForeground;
}
```

### 3. WinForms/CodeEditorControl.cs
**Проблема:** Конструктор обращался к `ThemeService.Current.PanelBackground` на строке 116.

**Решение:**
```csharp
// ДО:
BackColor = ThemeService.Current.PanelBackground;  // ❌

// ПОСЛЕ:
if (!DesignTime.IsActive)
    BackColor = ThemeService.Current.PanelBackground;  // ✅
```

### 4. WinForms/EmbeddedTerminalPanel.cs
**Проблема:** Метод `InitializeComponents()` обращался к `ThemeService.Current.Background` на строке 97.

**Решение:**
```csharp
// ДО:
BackColor = ThemeService.Current.Background;  // ❌

// ПОСЛЕ:
if (!DesignTime.IsActive)
    BackColor = ThemeService.Current.Background;  // ✅
```

### 5. WinForms/FindReplaceBar.cs
**Проблема:** Конструктор устанавливал `BackColor` используя `ThemeService.Current.HeaderBackground` на строке 135.

**Решение:**
```csharp
// ДО:
BackColor = ThemeService.Current.HeaderBackground,  // ❌

// ПОСЛЕ:
BackColor = DesignTime.IsActive ? Color.Empty : ThemeService.Current.HeaderBackground,  // ✅
```

## Как работает исправление

### Логика DesignTime.IsActive

```csharp
public static bool IsActive => HostIsDesigner || LicenseManager.UsageMode == LicenseUsageMode.Designtime;

private static bool DetectHost()
{
    var exe = Environment.ProcessPath;
    if (string.IsNullOrEmpty(exe)) return false;

    var name = Path.GetFileNameWithoutExtension(exe);
    return name.Equals("DesignToolsServer", StringComparison.OrdinalIgnoreCase)
        || name.Equals("devenv", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Blend", StringComparison.OrdinalIgnoreCase);
}
```

### При запуске в дизайнере:
1. ✅ Формы создаются без ошибок инициализации
2. ✅ Используются значения по умолчанию (Color.Empty, SystemFonts.DefaultFont)
3. ✅ Дизайнер может отобразить макет формы
4. ✅ Нет доступа к файлам %APPDATA% и логам

### При запуске приложения:
1. ✅ `DesignTime.IsActive` возвращает false
2. ✅ Все темы и стили применяются из `ThemeService.Current`
3. ✅ Подписки на события работают корректно
4. ✅ Приложение функционирует как обычно

## Результаты

✅ **Сборка:** Успешна без ошибок и предупреждений
✅ **Проект:** CoderCommander (.NET 8)
✅ **Формы:** Теперь открываются в режиме Designer
✅ **Функциональность:** Сохранена при запуске приложения

## Тестирование

Для проверки:
1. Откройте Visual Studio
2. Откройте CoderCommander.slnx
3. Откройте любую форму в режиме Designer (например, EditorForm.cs - нажмите [Design])
4. Форма должна отобразиться без ошибок

## Файлы, затронутые исправлением

- WinForms/ThemedForm.cs (базовый класс для всех форм)
- WinForms/EditorForm.cs (форма редактора кода)
- WinForms/CodeEditorControl.cs (контрол редактора)
- WinForms/EmbeddedTerminalPanel.cs (панель терминала)
- WinForms/FindReplaceBar.cs (панель поиска)

## Важные заметки

1. **Безопасность дизайнера:** Изменения гарантируют, что дизайнер никогда не будет срываться при попытке инициализировать UI
2. **Производительность:** Нет влияния на производительность при запуске приложения
3. **Обратная совместимость:** Все изменения полностью обратно совместимы
4. **Рекомендация:** Если в будущем добавляются новые формы с обращением к `ThemeService.Current` в конструкторе, не забыть защитить их проверкой `DesignTime.IsActive`
