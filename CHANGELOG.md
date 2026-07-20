# Changelog

Все заметные изменения в CoderCommander.
All notable changes to CoderCommander.

---

## [1.0.0] — 2026-07-21

### Фундамент (ph0)
- **IFileSystem + LocalFileSystem** — абстракция файловой системы, совместимая с `System.IO`
- **Движок операций** — `CopyOperation`, `MoveOperation`, `DeleteOperation` с прогрессом и отменой
- **OverwritePolicy** — Skip / Overwrite / OverwriteOlder / OverwriteSmaller / AutoRename

### Основные фичи (ph1)
- **Быстрый фильтр/поиск** (ph1.1) — инкрементальная фильтрация в панели с debounce
- **Контрольные суммы** (ph1.2) — MD5/SHA1/SHA256/SHA512/CRC32, экспорт sfv/md5sum/sha1sum/sha256sum
- **Split/Combine файлов** (ph1.3) — разбиение на тома .001/.002 + склейка
- **Wipe (DoD 5220.22-M)** (ph1.4) — безопасное удаление проходами 0x00/0xFF/random
- **Свойства/статистика папки** (ph1.5) — рекурсивный подсчёт файлов/папок/размера

### Поиск и переименование (ph2)
- **Поиск по содержимому** (ph2.1) — regex + MemoryMappedFile для крупных файлов, CodePages
- **Результаты поиска в панель** (ph2.2) — виртуальный режим через `ISearchResultSource`
- **Мульти-переименование** (ph2.3) — токены `[N]/[E]/[C]/[G]/дата/время`, regex find/replace
- **Поиск дубликатов** (ph2.4) — по размеру/имени/хешу/содержимому, массовое удаление
- **Атрибуты/hardlink/symlink** (ph2.5) — R/H/S/A, даты, жёсткие и символические ссылки

### Сравнение и синхронизация (ph3)
- **Diff текстовых файлов** (ph3.1) — DiffPlex (Myers O(ND)), side-by-side + inline
- **Бинарный diff + hex-вьювер** (ph3.2) — побайтовое сравнение, hex+ASCII отображение
- **Синхронизация папок** (ph3.3) — size+mtime+content, 7 состояний, асимметричный режим
- **Умный watcher** (ph3.4) — FileSystemWatcher + debounce, авто-обновление панели

### Продвинутое (ph4)
- **Поиск в архивах** (ph4.1) — SharpCompress, ZIP/7Z/RAR/TAR/GZ/BZ2/XZ/LZ
- **Кросс-VFS** (ph4.2) — Local ↔ SFTP через SSH.NET
- **IContentProvider** (ph4.3) — расширяемость провайдеров содержимого

### UI/UX (ph5)
- **Управление архивами** (ph5.1) — создание/извлечение через SharpCompress
- **Очередь операций** (ph5.2) — макс. 3 параллельные операции
- **Закладки** (ph5.3) — CRUD + экспорт/импорт JSON
- **Настраиваемые колонки** (ph5.4) — drag-drop порядка, ширина, видимость
- **«Открыть как» + QuickView** (ph5.5) — ассоциации файлов, быстрый просмотр
- **Дерево каталогов** (ph5.6) — ленивая загрузка, Alt+F1/F2
- **Прогресс операций** (ph5.7) — ProgressBar, ETA, конфликт перезаписи
- **Экспорт/импорт закладок** (ph5.8) — JSON, дедупликация
- **Вкладки панелей** (ph5.9) — TabControl, средняя кнопка мыши = закрыть

### Безопасность и расширяемость (ph6)
- **Горячие клавиши** (ph6.1) — кастомизация 17 действий через настройки
- **Резервное копирование** (ph6.2) — авто-бэкап настроек, SHA-256 дедупликация
- **Drag & Drop** (ph6.3) — перетаскивание между панелями (Copy/Move)
- **Улучшения терминала** (ph6.4) — PowerShell Core (pwsh), Set-Location, средняя кнопка мыши
- **Настройки панели** (ph6.5) — шрифт/размер списка файлов

### Новые фичи (post-ph6)
- **Compare Directories+** — асимметричный режим, двойной клик = DiffWindow
- **Multi-Rename+** — подстроки `[Nx]/[Ex]`, стили имён, regex groups, лог, bad chars
- **Internal Image Viewer** — зум, поворот, слайдшоу, навигация ←/→
- **Flat View** — рекурсивный список файлов из всех подпапок в панели
- **Unit-тесты** — 93 теста (xUnit), 100% pass

### Технологии
- .NET 8, WPF, CommunityToolkit.Mvvm 8.4.0
- AvalonEdit 6.3.1, DiffPlex 1.7.2, SharpCompress 0.50.0, SSH.NET 2024.2.0
- System.IO.Hashing 8.0.0, System.Text.Encoding.CodePages 8.0.0
