# AGENTS.md — CoderCommander

## Принципы
- **Язык общения** — русский
- **Минимум размышлений** — ответы только по существу, без воды
- **Скорость** — paralell tool calls, Task agent delegation
- **Карта связей** → точечные правки без чтения всего файла
- **Многопоточное делегирование** — рутина, задачи чтения → Task agents, кроме правок
- **Добиваться безупречной реализации задачи** — проверки, тесты, анализ, осмысливание логики, дополнение кода, чистый и рабочий код
- **Соблюдать стили** — единообразие стилей всех элементов, темы тёмная/светлая
- **Локализация** — обязательная проверка локализации после любых правок
- **Актуализация** — обновлять AGENTS.md после правок и успешных сборок
- **Git** — commit + push после каждого логически завершённого изменения

## Сборка
```powershell
dotnet build
```
Тестов и линтера нет. Успех = `dotnet build` без ошибок.

## Карта связей

```
Program.cs
 └─ MainViewModel (ViewModels/MainViewModel.cs)
     ├─ PanelViewModel (ViewModels/PanelViewModel.cs) x2
     │   └─ SortColumn / SortDescending / DirectoriesFirst
     ├─ IFileSystem (FileSystem/IFileSystem.cs)
     │   ├─ LocalFileSystem (FileSystem/LocalFileSystem.cs)
      │   └─ ZipArchiveFileSystem (FileSystem/ZipArchiveFileSystem.cs)
      │       └─ VfsPath (FileSystem/VfsPath.cs) / ArchivePath (FileSystem/ArchivePath.cs)
      │       └─ ReadDirectory нормализует "./" префикс в ZipEntryRecord.FullName
     ├─ Archives/ — формат-нейтральная абстракция архивов (ZIP, TAR, TAR.GZ, 7z, RAR, TAR.BZ2, TAR.XZ)
     │   ├─ IArchiveFormat / ArchiveFormatRegistry (Archives/ArchiveFormatRegistry.cs)
     │   ├─ IArchiveReader / IArchiveWriter
     │   ├─ RewritingArchiveWriter — add/delete для форматов без обновления на месте (TAR/TAR.GZ):
     │   │   стейджинг + пересборка при Commit, оригинал не трогается до атомарной замены
     │   ├─ NonDisposingStream — обёртка над потоком записи последовательного ридера; также
     │   │   дочитывает остаток при Dispose (обязательно для SharpCompress IReader)
      │   ├─ ArchiveFileSystem/ArchiveTree — обобщённый IFileSystem для просмотра архива как панели
      │   │   TrimmedName убирает "./" префикс (GNU tar, Info-ZIP и др.)
      │   ├─ Archives/Zip/{ZipArchiveFormat,ZipArchiveReader,ZipArchiveWriter}.cs — обёртки над ZipArchiveFileSystem
      │   ├─ Archives/Tar/{TarArchiveFormat,TarGzArchiveFormat,TarArchiveReader,TarSequentialWriter}.cs —
      │   │   на System.Formats.Tar, только последовательный доступ (без central directory);
      │   │   TarArchiveReader.ToRecord нормализует "./" префикс
      │   └─ Archives/SharpCompress/ — два файла с типами пакета SharpCompress:
      │       SharpCompressReader.cs (чтение: 7z, RAR, TAR.BZ2, TAR.XZ — нормализует "./") и
      │       SharpCompressTarWriter.cs (запись: только TAR.BZ2, через TarWriter+BZip2 —
      │       у SharpCompress 0.50.3 нет XZ-энкодера, поэтому TAR.XZ навсегда read-only)
     ├─ OperationManager (Operations/OperationManager.cs)
     │   ├─ CopyOperation / MoveOperation / DeleteOperation
     │   └─ PackOperation / UnpackOperation — работают через IArchiveWriter/IArchiveReader
     ├─ CommandEngine (Commands/CommandEngine.cs)
     │   └─ HotkeyManager (Commands/HotkeyManager.cs)
     └─ Services
         ├─ ThemeService (Services/ThemeService.cs) — 35+ цветов палитры
         ├─ SettingsService (Services/SettingsService.cs)
         │   └─ AppSettings: SortColumn, SortDescending, DirectoriesFirst,
         │      ShowExtensionInName, CopyAttributes, CopyTimestamps, ...
         ├─ LocalizationService (Services/LocalizationService.cs)
         └─ LogService (Services/LogService.cs)

 MainForm (Views/MainForm.cs)
 ├─ FilePanelUserControl (Views/FilePanelUserControl.cs) x2
 │   ├─ DoubleBuffered ListView, ColumnClick сортировка, SortChanged
 │   └─ DriveBarRenderer — ToolStrip с RoundedButton-стилем (gradient, radius)
 └─ EmbeddedTerminalPanel (WinForms/EmbeddedTerminalPanel.cs)
     └─ ThemedTabControl с вкладками CMD/PowerShell
         └─ Встроенные процессы в Panel с попыткой SetParent

Models/FileSystemItem.cs — обёртка FileEntry + выделение
 └─ Name, NameWithoutExtension, TypeDisplay, Extension, SizeDisplay, ...
```

## Ключевые принципы правок
| Файл | Что делает |
|---|---|
| `ViewModels/MainViewModel.cs` | Оркестратор, команды, события для UI |
 | `ViewModels/PanelViewModel.cs` | Навигация, листинг, сортировка (`SortColumn`/`SortDescending`/`DirectoriesFirst`), фильтры; ".." показывается на корне всех архивов через `ArchivePath.IsArchivePath(path)` |
| `Views/MainForm.cs` | Главное окно, связка VM ↔ UI, меню Вид→Сортировка; `SuggestArchiveBaseName` использует `FileEntry.GetExtension` для dot-prefixed папок (`.claude`, `.git`) |
| `Views/FilePanelUserControl.cs` | Контрол панели файлов, ColumnClick сортировка, индикаторы ▲/▼, DoubleBuffered, DriveBarRenderer |
| `WinForms/EmbeddedTerminalPanel.cs` | Встроенный терминал с вкладками CMD/PowerShell, процесс-сессии, F9 toggle |
| `Models/FileSystemItem.cs` | `NameWithoutExtension`, `TypeDisplay` (без точки), `DisplayName` (Flat View) |
| `Operations/OperationManager.cs` | Очередь операций |
| `FileSystem/*` | Файловая система (local + ZIP), `ArchivePath` — VFS-пути `archive.ext|inner/path` |
| `Archives/*` | Формат-нейтральный слой архивов: `IArchiveFormat`/`ArchiveFormatRegistry`, `Archives/Zip/`, `Archives/Tar/`, `Archives/SharpCompress/` — адаптеры по формату; все readers нормализуют `./` префикс имён записей |
| `Services/ThemeService.cs` | Тёмная/светлая тема (35+ цветов) |
| `Services/SettingsService.cs` | JSON-настройки (AppData), включая `ShowExtensionInName` |
| `Services/LocalizationService.cs` | Локализация (lang/*.lng) |

## XML-документация WinForms
- Все публичные/внутренние классы, конструкторы, методы, свойства и события в `WinForms/` имеют `/// <summary>` комментарии
- Теги: `/// <param name="..."/>`, `/// <see cref="..."/>`, `/// <c>...</c>`
- Файлы с полной документацией: `EmbeddedTerminalPanel.cs`, `CopyMoveDialogForm.cs`, `OverwriteDialogForm.cs`, `OperationDialogForm.cs`, `OperationQueueForm.cs`, `PackDialogForm.cs`, `InputDialogForm.cs`, `SettingsForm.cs`, `AboutForm.cs`, `PropertiesForm.cs`, `MultiRenameForm.cs`, `DirectoryTreeForm.cs`, `BookmarksForm.cs`

## Команды (CommandIds)
| Группа | Команды |
|---|---|
| Файл | View, Edit, Copy, Move, Rename, MakeDir, Delete, Wipe |
| Пакетные | MultiRename, EditNew, PackFiles, UnpackFiles, Checksum |
| Навигация | GoToParent, GoToRoot, GoToHome, Refresh, ChangeDir |
| Выделение | SelectAll, DeselectAll, InvertSelection, SelectGroup, DeselectGroup |
| Панель | SwapPanels, TargetEqualSource, ToggleHidden, ToggleFlatView |
| Вид | SetTheme, SetSortColumn, SetSortDescending, SetDirectoriesFirst, ToggleShowExtensionInName |
| Терминал | ToggleTerminal |
| Прочее | Exit, ShowProperties, About |

## Настройки (AppSettings)
| Свойство | Тип | По умолчанию | Описание |
|---|---|---|---|
| `Theme` | string | `"Dark"` | Тема оформления |
| `Language` | string | `"en"` | Язык интерфейса |
| `ShowHidden` | bool | `true` | Показывать скрытые файлы |
| `FlatView` | bool | `false` | Режим плоского просмотра |
| `SortColumn` | string | `"Name"` | Столбец сортировки (Name/Extension/Size/Modified) |
| `SortDescending` | bool | `false` | Направление сортировки |
| `DirectoriesFirst` | bool | `true` | Папки в начале |
| `ShowExtensionInName` | bool | `true` | Расширение в имени файла |
| `ConfirmDelete` | bool | `true` | Подтверждение удаления |
| `ConfirmOverwrite` | bool | `true` | Подтверждение перезаписи |
| `DefaultArchiveFormat` | string | `"zip"` | Формат по умолчанию в PackDialogForm (id из ArchiveFormatRegistry) |
| `ArchiveCompression` | `Dictionary<string,string>` | `{}` | Пресет сжатия (`CompressionPreset`) по id формата; нет записи → Balanced |
| `SkipCompressionForCompressedFiles` | bool | `true` | Не сжимать файлы с расширениями из `AlreadyCompressedExtensions` |
| `AlreadyCompressedExtensions` | `List<string>` | `[]` | Список расширений; пусто → встроенный список по умолчанию в PackOperation |

## Колонки ListView
| Индекс | Заголовок | Поле FileSystemItem | Сортировка |
|---|---|---|---|
| 0 | Имя | `Name` / `NameWithoutExtension` | `SortColumn = "Name"` |
| 1 | Тип | `TypeDisplay` (без точки) | `SortColumn = "Extension"` |
| 2 | Размер | `SizeDisplay` | `SortColumn = "Size"` |
| 3 | Изменён | `ModifiedDisplay` | `SortColumn = "Modified"` |
| 4 | Атрибуты | `AttributesDisplay` | `SortColumn = "Attributes"` |

## Локализация
- Дефолты в `LocalizationService.LoadDefaults()` (английские строки)
- Файлы: `lang/english.lng`, `lang/russian.lng`
- Ключи сортировки: `Menu.View.Sort`, `Menu.View.Sort.Name/Extension/Size/Modified/DirsFirst/Descending`
- Ключ расширения: `Menu.View.ShowExtInName`

## Todo-менеджмент
- Критические / блокирующие задачи — `priority: high`
- После каждого `dotnet build` → обновить AGENTS.md
- После коммита/пуша отметить `status: completed`
