using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;

namespace CoderCommander.Services;

/// <summary>
/// Сервис локализации приложения. Встроенный язык — английский, дополнительные загружаются из .lng файлов.
/// Application localization service. Built-in language is English; additional languages are loaded from .lng files.
/// Формат .lng файла: ключ=перевод (строка '# — комментарий, пустые строки игнорируются).
/// .lng file format: key=translation (# — comment, empty lines are ignored).
/// Реализация через ResourceDictionary: строки доступны через {DynamicResource Key} в XAML.
/// Implementation via ResourceDictionary: strings are accessible via {DynamicResource Key} in XAML.
/// </summary>
public sealed class LocalizationService
{
    /// <summary>Текущий экземпляр синглтона.</summary>
    public static LocalizationService Current { get; } = new();

    /// <summary>Код языка по умолчанию (встроенный).</summary>
    public const string DefaultLanguage = "en";

    /// <summary>Словарь строк: ключ → значение для текущего языка.</summary>
    private readonly Dictionary<string, string> _strings = new();

    /// <summary>Ссылка на ResourceDictionary текущего языка в App.Resources.</summary>
    private ResourceDictionary? _langDict;

    /// <summary>Текущий код языка.</summary>
    public string CurrentLanguage { get; private set; } = DefaultLanguage;

    /// <summary>Событие, вызываемое при смене языка.</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>
    /// Загружает язык и применяет строки через DynamicResource.
    /// Loads a language and applies strings via DynamicResource.
    /// </summary>
    /// <param name="languageCode">Код языка: "en" для встроенного английского, имя файла без расширения для дополнительных.</param>
    public void LoadLanguage(string languageCode)
    {
        _strings.Clear();

        // 1. Всегда загружаем английский как базу (встроенный)
        LoadEnglish();

        // 2. Если язык не английский — загружаем перевод поверх
        if (languageCode != DefaultLanguage)
            LoadFromFile(languageCode);

        CurrentLanguage = languageCode;

        // 3. Применяем строки через ResourceDictionary
        ApplyToResources();

        // 4. Уведомляем подписчиков
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Применяет загруженные строки в ResourceDictionary приложения.
    /// Applies loaded strings to the application ResourceDictionary.
    /// </summary>
    private void ApplyToResources()
    {
        var app = Application.Current;
        if (app is null) return;

        // Удаляем старый словарь, если был
        if (_langDict is not null && app.Resources.MergedDictionaries.Contains(_langDict))
            app.Resources.MergedDictionaries.Remove(_langDict);

        // Создаём новый словарь со всеми строками
        _langDict = new ResourceDictionary();
        foreach (var kvp in _strings)
            _langDict[kvp.Key] = kvp.Value;

        // Добавляем как первый словарь, чтобы перекрывал стандартные
        app.Resources.MergedDictionaries.Insert(0, _langDict);
    }

    /// <summary>
    /// Возвращает строку перевода по ключу. Если ключ не найден — возвращает сам ключ.
    /// Returns the translated string by key. Returns the key itself if not found.
    /// </summary>
    public string GetString(string key)
    {
        return _strings.TryGetValue(key, out var val) ? val : key;
    }

    /// <summary>
    /// Загружает встроенные английские строки (по умолчанию).
    /// Loads built-in English strings (default).
    /// </summary>
    private void LoadEnglish()
    {
        // ═══════════════════════════════════════════
        // MAIN WINDOW / TITLE
        // ═══════════════════════════════════════════
        _strings["AppTitle"] = "Coder Commander";
        _strings["AppSubtitle"] = "File Manager";

        _strings["About.Title"] = "About";
        _strings["About.Close"] = "Close";
        _strings["About.Subtitle"] = "Two-panel file manager for developers";

        _strings["Diff.Title"] = "File Comparison";

        // ═══════════════════════════════════════════
        // MENU
        // ═══════════════════════════════════════════
        _strings["Menu.Left"] = "Left";
        _strings["Menu.Left.Refresh"] = "Refresh panel";
        _strings["Menu.Left.ChangeDrive"] = "Change drive...";

        _strings["Menu.File"] = "File";
        _strings["Menu.File.View"] = "View";
        _strings["Menu.File.Edit"] = "Edit";
        _strings["Menu.File.Copy"] = "Copy";
        _strings["Menu.File.Move"] = "Move/Rename";
        _strings["Menu.File.CreateFolder"] = "Create folder";
        _strings["Menu.File.Delete"] = "Delete";
        _strings["Menu.File.Rename"] = "Rename";
        _strings["Menu.File.Exit"] = "Exit";

        _strings["Menu.Selection"] = "Selection";
        _strings["Menu.Selection.All"] = "Select all";
        _strings["Menu.Selection.None"] = "Deselect all";
        _strings["Menu.Selection.Invert"] = "Invert";
        _strings["Menu.Selection.Group"] = "Select group...";
        _strings["Menu.Selection.DeselectGroup"] = "Deselect group...";

        _strings["Menu.Commands"] = "Commands";
        _strings["Menu.Commands.Search"] = "Search files";
        _strings["Menu.Commands.Compare"] = "Compare directories";
        _strings["Menu.Commands.DiskInfo"] = "Disk info";
        _strings["Menu.Commands.SyncPanels"] = "Sync panels";
        _strings["Menu.Commands.SwapPanels"] = "Swap panels";
        _strings["Menu.Commands.SwitchPanel"] = "Switch panel";
        _strings["Menu.Commands.Terminal"] = "Terminal";
        _strings["Menu.Commands.Palette"] = "Command palette";

        _strings["Menu.Tools"] = "Tools";
        _strings["Menu.Tools.Git"] = "Git";
        _strings["Menu.Tools.Docker"] = "Docker";
        _strings["Menu.Tools.Ssh"] = "SSH";
        _strings["Menu.Tools.Sftp"] = "SFTP";
        _strings["Menu.Tools.SplitCombine"] = "Split / Combine";
        _strings["Menu.Tools.Split"] = "Split file…";
        _strings["Menu.Tools.Combine"] = "Combine volumes…";
        _strings["Menu.Tools.Duplicates"] = "Find duplicates…";
        _strings["Menu.Tools.MultiRename"] = "Multi-rename…";

        // ═══════════════════════════════════════
        // MULTI-RENAME (ph2.3)
        // ═══════════════════════════════════════
        _strings["MultiRename.Title"] = "Multi-Rename";
        _strings["MultiRename.ModeMask"] = "Mask";
        _strings["MultiRename.ModeRegex"] = "Find / Replace";
        _strings["MultiRename.Mask"] = "Mask:";
        _strings["MultiRename.MaskHint"] = "[N] name, [E] extension, [C] counter, [G] GUID, [Y][M][D] date, [h][n][s] time, [V:name] variable. Substrings: [N2], [N2:5], [E2:4]. Negative indexes: [-1] — last char.";
        _strings["MultiRename.CounterStart"] = "Start:";
        _strings["MultiRename.CounterStep"] = "Step:";
        _strings["MultiRename.CounterWidth"] = "Width:";
        _strings["MultiRename.Find"] = "Find (regex):";
        _strings["MultiRename.Replace"] = "Replace ($1, $2 — groups):";
        _strings["MultiRename.CaseSensitive"] = "Case sensitive";
        _strings["MultiRename.Variables"] = "Variables [V:name] (one value for all files):";
        _strings["MultiRename.NameStyle"] = "Name style:";
        _strings["MultiRename.StyleAsIs"] = "As-is";
        _strings["MultiRename.StyleUpper"] = "UPPERCASE";
        _strings["MultiRename.StyleLower"] = "lowercase";
        _strings["MultiRename.StyleTitle"] = "Title Case";
        _strings["MultiRename.BadChar"] = "Bad char replacement:";
        _strings["MultiRename.BadCharTip"] = "<>:\"/\\|?* → replaced with specified char";
        _strings["MultiRename.EnableLog"] = "Enable rename log";
        _strings["MultiRename.LogPath"] = "Log path:";
        _strings["MultiRename.Preset"] = "Preset:";
        _strings["MultiRename.PresetSave"] = "Save";
        _strings["MultiRename.PresetLoad"] = "Load";
        _strings["MultiRename.PresetDelete"] = "Delete";
        _strings["MultiRename.Preview"] = "Preview:";
        _strings["MultiRename.ColOriginal"] = "Original Name";
        _strings["MultiRename.ColNew"] = "New Name";
        _strings["MultiRename.Apply"] = "Apply";
        _strings["MultiRename.PreviewStatus"] = "Changes: {0} of {1}";
        _strings["MultiRename.Done"] = "Renamed: {0}";
        _strings["MultiRename.DoneErrors"] = "Renamed: {0}, errors: {1}";
        _strings["MultiRename.NoSelection"] = "No items selected to rename";
        _strings["MultiRename.Applied"] = "Multi-rename done: {0} items";
        _strings["MultiRename.PresetSaveTitle"] = "Save preset";
        _strings["MultiRename.PresetSavePrompt"] = "Preset name:";
        _strings["MultiRename.PresetSaved"] = "Preset saved: {0}";
        _strings["MultiRename.PresetLoaded"] = "Preset loaded: {0}";
        _strings["MultiRename.PresetDeleted"] = "Preset deleted: {0}";
        _strings["MultiRename.PresetError"] = "Preset error: {0}";

        _strings["Menu.View"] = "View";
        _strings["Menu.View.Refresh"] = "Refresh";
        _strings["Menu.View.Hidden"] = "Hidden files";
        _strings["Menu.View.FlatView"] = "Recursive list (Flat View)";
        _strings["Menu.View.FlatViewLeft"] = "Flat View (left)";
        _strings["Menu.View.FlatViewRight"] = "Flat View (right)";

        _strings["Menu.Settings"] = "Settings";
        _strings["Menu.Settings.Dialog"] = "Settings...";
        _strings["Menu.Settings.Theme"] = "Theme";
        _strings["Menu.Settings.Theme.Dark"] = "Dark";
        _strings["Menu.Settings.Theme.Light"] = "Light";
        _strings["Menu.Settings.Theme.System"] = "System";

        _strings["Menu.Right"] = "Right";
        _strings["Menu.Right.Refresh"] = "Refresh panel";
        _strings["Menu.Right.ChangeDrive"] = "Change drive...";

        _strings["Menu.Help"] = "Help";
        _strings["Menu.Help.About"] = "About";

        // ═══════════════════════════════════════════
        // TOOLBAR TOOLTIPS
        // ═══════════════════════════════════════════
        _strings["Tip.Refresh"] = "Refresh (Ctrl+R)";
        _strings["Tip.Back"] = "Back";
        _strings["Tip.Forward"] = "Forward";
        _strings["Tip.Up"] = "Up";
        _strings["Tip.NewFolder"] = "New folder (F7)";
        _strings["Tip.Copy"] = "Copy (F5)";
        _strings["Tip.Move"] = "Move (F6)";
        _strings["Tip.Delete"] = "Delete (F8)";
        _strings["Tip.Rename"] = "Rename (F2)";
        _strings["Tip.Bookmarks"] = "Bookmarks";
        _strings["Tip.Git"] = "Git (F9)";
        _strings["Tip.Docker"] = "Docker";
        _strings["Tip.Ssh"] = "SSH";
        _strings["Tip.Sftp"] = "SFTP";

        // ═══════════════════════════════════════════
        // COMMAND PALETTE
        // ═══════════════════════════════════════════
        _strings["Palette.Select"] = "  select    ";
        _strings["Palette.Navigate"] = "  navigate";

        // ═══════════════════════════════════════════
        // FILE PANEL
        // ═══════════════════════════════════════════
        _strings["Panel.EditPath"] = "Edit path";
        _strings["Panel.Back"] = "Back (Alt+\u2190)";
        _strings["Panel.Forward"] = "Forward (Alt+\u2192)";
        _strings["Panel.Up"] = "Up (PgUp)";
        _strings["Panel.Refresh"] = "Refresh (Ctrl+R)";
        _strings["Panel.Filter"] = "Filter...";
        _strings["Panel.ClearFilter"] = "Clear filter";
        _strings["Panel.ColName"] = "NAME...";
        _strings["Panel.ColSize"] = "SIZE";
        _strings["Panel.ColDate"] = "DATE";

        //═══════════════════════════════════════════════════════════════════
        //CUSTOM COLUMNS (ph5.4)
        //═══════════════════════════════════════════════════════════════════
        _strings["Columns.Name"] = "Name";
        _strings["Columns.Extension"] = "Extension";
        _strings["Columns.Size"] = "Size";
        _strings["Columns.Modified"] = "Modified";
        _strings["Columns.Created"] = "Created";
        _strings["Columns.Attributes"] = "Attributes";
        _strings["Columns.Type"] = "Type";
        _strings["Columns.Configure"] = "Configure Columns…";
        _strings["Columns.Reset"] = "Reset";
        _strings["Columns.Apply"] = "Apply";
        _strings["Columns.Available"] = "Available columns:";
        _strings["Columns.Active"] = "Active columns:";
        _strings["Columns.Add"] = "Add";
        _strings["Columns.Remove"] = "Remove";
        _strings["Columns.MoveUp"] = "Up";
        _strings["Columns.MoveDown"] = "Down";
        _strings["Columns.Width"] = "Width:";
        _strings["Columns.Hide"] = "Hide column";
        _strings["Menu.View.Columns"] = "Configure Columns…";

        // ═══════════════════════════════════════════
        // QUICK FILTER / QUICK SEARCH (ph1.1)
        // ═══════════════════════════════════════════
        _strings["QuickFilter.Placeholder"] = "Quick filter…";
        _strings["Quick.MatchCase"] = "Match case (Aa)";
        _strings["Quick.MatchStart"] = "Match at start of name";
        _strings["Quick.Scope"] = "Filter scope: all / files / folders";

        // ═══════════════════════════════════════════
        // CONTEXT MENU (FilePanel)
        // ═══════════════════════════════════════════
        _strings["Ctx.Open"] = "Open";
        _strings["Ctx.View"] = "View (F3)";
        _strings["Ctx.Edit"] = "Edit (F4)";
        _strings["Ctx.Copy"] = "Copy (F5)";
        _strings["Ctx.Move"] = "Move (F6)";
        _strings["Ctx.CreateFolder"] = "Create folder (F7)";
        _strings["Ctx.Delete"] = "Delete (F8)";
        _strings["Ctx.Rename"] = "Rename (F2)";
        _strings["Ctx.Split"] = "Split into volumes…";
        _strings["Ctx.Combine"] = "Combine volumes…";
        _strings["Ctx.Refresh"] = "Refresh (Ctrl+R)";
        _strings["Ctx.FlatView"] = "Recursive list (Flat View)";

        // ═══════════════════════════════════════════
        // GIT PANEL
        // ═══════════════════════════════════════════
        _strings["Git.Refresh"] = "\u27F3 Refresh";
        _strings["Git.Fetch"] = "\u2193 Fetch";
        _strings["Git.Pull"] = "\u2913 Pull";
        _strings["Git.Push"] = "\u2912 Push";
        _strings["Git.Stash"] = "\uD83D\uDCE5 Stash";
        _strings["Git.PopStash"] = "\uD83D\uDCE4 Pop";
        _strings["Git.NewBranch"] = "\u22C7 New branch";
        _strings["Git.Branch"] = "Branch:";
        _strings["Git.Changes"] = "changes";
        _strings["Git.Commit"] = "\u2714 Commit (Ctrl+Enter)";
        _strings["Git.Amend"] = "Amend";
        _strings["Git.StageAll"] = "+ All";
        _strings["Git.UnstageAll"] = "- All";
        _strings["Git.OpenInEditor"] = "\u25C9 editor";
        _strings["Git.ChangesTitle"] = "Changes";
        _strings["Git.NoRepo"] = "Not a git repository";
        _strings["Git.NoChanges"] = "No changes";
        _strings["Git.NoUntracked"] = "No untracked changes";
        _strings["Git.SelectFileHint"] = "Select a file to view changes";
        _strings["Git.CommitHistory"] = "Commit history";
        _strings["Git.CommitMsgPlaceholder"] = "Enter commit message...";

        _strings["Git.Tip.Refresh"] = "Refresh repository state";
        _strings["Git.Tip.Fetch"] = "Fetch from upstream without merging";
        _strings["Git.Tip.Pull"] = "Fetch and merge changes";
        _strings["Git.Tip.Push"] = "Push to remote repository";
        _strings["Git.Tip.Stash"] = "Stash changes";
        _strings["Git.Tip.PopStash"] = "Pop stashed changes";
        _strings["Git.Tip.NewBranch"] = "Create new branch";
        _strings["Git.Tip.StageAll"] = "Stage all changes";
        _strings["Git.Tip.UnstageAll"] = "Unstage all changes";
        _strings["Git.Tip.OpenDiff"] = "Open diff in editor";
        _strings["Git.Tip.Stage"] = "Stage";
        _strings["Git.Tip.Unstage"] = "Unstage";
        _strings["Git.Tip.OpenEditor"] = "Open in editor";
        _strings["Git.Tip.Discard"] = "Discard changes";
        _strings["Git.Tip.Amend"] = "Amend last commit";
        _strings["Git.Tip.Branch"] = "Switch to another branch";

        // ═══════════════════════════════════════════
        // DOCKER PANEL
        // ═══════════════════════════════════════════
        _strings["Docker.Containers"] = "Containers";
        _strings["Docker.NoContainers"] = "No containers";
        _strings["Docker.RunHint"] = "Run docker run from terminal";
        _strings["Docker.Exec"] = "Exec";
        _strings["Docker.Logs"] = "Logs";
        _strings["Docker.Images"] = "Images";

        _strings["Docker.Tip.Refresh"] = "Refresh containers and images";
        _strings["Docker.Tip.Start"] = "Start selected container";
        _strings["Docker.Tip.Stop"] = "Stop selected container";
        _strings["Docker.Tip.Remove"] = "Remove selected container";
        _strings["Docker.Tip.Logs"] = "Show container logs";
        _strings["Docker.Tip.Exec"] = "Execute command in container";
        _strings["Docker.Tip.ExecCmd"] = "Enter command to execute in container";
        _strings["Docker.Tip.RemoveImage"] = "Remove selected image";

        // ═══════════════════════════════════════════
        // SSH PANEL
        // ═══════════════════════════════════════════
        _strings["Ssh.Profiles"] = "Server profiles";
        _strings["Ssh.New"] = "\uFF0B New";
        _strings["Ssh.Save"] = "Save";
        _strings["Ssh.Delete"] = "Delete";
        _strings["Ssh.CheckConnection"] = "Check connection";
        _strings["Ssh.NoProfiles"] = "No profiles. Click \"New\" to create one.";
        _strings["Ssh.Editor"] = "Profile editor";
        _strings["Ssh.ProfileName"] = "Profile name";
        _strings["Ssh.Host"] = "Host (IP or domain)";
        _strings["Ssh.User"] = "User";
        _strings["Ssh.Port"] = "Port";
        _strings["Ssh.RemotePath"] = "Remote path";
        _strings["Ssh.IdentityFile"] = "Private key (Identity File)";
        _strings["Ssh.Publish"] = "Publish";
        _strings["Ssh.PublishHint"] = "Select a file or folder in the active panel and click \"Publish to server\". The selected profile is used by default.";

        _strings["Ssh.Tip.New"] = "Create new profile";
        _strings["Ssh.Tip.Save"] = "Save profile changes";
        _strings["Ssh.Tip.Delete"] = "Delete selected profile";
        _strings["Ssh.Tip.Check"] = "Check server availability";
        _strings["Ssh.Tip.Edit"] = "Edit profile";
        _strings["Ssh.Tip.Key"] = "Absolute path to private key file (optional)";

        // ═══════════════════════════════════════════
        // SFTP PANEL
        // ═══════════════════════════════════════════
        _strings["Sftp.Connect"] = "Connect";
        _strings["Sftp.Reconnect"] = "Reconnect";
        _strings["Sftp.Refresh"] = "\u27F3 Refresh";
        _strings["Sftp.Download"] = "\u2B07 Download";
        _strings["Sftp.Upload"] = "\u2B06 Upload";
        _strings["Sftp.NewFolder"] = "\uFF0B Folder";
        _strings["Sftp.Up"] = "\u2191 Up";
        _strings["Sftp.Path"] = "Path:";
        _strings["Sftp.Loading"] = "Loading...";

        _strings["Sftp.Tip.SelectProfile"] = "Select SSH profile to connect";
        _strings["Sftp.Tip.Refresh"] = "Refresh file list";
        _strings["Sftp.Tip.Download"] = "Download selected file to local folder";
        _strings["Sftp.Tip.Upload"] = "Upload file to server";
        _strings["Sftp.Tip.NewFolder"] = "Create new folder on server";
        _strings["Sftp.Tip.Up"] = "Go to parent directory";

        _strings["Sftp.Ctx.Open"] = "Open / enter folder";
        _strings["Sftp.Ctx.Download"] = "Download";
        _strings["Sftp.Ctx.Rename"] = "Rename";
        _strings["Sftp.Ctx.Delete"] = "Delete";
        _strings["Sftp.Ctx.NewFolder"] = "Create folder";

        // ═══════════════════════════════════════════
        // EDITOR WINDOW
        // ═══════════════════════════════════════════
        _strings["Editor.Title"] = "Editor";
        _strings["Editor.Viewer"] = "Viewer";
        _strings["Editor.Lines"] = "lines";
        _strings["Editor.Modified"] = "[Modified]";
        _strings["Editor.Tip.Minimize"] = "Minimize";
        _strings["Editor.Tip.Maximize"] = "Maximize";
        _strings["Editor.Tip.Close"] = "Close (Esc)";
        _strings["Editor.ViewMode"] = "[view]";
        _strings["Editor.Restore"] = "Restore";
        _strings["Editor.Maximize"] = "Maximize";
        _strings["Editor.Normal"] = "Normal";
        _strings["Editor.Saved"] = "File saved: {0}";
        _strings["Editor.SaveOk"] = "Saved";
        _strings["Editor.SaveError"] = "Save error: {0}";
        _strings["Editor.UnsavedPrompt"] = "File has been modified. Save changes?";
        _strings["Editor.UnsavedTitle"] = "Unsaved changes";
        _strings["Error.Title"] = "Error";

        // ═══════════════════════════════════════════
        // EDITOR TABS (ph8.1) / ВКЛАДКИ РЕДАКТОРА
        // ═══════════════════════════════════════════
        _strings["Editor.Tab.Close"] = "Close tab";
        _strings["Editor.Tab.CloseOthers"] = "Close others";
        _strings["Editor.Tab.CloseAll"] = "Close all";
        _strings["Editor.Tab.New"] = "New tab";
        _strings["Editor.Tabs.UnsavedPrompt"] = "Save changes to {0}?";
        _strings["Editor.Tabs.UnsavedTitle"] = "Unsaved changes";
        _strings["Editor.GoToLine"] = "Go to line";
        _strings["Editor.GoToLine.Title"] = "Line number:";

        // ═══════════════════════════════════════════
        // IMAGE VIEWER
        // ═══════════════════════════════════════════
        _strings["Editor.Image.ZoomIn"] = "Zoom In";
        _strings["Editor.Image.ZoomOut"] = "Zoom Out";
        _strings["Editor.Image.ZoomReset"] = "Actual Size (1:1)";
        _strings["Editor.Image.ZoomFit"] = "Fit to Window";
        _strings["Editor.Image.RotateLeft"] = "Rotate Left 90°";
        _strings["Editor.Image.RotateRight"] = "Rotate Right 90°";
        _strings["Editor.Image.Slideshow"] = "Slideshow";
        _strings["Editor.Image.SlideshowStop"] = "Stop Slideshow";
        _strings["Editor.Image.Size"] = "{0} × {1} px";

        // ═══════════════════════════════════════════
        // SETTINGS WINDOW
        // ═══════════════════════════════════════════
        _strings["Settings.Title"] = "Settings";
        _strings["Settings.Appearance"] = "Appearance";
        _strings["Settings.Editor"] = "Editor";
        _strings["Settings.Terminal"] = "Terminal";
        _strings["Settings.Behavior"] = "Behavior";
        _strings["Settings.Language"] = "Language";
        _strings["Settings.LanguageDesc"] = "Interface language";
        _strings["Settings.Theme"] = "Theme";
        _strings["Settings.ThemeDesc"] = "Color scheme";
        _strings["Settings.Theme.Dark"] = "Dark";
        _strings["Settings.Theme.Light"] = "Light";
        _strings["Settings.Theme.System"] = "System";
        _strings["Settings.ShowHidden"] = "Show hidden files";
        _strings["Settings.ShowHiddenDesc"] = "Display files with Hidden attribute";
        _strings["Settings.EditorFont"] = "Editor font";
        _strings["Settings.EditorFontDesc"] = "Font family for code display";
        _strings["Settings.EditorFontSize"] = "Font size";
        _strings["Settings.EditorFontSizeDesc"] = "Font size in points";
        _strings["Settings.LineNumbers"] = "Line numbers";
        _strings["Settings.LineNumbersDesc"] = "Show line numbers on the left";
        _strings["Settings.WordWrap"] = "Word wrap";
        _strings["Settings.WordWrapDesc"] = "Wrap long lines";
        _strings["Settings.TabWidth"] = "Tab width";
        _strings["Settings.TabWidthDesc"] = "Number of spaces per indent";
        _strings["Settings.UseSpaces"] = "Spaces instead of tabs";
        _strings["Settings.UseSpacesDesc"] = "Use spaces instead of tab character";
        _strings["Settings.ColumnRuler"] = "Column ruler";
        _strings["Settings.ColumnRulerDesc"] = "Show vertical line at column position";
        _strings["Settings.RulerPosition"] = "Ruler position";
        _strings["Settings.RulerPositionDesc"] = "Column number for the ruler line";
        _strings["Settings.ShowSpaces"] = "Show spaces";
        _strings["Settings.ShowSpacesDesc"] = "Display space characters as dots";
        _strings["Settings.ShowTabs"] = "Show tabs";
        _strings["Settings.ShowTabsDesc"] = "Display tab characters as arrows";
        _strings["Settings.ShowEndOfLine"] = "Show line endings";
        _strings["Settings.ShowEndOfLineDesc"] = "Display end-of-line markers (CR/LF)";
        _strings["Editor.SaveAs"] = "Save As";
        _strings["Editor.ToggleReadOnly"] = "Toggle Read-Only";
        _strings["Editor.Words"] = "words";
        _strings["Settings.DefaultShell"] = "Default shell";
        _strings["Settings.DefaultShellDesc"] = "Shell for new tabs";
        _strings["Settings.TerminalFont"] = "Terminal font";
        _strings["Settings.TerminalFontDesc"] = "Font family for console";
        _strings["Settings.TerminalFontSize"] = "Font size";
        _strings["Settings.ConfirmDelete"] = "Confirm delete";
        _strings["Settings.ConfirmDeleteDesc"] = "Ask for confirmation when deleting files";
        _strings["Settings.ConfirmOverwrite"] = "Confirm overwrite";
        _strings["Settings.ConfirmOverwriteDesc"] = "Ask for confirmation when overwriting files";
        _strings["Settings.AutoRefresh"] = "Auto-refresh panels";
        _strings["Settings.AutoRefreshDesc"] = "Automatically refresh folder contents";
        _strings["Settings.RefreshInterval"] = "Refresh interval (ms)";
        _strings["Settings.RefreshIntervalDesc"] = "Auto-refresh period in milliseconds";
        _strings["Settings.PanelFont"] = "Panel font";
        _strings["Settings.PanelFontDesc"] = "Font family for file list panels";
        _strings["Settings.PanelFontSize"] = "Panel font size";
        _strings["Settings.PanelFontSizeDesc"] = "Font size in points for file list";
        _strings["Settings.Reset"] = "Reset";
        _strings["Settings.Save"] = "Save";
        _strings["Settings.Saved"] = "Settings saved";
        _strings["Settings.ResetConfirm"] = "Reset settings to defaults?";
        _strings["Settings.ResetTitle"] = "Reset settings";

        // ═══════════════════════════════════════════
        // HOTKEYS (ph6.1)
        // ═══════════════════════════════════════════
        _strings["Settings.Hotkeys"] = "Hotkeys";
        _strings["Settings.HotkeysDesc"] = "Customize panel keyboard shortcuts";
        _strings["Hotkey.Action"] = "Action";
        _strings["Hotkey.Key"] = "Key combination";
        _strings["Hotkey.Description"] = "Description";
        _strings["Hotkey.CaptureHint"] = "Click 'Change' then press a key...";
        _strings["Hotkey.Capturing"] = "Press a key...";
        _strings["Hotkey.Change"] = "Change";
        _strings["Hotkey.Reset"] = "Reset";
        _strings["Hotkey.ResetConfirm"] = "Reset all hotkeys to defaults?";

        // ═══════════════════════════════════════════
        // STATUS MESSAGES
        // ═══════════════════════════════════════════
        _strings["Status.Ready"] = "Ready";
        _strings["Status.ItemsIn"] = "{0} items in {1}";
        _strings["Status.Dir"] = "[DIR] {0}";
        _strings["Status.File"] = "{0} {1} {2}";
        _strings["Status.Copied"] = "Copied: ";
        _strings["Status.Moved"] = "Moved: ";
        _strings["Status.Deleted"] = "Deleted: {0}";
        _strings["Status.DeletedOf"] = "Deleted {0} of {1}. Error: {2}";
        _strings["Status.Searching"] = "Searching...";
        _strings["Status.Found"] = "Found: {0}";
        _strings["Status.NotFound"] = "Nothing found";
        _strings["Status.PanelsSynced"] = "Panels synced";
        _strings["Status.PanelsSwapped"] = "Panels swapped";
        _strings["Status.Compared"] = "Directories compared";
        _strings["Status.NoSshProfiles"] = "No SSH profiles";
        _strings["Status.Error"] = "Error: {0}";
        _strings["Split.NoFiles"] = "No files selected to split";
        _strings["Split.SizeTitle"] = "Volume size";
        _strings["Split.SizePrompt"] = "Enter volume size (e.g. 100M, 1.5G, 500K):";
        _strings["Split.BadSize"] = "Invalid volume size";
        _strings["Split.Started"] = "Splitting file…";
        _strings["Split.Done"] = "Split done: {0} file(s)";
        _strings["Combine.NoFile"] = "Select .sum or first volume (.001)";
        _strings["Combine.Started"] = "Combining volumes…";
        _strings["Combine.Done"] = "Combine done: {0} ({1} volumes)";

        // ═══════════════════════════════════════════
        // DIALOGS
        // ═══════════════════════════════════════════
        _strings["Dialog.ConfirmDelete"] = "Delete {0} item(s)?";
        _strings["Dialog.ConfirmDeleteTitle"] = "Confirmation";
        _strings["Dialog.Overwrite"] = "\"{0}\" already exists. Overwrite?";
        _strings["Dialog.OverwriteTitle"] = "Conflict";
        _strings["Dialog.ApplyAll"] = "Apply to all?";
        _strings["Dialog.NewFolderTitle"] = "New folder";
        _strings["Dialog.NewFolderName"] = "Name:";
        _strings["Dialog.RenameTitle"] = "Rename";
        _strings["Dialog.RenameName"] = "New name:";
        _strings["Dialog.SelectFilesTitle"] = "Select files";
        _strings["Dialog.SelectFilesMask"] = "Mask (e.g., *.txt):";
        _strings["Dialog.DeselectFilesTitle"] = "Deselect files";
        _strings["Dialog.SearchTitle"] = "Search files";
        _strings["Dialog.SearchName"] = "Name (masks: *, ?):";
        _strings["Dialog.SearchText"] = "Text in file (empty = by name):";
        _strings["Dialog.SearchResults"] = "Found: {0}";
        _strings["Dialog.SearchEmpty"] = "Files not found";
        _strings["Dialog.SearchTruncated"] = "\n\n... and {0} more";
        _strings["Dialog.FileInfo"] = "Info";
        _strings["Dialog.DiskInfo"] = "Disk info";
        _strings["Dialog.AboutTitle"] = "About";
        _strings["Dialog.About"] = "Coder Commander v1.0\nTwo-panel file manager for developers\n\n.NET 8 + WPF + AvalonEdit + Git + Docker + SSH + SFTP";
        _strings["Dialog.Cancel"] = "Cancel";
        _strings["Dialog.Ok"] = "OK";
        _strings["Dialog.UnhandledException"] = "Unhandled exception";

        // ═══════════════════════════════════════════
        // MESSAGE BOX BUTTONS / КНОПКИ MSGBOX
        // ═══════════════════════════════════════════
        _strings["MsgBox.OK"] = "OK";
        _strings["MsgBox.Yes"] = "Yes";
        _strings["MsgBox.No"] = "No";
        _strings["MsgBox.Cancel"] = "Cancel";
        _strings["MsgBox.Message"] = "Message";

        // ═══════════════════════════════════════════
        // SEARCH DIALOG (ph2.1)
        // ═══════════════════════════════════════════
        _strings["Menu.Search.Dialog"] = "Search…";
        _strings["Search.LastResults"] = "Last search results";
        _strings["Search.Dialog.Title"] = "File Search";
        _strings["Search.Root"] = "Folder";
        _strings["Search.NameMasks"] = "Name masks (; separated, * and ?):";
        _strings["Search.NameRegex"] = "Name regex mode";
        _strings["Search.Content"] = "Search in content (regex):";
        _strings["Search.MatchCase"] = "Match case";
        _strings["Search.RecurseLabel"] = "Recursion";
        _strings["Search.Recurse"] = "Including subfolders";
        _strings["Search.SizeFilter"] = "Size (from/to):";
        _strings["Search.SizeFrom"] = "from";
        _strings["Search.SizeTo"] = "to";
        _strings["Search.Date"] = "Modified date (from/to):";
        _strings["Search.DateFrom"] = "from";
        _strings["Search.DateTo"] = "to";
        _strings["Search.Encoding"] = "Encoding:";
        _strings["Search.EncodingFallback"] = "(if no BOM)";
        _strings["Search.Attributes"] = "Attributes (required):";
        _strings["Search.Attr.ReadOnly"] = "Read-only";
        _strings["Search.Attr.Hidden"] = "Hidden";
        _strings["Search.Attr.System"] = "System";
        _strings["Search.Attr.Archive"] = "Archive";
        _strings["Search.InArchives"] = "Search in archives:"; // ph4.1
        _strings["Search.InArchives.Tip"] = "ZIP, 7Z, RAR, TAR, GZ (up to 500 MB)"; // ph4.1
        _strings["Search.Start"] = "Start Search";
        _strings["Search.Cancel"] = "Cancel";
        _strings["Search.Clear"] = "Clear";
        _strings["Search.Name"] = "Name";
        _strings["Search.Path"] = "Path";
        _strings["Search.Col.Size"] = "Size";
        _strings["Search.Col.Line"] = "Line";
        _strings["Search.Col.Match"] = "Match";

        // ═══════════════════════════════════════════
        // QUICK COMMANDS
        // ═══════════════════════════════════════════
        _strings["Quick.GitStatus"] = "Git: Status";
        _strings["Quick.GitStatusDesc"] = "Show git panel";
        _strings["Quick.GitStatusResult"] = "Git opened";
        _strings["Quick.GitCommitAll"] = "Git: Commit all";
        _strings["Quick.GitCommitAllDesc"] = "Stage and commit";
        _strings["Quick.GitCommitMsg"] = "Message:";
        _strings["Quick.GitCommitResult"] = "Commit created";
        _strings["Quick.GitNotRepo"] = "Not a repository";
        _strings["Quick.GitCancelled"] = "Cancelled";
        _strings["Quick.GitPush"] = "Git: Push";
        _strings["Quick.GitPushDesc"] = "Push to remote";
        _strings["Quick.GitPushResult"] = "Push done";
        _strings["Quick.GitPull"] = "Git: Pull";
        _strings["Quick.GitPullDesc"] = "Pull changes";
        _strings["Quick.GitPullResult"] = "Pull done";
        _strings["Quick.DockerPanel"] = "Docker: Panel";
        _strings["Quick.DockerPanelDesc"] = "Show docker containers";
        _strings["Quick.DockerResult"] = "Docker opened";
        _strings["Quick.ComposeUp"] = "Docker Compose: Up";
        _strings["Quick.ComposeUpDesc"] = "Start compose in current folder";
        _strings["Quick.ComposeUpResult"] = "Compose up";
        _strings["Quick.ComposeDown"] = "Docker Compose: Down";
        _strings["Quick.ComposeDownDesc"] = "Stop compose";
        _strings["Quick.ComposeDownResult"] = "Compose down";
        _strings["Quick.SshPanel"] = "SSH: Profiles";
        _strings["Quick.SshPanelDesc"] = "Show server profiles";
        _strings["Quick.SshResult"] = "SSH opened";
        _strings["Quick.SftpPanel"] = "SFTP: Panel";
        _strings["Quick.SftpPanelDesc"] = "Show remote filesystem browser";
        _strings["Quick.SftpResult"] = "SFTP opened";
        _strings["Quick.CloudStorage"] = "Cloud Storage: Panel";
        _strings["Quick.CloudStorageDesc"] = "Show cloud storage browser";
        _strings["Quick.CloudStorageResult"] = "Cloud Storage opened";
        _strings["Quick.TerminalOpen"] = "Terminal: Open";
        _strings["Quick.TerminalOpenDesc"] = "Open built-in terminal";
        _strings["Quick.TerminalResult"] = "Terminal opened";

        // ═══════════════════════════════════════════
        // BOOKMARKS
        // ═══════════════════════════════════════════
        _strings["Bookmark.Desktop"] = "Desktop";
        _strings["Bookmark.Documents"] = "Documents";
        _strings["Bookmark.Downloads"] = "Downloads";

    //═══════════════════════════════════════════
    //BOOKMARKS MANAGEMENT (ph5.3)
    //═══════════════════════════════════════════
    _strings["Menu.Bookmarks"] = "Bookmarks";
    _strings["Bookmark.Add"] = "Add bookmark";
    _strings["Bookmark.Remove"] = "Remove bookmark";
    _strings["Bookmark.Rename"] = "Rename bookmark";
    _strings["Bookmark.Navigate"] = "Navigate to bookmark";
    _strings["Bookmark.Manage"] = "Manage bookmarks…";
    _strings["Bookmark.AlreadyExists"] = "This path is already bookmarked";
    _strings["Bookmark.Added"] = "Bookmark added: {0}";
    _strings["Bookmark.Name"] = "Name:";
    _strings["Bookmark.Export"] = "Export to JSON";
    _strings["Bookmark.Import"] = "Import from JSON";
    _strings["Bookmark.ExportDone"] = "Bookmarks exported: {0}";
    _strings["Bookmark.ImportResult"] = "Import: added {0}, skipped {1}";
    _strings["Ctx.AddBookmark"] = "Add to bookmarks…";

        // ═══════════════════════════════════════════
        // PROGRESS / COPY-MOVE
        // ═══════════════════════════════════════════
        _strings["Progress.Copying"] = "Copying";
        _strings["Progress.Moving"] = "Moving";
        _strings["Progress.Deleting"] = "Deleting: ";

        // ═══════════════════════════════════════════
        // DUPLICATES WINDOW (ph2.4)
        // ═══════════════════════════════════════════
        _strings["Dup.Title"] = "Find Duplicates";
        _strings["Dup.Folder"] = "Folder:";
        _strings["Dup.Browse"] = "Browse";
        _strings["Dup.IncludeSubfolders"] = "Include subfolders";
        _strings["Dup.Criterion"] = "Criterion:";
        _strings["Dup.Search"] = "Find Duplicates";
        _strings["Dup.Cancel"] = "Cancel";
        _strings["Dup.MarkExceptFirst"] = "Mark all except first";
        _strings["Dup.ClearMarks"] = "Clear marks";
        _strings["Dup.DeleteMarked"] = "Delete marked";
        _strings["Dup.DeleteTitle"] = "Delete confirmation";
        _strings["Dup.Status.Gather"] = "Gathering files…";
        _strings["Dup.Status.Hashing"] = "Computing hashes…";
        _strings["Dup.Status.None"] = "No duplicates found";
        _strings["Dup.Status.Found"] = "Groups found: {0}, duplicates: {1}";
        _strings["Dup.Status.NoFolder"] = "Folder not selected or missing";
        _strings["Dup.Status.DeleteConfirm"] = "Delete {0} file(s) from \"{1}\"? This cannot be undone.";
        _strings["Dup.Status.Deleted"] = "Deleted: {0}, errors: {1}";
        _strings["Dup.NothingMarked"] = "No files marked for deletion";
        _strings["Dup.Differ"] = "differs (not a duplicate)";
        _strings["Dup.FileCountSuffix"] = " file(s)";

        // ═══════════════════════════════════════════
        // SYNC DIRECTORIES WINDOW (ph3.3)
        // ═══════════════════════════════════════════
        _strings["Menu.Commands.SyncDirs"] = "Sync directories…";
        _strings["Sync.Title"] = "Sync Directories";
        _strings["Sync.Left"] = "Left:";
        _strings["Sync.Right"] = "Right:";
        _strings["Sync.Mask"] = "Mask:";
        _strings["Sync.IncludeSubfolders"] = "Include subfolders";
        _strings["Sync.OnlySelected"] = "Selected only";
        _strings["Sync.Mode"] = "Compare:";
        _strings["Sync.Direction"] = "Direction:";
        _strings["Sync.Col.Apply"] = "✓";
        _strings["Sync.Col.Path"] = "Path";
        _strings["Sync.Col.LeftSize"] = "Size ←";
        _strings["Sync.Col.LeftTime"] = "Modified ←";
        _strings["Sync.Col.RightSize"] = "Size →";
        _strings["Sync.Col.RightTime"] = "Modified →";
        _strings["Sync.Col.Diff"] = "Diff";
        _strings["Sync.Col.Action"] = "Action";
        _strings["Sync.All"] = "All";
        _strings["Sync.None"] = "None";
        _strings["Sync.Set"] = "Set for selected:";
        _strings["Sync.CopyLeft"] = "← To left";
        _strings["Sync.CopyRight"] = "To right →";
        _strings["Sync.DeleteBoth"] = "Delete both";
        _strings["Sync.ClearAction"] = "No action";
        _strings["Sync.ToApply"] = "to apply";
        _strings["Sync.Compare"] = "Compare";
        _strings["Sync.Apply"] = "Apply";
        _strings["Sync.Cancel"] = "Cancel";
        _strings["Sync.Status.Scan"] = "Scanning folders…";
        _strings["Sync.Status.Found"] = "Pairs: {0}, need action: {1}";
        _strings["Sync.Status.Cancelled"] = "Cancelled";
        _strings["Sync.Status.Applying"] = "Applying actions…";
        _strings["Sync.Status.ApplyingFile"] = "Applying: {0}";
        _strings["Sync.Status.Applied"] = "Done: {0} succeeded, {1} errors";
        _strings["Sync.Mode.SizeMtime"] = "By size and date";
        _strings["Sync.Mode.Content"] = "By content (SHA-256 hash)";
        _strings["Sync.Direction.CopyToNewer"] = "Newer → older";
        _strings["Sync.Direction.CopyToLeft"] = "To left panel";
        _strings["Sync.Direction.CopyToRight"] = "To right panel";
        _strings["Sync.Action.None"] = "No action";
        _strings["Sync.Action.Equal"] = "Equal";
        _strings["Sync.Action.CopyLeft"] = "← To left";
        _strings["Sync.Action.CopyRight"] = "To right →";
        _strings["Sync.Action.DeleteLeft"] = "Delete ←";
        _strings["Sync.Action.DeleteRight"] = "Delete →";
        _strings["Sync.Action.DeleteBoth"] = "Delete both";
        _strings["Sync.Asymmetric"] = "Asymmetric";
        _strings["Sync.AsymmetricDesc"] = "Copy from left panel only; right files are skipped, no deletions";

        // FILE ATTRIBUTES / TIMESTAMPS / LINKS (ph2.5)
        // ═══════════════════════════════════════════
        _strings["Attr.Title"] = "File Properties";
        _strings["Attr.Menu"] = "Attributes:";
        _strings["Attr.HardlinkMenu"] = "Create hardlink\u2026";
        _strings["Attr.SymlinkMenu"] = "Create symlink\u2026";
        _strings["Attr.Single"] = "File: {0}";
        _strings["Attr.FilesSelected"] = "Files selected: {0} (applies to all)";
        _strings["Attr.NoSelection"] = "No files selected";
        _strings["Attr.SectionAttr"] = "Attributes";
        _strings["Attr.ReadOnly"] = "Read-only";
        _strings["Attr.Hidden"] = "Hidden";
        _strings["Attr.System"] = "System";
        _strings["Attr.Archive"] = "Archive";
        _strings["Attr.SectionTime"] = "Timestamps";
        _strings["Attr.Created"] = "Created";
        _strings["Attr.Modified"] = "Modified";
        _strings["Attr.Accessed"] = "Accessed";
        _strings["Attr.ApplyCreated"] = "Apply created date";
        _strings["Attr.ApplyModified"] = "Apply modified date";
        _strings["Attr.ApplyAccessed"] = "Apply accessed date";
        _strings["Attr.SectionLink"] = "Hard and symbolic links";
        _strings["Attr.LinkTarget"] = "Target / new link";
        _strings["Attr.LinkPlaceholder"] = "For Hardlink — new path; for Symlink — existing file (for multiple — folder)";
        _strings["Attr.CreateHardlink"] = "Create hardlink";
        _strings["Attr.CreateSymlink"] = "Create symlink";
        _strings["Attr.Apply"] = "Apply";
        _strings["Attr.Cancel"] = "Cancel";
        _strings["Attr.Done"] = "Done";
        _strings["Attr.AttrApplied"] = "Attributes and dates applied to {0} items";
        _strings["Attr.HardlinkDone"] = "Hardlinks created: {0}, errors: {1}";
        _strings["Attr.SymlinkDone"] = "Symlinks created: {0}, errors: {1}";
        _strings["Attr.ErrTargetEmpty"] = "Link target is empty";
        _strings["Attr.ErrTargetNotDir"] = "For multiple files target must be a directory";
        _strings["Attr.ErrLinkExists"] = "Link already exists: {0}";
        _strings["Attr.ErrAccess"] = "Access denied (run as admin for links): {0}";

        // ═══════════════════════════════════════════
        // BINARY DIFF / HEX VIEWER (ph3.2)
        // ═══════════════════════════════════════════
        _strings["Hex.Tab"] = "Binary (Hex)";
        _strings["Hex.Identical"] = "Files are identical (binary)";
        _strings["Hex.Status"] = "Left: {0}, Right: {1} · Different: {2} bytes in {3} ranges";
        _strings["Hex.Prev"] = "Previous diff";
        _strings["Hex.Next"] = "Next diff";

        // ═══════════════════════════════════════════
        // CROSS-VFS (ph4.2): Local ↔ SFTP
        // ═══════════════════════════════════════════
        _strings["CrossVfs.Connecting"] = "Connecting to SFTP: {0}…";
        _strings["CrossVfs.Connected"] = "SFTP connected: {0}";
        _strings["CrossVfs.ConnectFailed"] = "Failed to connect to {0}";
        _strings["CrossVfs.Disconnected"] = "SFTP disconnected";
        _strings["CrossVfs.Uploading"] = "Uploading to server: {0}";
        _strings["CrossVfs.Downloading"] = "Downloading: {0}";
        _strings["CrossVfs.Moving"] = "Moving to server: {0}";
        _strings["CrossVfs.CopyDone"] = "Copied to SFTP: {0}";
        _strings["CrossVfs.CopyDoneErrors"] = "Copied: {0}, errors: {1}";
        _strings["CrossVfs.MoveDone"] = "Moved to SFTP: {0}";
        _strings["CrossVfs.MoveDoneErrors"] = "Moved: {0}, errors: {1}";
        _strings["CrossVfs.Cancelled"] = "Operation cancelled";
        _strings["CrossVfs.Error"] = "Error: {0}";
        _strings["CrossVfs.ConfirmMove"] = "Move {0} item(s) to SFTP server?\nSources will be deleted after transfer.";
        _strings["CrossVfs.MoveTitle"] = "Move to SFTP";
        _strings["CrossVfs.CopyToSftp"] = "Copy to SFTP…";
        _strings["CrossVfs.MoveToSftp"] = "Move to SFTP…";
        _strings["CrossVfs.DownloadFromSftp"] = "Download from SFTP…";
        _strings["CrossVfs.Disconnect"] = "Disconnect SFTP";
        _strings["CrossVfs.NoConnection"] = "No SFTP connection";

        //═══════════════════════════════════════════
        //ARCHIVE PACK / UNPACK (ph5.1)
        //═══════════════════════════════════════════
        _strings["Archive.Title"] = "Archive Manager";
        _strings["Archive.TabCreate"] = "Create archive";
        _strings["Archive.TabExtract"] = "Extract archive";
        _strings["Archive.Create"] = "Create";
        _strings["Archive.Extract"] = "Extract";
        _strings["Archive.Cancel"] = "Cancel";
        _strings["Archive.Format"] = "Format:";
        _strings["Archive.Compression"] = "Compression:";
        _strings["Archive.CompNone"] = "None";
        _strings["Archive.CompFast"] = "Fast";
        _strings["Archive.CompOptimal"] = "Optimal";
        _strings["Archive.CompBest"] = "Maximum";
        _strings["Archive.Format.Zip"] = "ZIP";
        _strings["Archive.Format.SevenZip"] = "7Z";
        _strings["Archive.Format.Tar"] = "TAR";
        _strings["Archive.Format.TarGz"] = "TAR.GZ";
        _strings["Archive.Format.TarBz2"] = "TAR.BZ2";
        _strings["Archive.Format.GZip"] = "GZIP";
        _strings["Archive.Format.BZip2"] = "BZIP2";
        _strings["Archive.Password"] = "Password:";
        _strings["Archive.OutputPath"] = "Archive:";
        _strings["Archive.ArchivePath"] = "Archive:";
        _strings["Archive.ExtractTo"] = "Extract to:";
        _strings["Archive.ClearFiles"] = "Clear";
        _strings["Archive.SelectAll"] = "All";
        _strings["Archive.SelectNone"] = "None";
        _strings["Archive.Ready"] = "Ready";
        _strings["Archive.NoFiles"] = "No files to archive";
        _strings["Archive.NoOutputPath"] = "Output path not specified";
        _strings["Archive.NoArchive"] = "Archive not found";
        _strings["Archive.NoArchiveSelected"] = "Select an archive file (.zip/.7z/.rar/.tar/.gz)";
        _strings["Archive.NoExtractPath"] = "Destination not specified";
        _strings["Archive.Creating"] = "Creating archive…";
        _strings["Archive.Extracting"] = "Extracting archive…";
        _strings["Archive.CreateDone"] = "Archive created: {0}";
        _strings["Archive.ExtractDone"] = "Extracted to: {0}";
        _strings["Archive.Cancelled"] = "Operation cancelled";
        _strings["Archive.SomeFilesMissing"] = "{0} files not found, skipping them";
        _strings["Archive.Loading"] = "Loading archive contents…";
        _strings["Archive.EntriesLoaded"] = "Archive entries: {0}";
        _strings["Menu.Tools.Archive"] = "Archives";
        _strings["Menu.Tools.ArchiveCreate"] = "Create archive…";
        _strings["Menu.Tools.ArchiveExtract"] = "Extract archive…";
        _strings["Ctx.ArchiveCreate"] = "Create archive…";
        _strings["Ctx.ArchiveExtract"] = "Extract archive…";

        // ═══════════════════════════════════════════
        // PANEL TABS (ph5.9)
        // ═══════════════════════════════════════════
        _strings["Tab.New"] = "New tab";
        _strings["Tab.Close"] = "Close tab";
        _strings["Tab.CloseOthers"] = "Close others";
        _strings["Tab.CloseAll"] = "Close all";

        //OPERATION QUEUE (ph5.2)
        //═══════════════════════════════════════════
        _strings["OpQueue.Title"] = "Operation Queue";
        _strings["OpQueue.Stats.Active"] = "active";
        _strings["OpQueue.Stats.Pending"] = "pending";
        _strings["OpQueue.Stats.Completed"] = "completed";
        _strings["OpQueue.CancelAll"] = "Cancel all";
        _strings["OpQueue.ClearCompleted"] = "Clear completed";
        _strings["OpQueue.RetryFailed"] = "Retry failed";
        _strings["OpQueue.Section.Active"] = "▸ Active operations";
        _strings["OpQueue.Section.Pending"] = "▸ Pending";
        _strings["OpQueue.Col.Description"] = "Description";
        _strings["OpQueue.Col.Status"] = "Status";
        _strings["OpQueue.Col.File"] = "File";
        _strings["OpQueue.Col.Progress"] = "Progress";
        _strings["OpQueue.Col.Percent"] = "%";
        _strings["OpQueue.Col.Actions"] = "Action";
        _strings["OpQueue.Tip.CancelOne"] = "Cancel operation";
        _strings["OpQueue.Tip.Close"] = "Close (Esc)";
        _strings["OpQueue.Footer.Hint"] = "Up to 3 parallel operations. Cancelled and completed can be retried.";
        _strings["OpQueue.StatusBar.Tooltip"] = "Operation queue: {0} active, {1} pending";
        _strings["OpQueue.Status.Running"] = "Running";
        _strings["OpQueue.Status.Completed"] = "Done";
        _strings["OpQueue.Status.Failed"] = "Failed";
        _strings["OpQueue.Status.Cancelled"] = "Cancelled";
        _strings["OpQueue.Status.Queued"] = "Queued";
        _strings["OpQueue.Status.Paused"] = "Paused";
        _strings["OpQueue.Title"] = "Operation Queue";
        _strings["OpQueue.Title.Active"] = "Queue ({0} active, {1} pending)";
        _strings["OpQueue.Title.Completed"] = "Queue completed";
        _strings["OpQueue.Pause"] = "Pause";
        _strings["OpQueue.Resume"] = "Resume";
        _strings["OpQueue.NoActive"] = "Waiting for operations...";
        _strings["OpQueue.NoOperations"] = "No operations in queue";
        _strings["OpQueue.CloseConfirm"] = "Close window? Operations will continue in background.\n\nCancel all operations?";
        _strings["OpQueue.CloseTitle"] = "Close Queue";
        _strings["OpQueue.Title.Operations"] = "Operations ({0})";
        _strings["OpQueue.Col.Type"] = "Type";
        _strings["OpQueue.Col.Source"] = "Source";
        _strings["OpQueue.Col.Destination"] = "Destination";
        _strings["OpQueue.Col.Status"] = "Status";
        _strings["OpQueue.Col.Percent"] = "%";
        _strings["OpQueue.Operation.Copy"] = "Copy";
        _strings["OpQueue.Operation.Move"] = "Move";
        _strings["OpDlg.CurrentFile"] = "File:";
        _strings["OpDlg.Total"] = "Total:";
        // ═══════════════════════════════════════════
        // OPEN WITH DIALOG (ph5.5)
        // ═══════════════════════════════════════════
        _strings["OpenWith.Title"] = "Open With";
        _strings["OpenWith.Menu"] = "Open with…";
        _strings["OpenWith.SetDefault"] = "Use as default";
        _strings["OpenWith.Browse"] = "Browse…";
        _strings["OpenWith.BrowseTitle"] = "Choose application";
        _strings["OpenWith.Open"] = "Open";
        _strings["OpenWith.Cancel"] = "Cancel";
        _strings["OpenWith.Opened"] = "Opened {0} in {1}";

        // ═══════════════════════════════════════════
        // QUICK VIEW (ph5.5)
        // ═══════════════════════════════════════════
        _strings["QuickView.Title"] = "Quick View";
        _strings["QuickView.Toggle"] = "Quick View";
        _strings["QuickView.NoPreview"] = "Preview not available";
        _strings["QuickView.CannotLoad"] = "Failed to load image";

        // ═══════════════════════════════════════════
        // OPERATION DIALOG (ph5.7)
        // ═══════════════════════════════════════════
        _strings["OpDlg.Title.Copy"] = "Copying";
        _strings["OpDlg.Title.Move"] = "Moving";
        _strings["OpDlg.Title.Delete"] = "Deleting";
        _strings["OpDlg.Speed"] = "Speed:";
        _strings["OpDlg.ETA"] = "ETA:";
        _strings["OpDlg.of"] = "of";
        _strings["OpDlg.Cancel"] = "Cancel";
        _strings["OpDlg.State.Running"] = "Running…";
        _strings["OpDlg.State.Completed"] = "Completed";
        _strings["OpDlg.State.Canceled"] = "Cancelled";
        _strings["OpDlg.State.Failed"] = "Failed";

        // ═══════════════════════════════════════════
        // OVERWRITE DIALOG (ph5.7)
        // ═══════════════════════════════════════════
        _strings["OpDlg.Overwrite.Title"] = "Overwrite Conflict";
        _strings["OpDlg.Overwrite.Source"] = "Source:";
        _strings["OpDlg.Overwrite.Dest"] = "Destination:";
        _strings["OpDlg.Overwrite.Skip"] = "Skip";
        _strings["OpDlg.Overwrite.Overwrite"] = "Overwrite";
        _strings["OpDlg.Overwrite.Older"] = "If older";
        _strings["OpDlg.Overwrite.Rename"] = "Rename";
        _strings["OpDlg.Overwrite.Abort"] = "Abort";
        _strings["OpDlg.Overwrite.ApplyAll"] = "Apply to all conflicts";

        // ═══════════════════════════════════════════
        // EXTENDED CONTEXT MENU (ph6.2)
        // ═══════════════════════════════════════════
        _strings["Ctx.CopyPath"] = "Copy path";
        _strings["Ctx.CopyPath.Full"] = "Full path";
        _strings["Ctx.CopyPath.Name"] = "File name";
        _strings["Ctx.CopyPath.NoExt"] = "Path without extension";
        _strings["Ctx.CopyPath.NameNoExt"] = "Name without extension";
        _strings["Ctx.OpenInExplorer"] = "Open in Explorer";
        _strings["Ctx.OpenInTerminal"] = "Open in terminal";
        _strings["Ctx.CopyToOther"] = "Copy to other panel";
        _strings["Ctx.MoveToOther"] = "Move to other panel";
        _strings["Ctx.Compare"] = "Compare files";
        _strings["Ctx.Checksum"] = "Checksum (SHA-256)";
        _strings["Ctx.SelectGroup"] = "Select group…";
        _strings["Ctx.DeselectGroup"] = "Deselect group…";
        _strings["Ctx.GroupMask"] = "File mask (e.g. *.txt):";
        _strings["Ctx.PathCopied"] = "Path copied";
        _strings["Ctx.NameCopied"] = "Name copied";
        _strings["Ctx.Copied"] = "Copied: {0}";
        _strings["Ctx.Moved"] = "Moved: {0}";

        // ═══════════════════════════════════════════
        // DRAG & DROP (ph6.3)
        // ═══════════════════════════════════════════
        _strings["DragDrop.Copied"] = "Drag-copied: {0}";
        _strings["DragDrop.Moved"] = "Drag-moved: {0}";

        // ═══════════════════════════════════════════
        // TERMINAL IMPROVEMENTS (ph6.4)
        // ═══════════════════════════════════════════
        _strings["Terminal.Split"] = "Split terminal";
        _strings["Terminal.Pwsh"] = "PowerShell Core (pwsh)";

        // ═══════════════════════════════════════════
        // DISK INFO
        // ═══════════════════════════════════════════
        _strings["Disk.Title"] = "Disk Information";
        _strings["Disk.Label"] = "Label";
        _strings["Disk.Free"] = "Free";
        _strings["Disk.Used"] = "Used";
        _strings["Disk.Total"] = "Total";
        _strings["Disk.FS"] = "File system";
        _strings["Disk.Type"] = "Type";
        _strings["Disk.NA"] = "N/A";
        _strings["Disk.NotReady"] = "Not ready";
        _strings["Disk.Type.Fixed"] = "Fixed";
        _strings["Disk.Type.Removable"] = "Removable";
        _strings["Disk.Type.CDRom"] = "CD-ROM";
        _strings["Disk.Type.Network"] = "Network";
        _strings["Disk.Type.Ram"] = "RAM disk";
        _strings["Disk.Type.Unknown"] = "Unknown";

        // ═══════════════════════════════════════════
        // MACROS (ph8.2)
        // ═══════════════════════════════════════════
        _strings["Menu.Tools.Macros"] = "Macros…";
        _strings["Macro.Title"] = "Macro Manager";
        _strings["Macro.Name"] = "Name:";
        _strings["Macro.Description"] = "Description:";
        _strings["Macro.Hotkey"] = "Hotkey:";
        _strings["Macro.Enabled"] = "Enabled";
        _strings["Macro.Steps"] = "Steps:";
        _strings["Macro.AddStep"] = "Add step";
        _strings["Macro.RemoveStep"] = "Remove step";
        _strings["Macro.MoveUp"] = "Move up";
        _strings["Macro.MoveDown"] = "Move down";
        _strings["Macro.Execute"] = "Execute";
        _strings["Macro.Save"] = "Save";
        _strings["Macro.Cancel"] = "Cancel";
        _strings["Macro.Add"] = "Add macro";
        _strings["Macro.Delete"] = "Delete macro";
        _strings["Macro.DeleteConfirm"] = "Delete macro '{0}'?";
        _strings["Macro.ExecuteDone"] = "Macro executed: {0} steps";
        _strings["Macro.ExecuteError"] = "Macro error: {0}";
        _strings["Macro.NoCommand"] = "Command not found: {0}";
        _strings["Macro.NoSteps"] = "Macro has no steps";
        _strings["Macro.NameEmpty"] = "Macro name cannot be empty";
        _strings["Macro.ColCommand"] = "Command";
        _strings["Macro.ColParams"] = "Parameters";
        _strings["Macro.ColOrder"] = "#";
        _strings["Macro.StepCommandPrompt"] = "Command name (e.g. app.copy):";
        _strings["Macro.StepParamsPrompt"] = "Parameters (key=value;key2=value2):";
        _strings["Macro.Saved"] = "Macros saved";

        // ═══════════════════════════════════════════
        // PLUGINS (ph8.3)
        // ═══════════════════════════════════════════
        _strings["Plugin.Title"] = "Plugin Manager";
        _strings["Plugin.Name"] = "Name";
        _strings["Plugin.Version"] = "Version";
        _strings["Plugin.Author"] = "Author";
        _strings["Plugin.Description"] = "Description";
        _strings["Plugin.Enabled"] = "Enabled";
        _strings["Plugin.Disabled"] = "Disabled";
        _strings["Plugin.Enable"] = "Enable";
        _strings["Plugin.Disable"] = "Disable";
        _strings["Plugin.Reload"] = "Reload";
        _strings["Plugin.Refresh"] = "Refresh";
        _strings["Plugin.OpenFolder"] = "Open plugins folder";
        _strings["Plugin.NoPlugins"] = "No plugins found. Place DLL files in the plugins folder.";
        _strings["Plugin.LoadError"] = "Failed to load plugin: {0}";
        _strings["Plugin.Initialized"] = "Plugin initialized: {0}";
        _strings["Menu.Tools.Plugins"] = "Plugins…";

        // ═══════════════════════════════════════════
        // CLOUD STORAGE (ph8.4)
        // ═══════════════════════════════════════════
        _strings["Menu.Tools.CloudStorage"] = "Cloud Storage…";
        _strings["Cloud.Title"] = "Cloud Storage";
        _strings["Cloud.Profiles"] = "Profiles";
        _strings["Cloud.AddProfile"] = "Add profile";
        _strings["Cloud.EditProfile"] = "Edit profile";
        _strings["Cloud.DeleteProfile"] = "Delete profile";
        _strings["Cloud.Connect"] = "Connect";
        _strings["Cloud.Disconnect"] = "Disconnect";
        _strings["Cloud.Refresh"] = "Refresh";
        _strings["Cloud.Upload"] = "Upload";
        _strings["Cloud.Download"] = "Download";
        _strings["Cloud.Provider"] = "Provider";
        _strings["Cloud.AccessKey"] = "Access Key";
        _strings["Cloud.SecretKey"] = "Secret Key";
        _strings["Cloud.Region"] = "Region";
        _strings["Cloud.Bucket"] = "Bucket/Container";
        _strings["Cloud.ConnectionString"] = "Connection String";
        _strings["Cloud.Connected"] = "Connected";
        _strings["Cloud.Disconnected"] = "Disconnected";
        _strings["Cloud.Connecting"] = "Connecting...";
        _strings["Cloud.Error"] = "Connection error: {0}";
        _strings["Cloud.NoProfiles"] = "No cloud profiles. Add a profile to connect.";
        _strings["Cloud.Loading"] = "Loading...";
        _strings["Cloud.YandexDisk"] = "Yandex Disk";
        _strings["Cloud.YandexDisk.Token"] = "OAuth Token";
        _strings["Cloud.YandexDisk.GetToken"] = "Get token";
        _strings["Cloud.YandexDisk.TokenHint"] = "Get token at https://oauth.yandex.ru/";
        _strings["Cloud.YandexDisk.RootPath"] = "Root path";

        // ═══════════════════════════════════════════
        // CLOUD STORAGE — NEXTCLOUD (ph9.2)
        // ═══════════════════════════════════════════
        _strings["Cloud.NextCloud"] = "NextCloud";
        _strings["Cloud.NextCloud.ServerUrl"] = "Server URL";
        _strings["Cloud.NextCloud.Username"] = "Username";
        _strings["Cloud.NextCloud.Password"] = "Password";
        _strings["Cloud.NextCloud.Hint"] = "Use App Password for better security";
        _strings["Cloud.NextCloud.RootPath"] = "Root path";

        // ═══════════════════════════════════════════
        // CLOUD STORAGE — GOOGLE DRIVE (ph9.3)
        // ═══════════════════════════════════════════
        _strings["Cloud.GDrive"] = "Google Drive";
        _strings["Cloud.GDrive.Authorize"] = "Authorize";
        _strings["Cloud.GDrive.ClientId"] = "Client ID";
        _strings["Cloud.GDrive.ClientSecret"] = "Client Secret";
        _strings["Cloud.GDrive.RefreshToken"] = "Refresh Token";
        _strings["Cloud.GDrive.Authorized"] = "Authorized";
        _strings["Cloud.GDrive.NotAuthorized"] = "Not authorized";
        _strings["Cloud.GDrive.SaveCredentials"] = "Save";
        _strings["Cloud.GDrive.GetCredentials"] = "Get credentials at Google Cloud Console";
        _strings["Cloud.GDrive.Authorizing"] = "Authorizing...";
        _strings["Cloud.GDrive.OpenBrowser"] = "Opening browser for authorization...";
        _strings["Cloud.GDrive.ExchangeCode"] = "Exchanging code for Refresh Token...";
        _strings["Cloud.GDrive.AuthSuccess"] = "Authorized successfully";
        _strings["Cloud.GDrive.AuthCancelled"] = "Authorization cancelled";
        _strings["Cloud.GDrive.AuthError"] = "Authorization error: {0}";
        // ═══════════════════════════════════════════
        // CHECKSUM (ph1.2)
        // ═══════════════════════════════════════════
        _strings["Checksum.Ready"] = "Ready";
        _strings["Checksum.NoFiles"] = "No files selected";
        _strings["Checksum.Calculating"] = "Calculating {0}…";
        _strings["Checksum.DoneExport"] = "Done, exported {0} files → {1}";
        _strings["Checksum.Done"] = "Done: {0} files";
        _strings["Checksum.SumNotFound"] = "Sum file not found";
        _strings["Checksum.Verifying"] = "Verifying via {0}…";
        _strings["Checksum.AllMatch"] = "All {0} match";
        _strings["Checksum.Mismatches"] = "Mismatches: {0} of {1}";

        // ═══════════════════════════════════════════
        // SFTP EXTENDED STATUS (ph4.2)
        // ═══════════════════════════════════════════
        _strings["Sftp.SelectProfileAndConnect"] = "Select a profile and connect";
        _strings["Sftp.ProfileNotSelected"] = "Profile not selected";
        _strings["Sftp.CheckingConnection"] = "Checking connection…";
        _strings["Sftp.ConnectFailed"] = "Failed to connect to {0}";
        _strings["Sftp.LoadingPath"] = "Loading {0}";
        _strings["Sftp.ItemsIn"] = "{0} items in {1}";
        _strings["Sftp.ItemNotSelected"] = "Item not selected";
        _strings["Sftp.DirDownloadNotSupported"] = "Downloading folders is not supported";
        _strings["Sftp.Downloading"] = "Downloading {0}";
        _strings["Sftp.DownloadingBytes"] = "Downloading {0}: {1} bytes";
        _strings["Sftp.Downloaded"] = "Downloaded: {0}";
        _strings["Sftp.UploadTitle"] = "Upload to server";
        _strings["Sftp.UploadLocalFile"] = "Local file name:";
        _strings["Sftp.SpecifyLocalFile"] = "Specify local file";
        _strings["Sftp.FileNotFound"] = "File not found: {0}";
        _strings["Sftp.Uploading"] = "Uploading {0}";
        _strings["Sftp.UploadingBytes"] = "Uploading {0}: {1} bytes";
        _strings["Sftp.MkdirTitle"] = "New folder";
        _strings["Sftp.FolderName"] = "Folder name:";
        _strings["Sftp.Creating"] = "Creating {0}";
        _strings["Sftp.ConfirmDelete"] = "Delete \"{0}\" on server?";
        _strings["Sftp.DeleteTitle"] = "Confirmation";
        _strings["Sftp.Deleting"] = "Deleting {0}";
        _strings["Sftp.Renaming"] = "Renaming {0}";

        // ═══════════════════════════════════════════
        // SSH EXTENDED STATUS
        // ═══════════════════════════════════════════
        _strings["Ssh.NameHostRequired"] = "Name and host are required";
        _strings["Ssh.ProfileSaved"] = "Profile saved";
        _strings["Ssh.NoProfileSelected"] = "Select a profile to check";
        _strings["Ssh.Checking"] = "Checking connection...";
        _strings["Ssh.Reachable"] = "Connected to {0} successfully";
        _strings["Ssh.Unreachable"] = "Cannot connect to {0}";

        // ═══════════════════════════════════════════
        // GIT EXTENDED STATUS
        // ═══════════════════════════════════════════
        _strings["Git.DiffTitle"] = "Diff";
        _strings["Git.GitStatusError"] = "Error getting git status";
        _strings["Git.EnterCommitMessage"] = "Enter commit message";
        _strings["Git.CommitCreated"] = "Commit created";
        _strings["Git.CommitAmended"] = "Commit amended";
        _strings["Git.BranchCreated"] = "Branch {0} created";
        _strings["Git.CannotDeleteCurrentBranch"] = "Cannot delete current branch";
        _strings["Git.ConfirmDeleteBranch"] = "Delete branch \"{0}\"?";
        _strings["Git.BranchDeleted"] = "Branch {0} deleted";
        _strings["Git.BranchTitle"] = "Git";

        _strings["Cloud.GDrive.NeedCredentials"] = "Enter Client ID and Client Secret first";

        // ═══════════════════════════════════════════
        // CLOUD STORAGE — UI STRINGS (ph8.4 extended)
        // ═══════════════════════════════════════════
        _strings["Cloud.SelectProfile"] = "Select a cloud storage profile";
        _strings["Cloud.Reconnect"] = "Reconnect";
        _strings["Cloud.DisconnectBtn"] = "Disconnect";
        _strings["Cloud.DisconnectTip"] = "Disconnect from cloud storage";
        _strings["Cloud.NewFolder"] = "Folder";
        _strings["Cloud.Up"] = "Up";
        _strings["Cloud.Path"] = "Path:";
        _strings["Cloud.LoadingFiles"] = "Loading {0}...";
        _strings["Cloud.LoadingOverlay"] = "Loading...";
        _strings["Cloud.ProfileNotSelected"] = "Profile not selected";
        _strings["Cloud.ConnectingStatus"] = "Connecting...";
        _strings["Cloud.ConnectedTo"] = "Connected: {0}";
        _strings["Cloud.Cancelled"] = "Cancelled";
        _strings["Cloud.DisconnectedStatus"] = "Disconnected";
        _strings["Cloud.ItemNotSelected"] = "Item not selected";
        _strings["Cloud.ItemNotSelectedOrDir"] = "Item not selected or it is a folder";
        _strings["Cloud.NoConnectionStatus"] = "No connection";
        _strings["Cloud.DownloadingFile"] = "Downloading {0}...";
        _strings["Cloud.Downloaded"] = "Downloaded: {0}";
        _strings["Cloud.UploadingFile"] = "Uploading {0}...";
        _strings["Cloud.Uploaded"] = "Uploaded: {0}";
        _strings["Cloud.UploadTitle"] = "Upload to cloud";
        _strings["Cloud.UploadLocalFile"] = "Local file name:";
        _strings["Cloud.SpecifyFileName"] = "Specify file name";
        _strings["Cloud.FileNotFound"] = "File not found: {0}";
        _strings["Cloud.DirDownloadNotSupported"] = "Downloading folders is not supported";
        _strings["Cloud.CreateFolderTitle"] = "New folder";
        _strings["Cloud.FolderName"] = "Folder name:";
        _strings["Cloud.Creating"] = "Creating {0}...";
        _strings["Cloud.ConfirmDelete"] = "Delete \"{0}\" in cloud?";
        _strings["Cloud.ConfirmDeleteTitle"] = "Confirmation";
        _strings["Cloud.DeletingFile"] = "Deleting {0}...";
        _strings["Cloud.RenamingFile"] = "Renaming {0}...";
        _strings["Cloud.RenameTitle"] = "Rename";
        _strings["Cloud.RenameName"] = "New name:";
        _strings["Cloud.DeleteProfileConfirm"] = "Delete profile \"{0}\"?";
        _strings["Cloud.DeleteProfileTitle"] = "Confirmation";
        _strings["Cloud.GDrive.SaveCredentialsTip"] = "Save credentials to profile";
        _strings["Cloud.GDrive.SelectProfileFirst"] = "Select a Google Drive profile first";
        _strings["Cloud.GDrive.CredentialsSaved"] = "Google Drive credentials saved";
        _strings["Cloud.GDrive.AuthorizeInBrowser"] = "Authorize in browser";
        _strings["Cloud.GDrive.GetCredentialsAt"] = "Get credentials at";
        _strings["Cloud.GDrive.OAuthTitle"] = "Google Drive OAuth2";
        _strings["Cloud.GDrive.GoogleCloudConsole"] = "Google Cloud Console";
        _strings["Cloud.AuthCancelled"] = "Authorization cancelled";
        _strings["Cloud.AuthCancelledStatus"] = "Authorization cancelled";
        _strings["Cloud.OpenCtx"] = "Open";
        _strings["Cloud.DownloadCtx"] = "Download";
        _strings["Cloud.RenameCtx"] = "Rename";
        _strings["Cloud.DeleteCtx"] = "Delete";
        _strings["Cloud.MakeDirCtx"] = "Create folder";

        // ═══════════════════════════════════════════
        // COPY/MOVE DIALOG (ph9.5)
        // ═══════════════════════════════════════════
        _strings["CopyMove.Title.Copy"] = "Copy";
        _strings["CopyMove.Title.Move"] = "Move";
        _strings["CopyMove.Source"] = "Source:";
        _strings["CopyMove.Destination"] = "Destination:";
        _strings["CopyMove.Browse"] = "Browse";
        _strings["CopyMove.FileCount"] = "Files:";
        _strings["CopyMove.TotalSize"] = "Total size:";
        _strings["CopyMove.OverwritePolicy"] = "If file exists:";
        _strings["CopyMove.CopyAttributes"] = "Copy attributes";
        _strings["CopyMove.CopyTimestamps"] = "Copy timestamps";
        _strings["CopyMove.CopyNtfsPermissions"] = "Copy NTFS permissions (requires admin)";
        _strings["CopyMove.SelfCopyWarning"] = "Warning: source and destination are the same location";
        _strings["CopyMove.AddToQueue"] = "Add to queue";
        _strings["CopyMove.OK"] = "OK";
        _strings["CopyMove.Cancel"] = "Cancel";
        _strings["CopyMove.SelectFolder"] = "Select destination folder";

        // Перечисление OverwritePolicy / OverwritePolicy enum values
        _strings["OverwritePolicy.Overwrite"] = "Overwrite";
        _strings["OverwritePolicy.Skip"] = "Skip";
        _strings["OverwritePolicy.OverwriteOlder"] = "Overwrite if older";
        _strings["OverwritePolicy.OverwriteSmaller"] = "Overwrite if smaller";
        _strings["OverwritePolicy.AutoRename"] = "Auto-rename";
        _strings["OverwritePolicy.Ask"] = "Ask";

        // ═══════════════════════════════════════════
        // ENHANCED OVERWRITE DIALOG (ph9.5)
        // ═══════════════════════════════════════════
        _strings["OpDlg.Overwrite.OverwriteAll"] = "Overwrite All";
        _strings["OpDlg.Overwrite.SkipAll"] = "Skip All";
        _strings["OpDlg.Overwrite.SizeInfo"] = "Size: {0}";
        _strings["OpDlg.Overwrite.DateInfo"] = "Modified: {0}";

        // ═══════════════════════════════════════════
        // ENHANCED PROGRESS DIALOG (ph9.5)
        // ═══════════════════════════════════════════
        _strings["OpDlg.Skip"] = "Skip";
        _strings["OpDlg.Pause"] = "Pause";
        _strings["OpDlg.Resume"] = "Resume";

        // ═══════════════════════════════════════════
        // FILE OPERATIONS SETTINGS (ph9.5)
        // ═══════════════════════════════════════════
        _strings["Settings.FileOps"] = "File Operations";
        _strings["Settings.DefaultOverwrite"] = "Default overwrite policy:";
        _strings["Settings.DefaultOverwriteDesc"] = "Policy when destination file exists";
        _strings["Settings.OverwriteAsk"] = "Ask";
        _strings["Settings.OverwriteAlways"] = "Always overwrite";
        _strings["Settings.OverwriteNever"] = "Never overwrite";
        _strings["Settings.OverwriteOlder"] = "Overwrite if older";
        _strings["Settings.OverwriteSmaller"] = "Overwrite if smaller";
        _strings["Settings.OverwriteAutoRename"] = "Auto-rename";
        _strings["Settings.BufferSize"] = "Buffer size (KB):";
        _strings["Settings.BufferSizeDesc"] = "Copy buffer size in kilobytes";
        _strings["Settings.CopyAttributesCheck"] = "Copy file attributes";
        _strings["Settings.CopyAttributesDesc"] = "Preserve file attributes when copying";
        _strings["Settings.CopyTimestampsCheck"] = "Copy timestamps";
        _strings["Settings.CopyTimestampsDesc"] = "Preserve file timestamps when copying";
        _strings["Settings.ReserveSpace"] = "Reserve disk space";
        _strings["Settings.ReserveSpaceDesc"] = "Pre-allocate disk space before copying";
        _strings["Settings.CopyNtfsPermissionsCheck"] = "Copy NTFS permissions";
        _strings["Settings.CopyNtfsPermissionsDesc"] = "Copy NTFS ACL (requires administrator rights)";

        // ═══════════════════════════════════════════
        // OPERATION QUEUE DESCRIPTIONS (ph5.2)
        // ═══════════════════════════════════════════
        _strings["Operation.Copying"] = "Copying";
        _strings["Operation.Moving"] = "Moving";
        _strings["Operation.Deleting"] = "Deleting";

        // ═══════════════════════════════════════════
        // MISSING DIALOG KEY
        // ═══════════════════════════════════════════
        _strings["Dialog.OverwriteConfirm"] = "\"{0}\" already exists. Overwrite?";

        // ═══════════════════════════════════════════
        // XAML FIXES
        // ═══════════════════════════════════════════
        _strings["Menu.Tools.CloudTab"] = "Cloud";
        _strings["Editor.Image.Fit"] = "Fit";
        _strings["Columns.Px"] = "px";

        // ═══════════════════════════════════════════
        // GIT EXTENDED STATUS (ph10)
        // ═══════════════════════════════════════════
        _strings["Git.StatusFormat"] = "{0} ↑{1} ↓{2} {3} changes";
        _strings["Git.DiscardConfirm"] = "Discard changes to \"{0}\"?";
        _strings["Git.PushDone"] = "Push done";
        _strings["Git.PullDone"] = "Pull done";
        _strings["Git.FetchDone"] = "Fetch done";
        _strings["Git.StashApplied"] = "Stash applied";
        _strings["Git.SwitchedTo"] = "Switched to {0}";
        _strings["Git.ErrorPrefix"] = "Error: {0}";
        _strings["Git.NewBranchPrompt"] = "New branch";
        _strings["Git.BranchNamePrompt"] = "Branch name:";

        // ═══════════════════════════════════════════
        // DOCKER STATUS (ph10)
        // ═══════════════════════════════════════════
        _strings["Docker.StatusFormat"] = "Containers: {0}, images: {1}";

        // ═══════════════════════════════════════════
        // DIFF COMMAND (ph10)
        // ═══════════════════════════════════════════
        _strings["Diff.SelectTwoFiles"] = "Select two files to compare (two in the active panel or one in each).";
        _strings["Diff.DirCompareNotSupported"] = "Directory comparison is not yet supported — select two files.";
        _strings["Diff.Comparing"] = "Comparing: {0} ⇄ {1}";
        _strings["Diff.CompareError"] = "Compare error: {0}";

        // ═══════════════════════════════════════════
        // WIPE (ph10)
        // ═══════════════════════════════════════════
        _strings["Wipe.Confirm"] = "Securely delete (Wipe) {0} item(s)? Data cannot be recovered.";
        _strings["Wipe.Title"] = "Secure delete";
        _strings["Wipe.Done"] = "Wipe: {0} item(s) wiped";
        _strings["Wipe.DoneErrors"] = "Wipe: {0} wiped, errors: {1}";
        _strings["Wipe.Cancelled"] = "Wipe: cancelled";

        // ═══════════════════════════════════════════
        // SSH PUBLISH STATUS (ph10)
        // ═══════════════════════════════════════════
        _strings["Ssh.Published"] = "Published: {0}";

        // ═══════════════════════════════════════════
        // SEARCH DIALOG VM STATUS (ph10)
        // ═══════════════════════════════════════════
        _strings["Search.CriteriaError"] = "Criteria error: {0}";
        _strings["Search.SearchingStatus"] = "Searching…";
        _strings["Search.FoundScanned"] = "Found: {0} (scanned {1})";
        _strings["Search.NothingFoundStatus"] = "Nothing found";
        _strings["Search.CancelledStatus"] = "Cancelled";

        // ═══════════════════════════════════════════
        // HOTKEY CAPTURE (ph10)
        // ═══════════════════════════════════════════
        _strings["Hotkey.CapturingFor"] = "Press a key for \"{0}\"...";

        // ═══════════════════════════════════════════
        // QUICK FILTER/SEARCH STATUS (ph10)
        // ═══════════════════════════════════════════
        _strings["Quick.NoMatch"] = "Search: \"{0}\" — no matches";
        _strings["Quick.MatchStatus"] = "Search: {0}/{1} \"{2}\"";
        _strings["Quick.FilterStatus"] = "Filter: {0}/{1}";

        // ═══════════════════════════════════════════
        // PANEL STATUS (ph10)
        // ═══════════════════════════════════════════
        _strings["Status.FilesDirs"] = "{0} files, {1} folders";

        // ═══════════════════════════════════════════
        // OPERATION DIALOG FORMAT (ph10)
        // ═══════════════════════════════════════════
        _strings["OpDlg.Speed.Bs"] = "B/s";
        _strings["OpDlg.Speed.KBs"] = "KB/s";
        _strings["OpDlg.Speed.MBs"] = "MB/s";
        _strings["OpDlg.Speed.GBs"] = "GB/s";
        _strings["OpDlg.ETA.LessSec"] = "< 1 sec";
        _strings["OpDlg.ETA.Sec"] = "~{0} sec";
        _strings["OpDlg.ETA.MinSec"] = "~{0} min {1} sec";
        _strings["OpDlg.ETA.Min"] = "~{0} min";

        // ═══════════════════════════════════════════
        // FORMAT BYTES (ph10)
        // ═══════════════════════════════════════════
        _strings["Format.B"] = "B";
        _strings["Format.KB"] = "KB";
        _strings["Format.MB"] = "MB";
        _strings["Format.GB"] = "GB";

        // ═══════════════════════════════════════════
        // CLOUD GDRIVE STATUS (ph10)
        // ═══════════════════════════════════════════
        _strings["Cloud.GDrive.AuthorizedStatus"] = "Authorized";
        _strings["Cloud.GDrive.NotAuthorizedStatus"] = "Not authorized";

        // ═══════════════════════════════════════════
        // ERROR MESSAGES (ph10)
        // ═══════════════════════════════════════════
        _strings["Error.OpenEditor"] = "Failed to open editor: {0}";
        _strings["Error.OpenViewer"] = "Failed to open viewer: {0}";
        _strings["Error.OpenDuplicates"] = "Failed to open duplicates search: {0}";

        // ═══════════════════════════════════════════
        // MISSING XAML KEYS (ph11)
        // ═══════════════════════════════════════════
        _strings["Dialog.Close"] = "Close";
        _strings["DirTree.Title"] = "Directory Tree";
        _strings["DirTree.Status.Hint"] = "Double-click to select";
        _strings["Search.BackToFolder"] = "Back to folder";
        _strings["Search.ShowInPanel"] = "Show results in panel";
        _strings["Search.ShowInPanel.Tip"] = "Display search results in the active file panel";
        _strings["Tab.Git"] = "Git";
        _strings["Tab.Docker"] = "Docker";
        _strings["Tab.Ssh"] = "SSH";
        _strings["Tab.Sftp"] = "SFTP";
        _strings["Terminal.NewCmd"] = "+ CMD";
        _strings["Terminal.NewPs"] = "+ PowerShell";
        _strings["Terminal.NewPwsh"] = "+ pwsh";
        _strings["Archive.FilesForArchive"] = "Files for archive: {0}";
        _strings["Archive.EntriesInArchive"] = "Entries in archive: {0}";
        _strings["Bookmark.Count"] = "{0} bookmark(s)";

        // ═══════════════════════════════════════════
        // CREATE LINK WINDOW / ОКНО СОЗДАНИЯ ССЫЛОК
        // ═══════════════════════════════════════════
        _strings["Link.Title.Symlink"] = "Create Symbolic Link";
        _strings["Link.Title.Hardlink"] = "Create Hard Link";
        _strings["Link.Target"] = "Target:";
        _strings["Link.Name"] = "Link name:";
        _strings["Link.Path"] = "Link path:";
        _strings["Link.Type"] = "Type:";
        _strings["Link.Type.Symlink"] = "Symbolic Link";
        _strings["Link.Type.Hardlink"] = "Hard Link";
        _strings["Link.Create"] = "Create";
        _strings["Link.Cancel"] = "Cancel";
        _strings["Link.Success"] = "Link created: {0}";
        _strings["Link.Error"] = "Error creating link: {0}";
        _strings["Link.AdminRequired"] = "Administrator rights required for symbolic links";
        _strings["Link.NameEmpty"] = "Link name cannot be empty";
        _strings["Link.AlreadyExists"] = "Link already exists: {0}";
        _strings["Link.InvalidChars"] = "Link name contains invalid characters";
        _strings["Link.MultipleTitle"] = "Create Links";
        _strings["Link.MultipleFiles"] = "Files:";
        _strings["Link.Done"] = "Links created: {0}, errors: {1}";

        // ═══════════════════════════════════════════
        // FILE PROPERTIES / СВОЙСТВА ФАЙЛА
        // ═══════════════════════════════════════════
        _strings["Props.Title"] = "Properties";
        _strings["Props.Type"] = "Type:";
        _strings["Props.Size"] = "Size:";
        _strings["Props.Files"] = "Contains files:";
        _strings["Props.Folders"] = "Contains folders:";
        _strings["Props.Symlinks"] = "Symlinks:";
        _strings["Props.Created"] = "Created:";
        _strings["Props.Modified"] = "Modified:";
        _strings["Props.Accessed"] = "Accessed:";
        _strings["Props.Attributes"] = "Attributes:";
        _strings["Props.Folder"] = "Folder";
        _strings["Props.File"] = "File";
        _strings["Props.Computing"] = "Computing folder size...";
        _strings["Props.Processed"] = "Processed items: {0}";
        _strings["Props.DoneTime"] = "Done in {0} sec";
        _strings["Props.Error"] = "Error: {0}";
        _strings["Props.Done"] = "Done";
        _strings["Props.CopyPath"] = "Copy Path";
        _strings["Props.OpenFolder"] = "Open Folder";
        _strings["Props.OpenFile"] = "Open";
        _strings["Props.Copied"] = "Path copied";
        _strings["Props.Attr.ReadOnly"] = "R";
        _strings["Props.Attr.Hidden"] = "H";
        _strings["Props.Attr.System"] = "S";
        _strings["Props.Attr.Archive"] = "A";
        _strings["Props.Attr.ReadOnly.Full"] = "Read-only";
        _strings["Props.Attr.Hidden.Full"] = "Hidden";
        _strings["Props.Attr.System.Full"] = "System";
        _strings["Props.Attr.Archive.Full"] = "Archive";
        _strings["Props.EditAttributes"] = "Edit attributes";
        _strings["Props.EditTimestamps"] = "Edit timestamps";
        _strings["Attr.ErrSuffix"] = "errors";

        // ═══════════════════════════════════════════
        // MAIN MENU / COMMANDS
        // ═══════════════════════════════════════════
        _strings["Main.Properties"] = "Properties…";
        _strings["Main.Wipe"] = "Wipe…";
        _strings["Main.CompareFiles"] = "Compare files…";

        // ═══════════════════════════════════════════
        // MISSING ENGLISH FALLBACKS (sync with ru.lng)
        // ═══════════════════════════════════════════

        // Menu
        _strings["Menu.File.Info"] = "Info";
        _strings["Menu.File.Properties"] = "Properties…";
        _strings["Menu.Tools.CloudStorage"] = "Cloud Storage…";
        _strings["Menu.Tools.Macros"] = "Macros…";
        _strings["Menu.Tools.Plugins"] = "Plugins…";

        // Diff
        _strings["Diff.SideBySide"] = "Side by Side";
        _strings["Diff.Inline"] = "Inline";
        _strings["Diff.Legend.Added"] = "added";
        _strings["Diff.Legend.Removed"] = "removed";
        _strings["Diff.Legend.Modified"] = "modified";
        _strings["Diff.PrevDiff"] = "Previous diff";
        _strings["Diff.NextDiff"] = "Next diff";
        _strings["Diff.BinaryCompareError"] = "Compare error: {0}";

        // Hex
        _strings["Hex.PrevDiff"] = "◀ Prev";
        _strings["Hex.NextDiff"] = "Next ▶";

        // Editor
        _strings["Editor.SaveErrTitle"] = "Error";

        // Editor Image
        _strings["Editor.Image.ZoomIn"] = "Zoom In";
        _strings["Editor.Image.ZoomOut"] = "Zoom Out";
        _strings["Editor.Image.ZoomReset"] = "Actual Size (1:1)";
        _strings["Editor.Image.ZoomFit"] = "Fit to Window";
        _strings["Editor.Image.RotateLeft"] = "Rotate Left 90°";
        _strings["Editor.Image.RotateRight"] = "Rotate Right 90°";
        _strings["Editor.Image.Slideshow"] = "Slideshow";
        _strings["Editor.Image.SlideshowStop"] = "Stop Slideshow";
        _strings["Editor.Image.Size"] = "{0} × {1} px";

        // Archive tips
        _strings["Archive.Tip.Minimize"] = "Minimize";
        _strings["Archive.Tip.Maximize"] = "Maximize";
        _strings["Archive.Tip.Close"] = "Close";
        _strings["Archive.BrowseOutput"] = "Browse output path";
        _strings["Archive.BrowseExtractTo"] = "Browse destination folder";
        _strings["Archive.SelectOutputPath"] = "Select output archive path";
        _strings["Archive.SelectExtractPath"] = "Select extraction destination";
        _strings["Archive.ArchiveFiles"] = "Archive files";
        _strings["Archive.AllFiles"] = "All files";

        // Attributes
        _strings["Attr.HardlinkMenu"] = "Create hardlink…";
        _strings["Attr.SymlinkMenu"] = "Create symlink…";
        _strings["Attr.AccessDenied"] = "access denied";

        // Bookmarks
        _strings["Bookmark.Tip.Up"] = "Up";
        _strings["Bookmark.Tip.Down"] = "Down";

        // Dialog
        _strings["Dialog.Message"] = "Message";

        // Directory tree
        _strings["DirTree.Tip.Close"] = "Close (Esc)";

        // Duplicates
        _strings["Dup.FileCount"] = "{0} file(s)";
        _strings["Dup.Ready"] = "Ready";
        _strings["Dup.Cancelled"] = "Search cancelled";
        _strings["Dup.Criterion.Size"] = "By size";
        _strings["Dup.Criterion.SizeName"] = "By size and name";
        _strings["Dup.Criterion.Hash"] = "By hash (SHA-256)";
        _strings["Dup.Criterion.Content"] = "By content (byte-by-byte)";

        // Error messages
        _strings["Error.Title"] = "Error";
        _strings["Error.OpenSync"] = "Error opening sync: {0}";
        _strings["Error.OpenQueue"] = "Error opening queue: {0}";
        _strings["Error.OpenBookmarks"] = "Error opening bookmarks: {0}";
        _strings["Error.OpenArchive"] = "Error opening archive: {0}";
        _strings["Error.OpenTree"] = "Error opening directory tree: {0}";
        _strings["Error.OpenColumnSettings"] = "Error opening column settings: {0}";
        _strings["Error.OpenFile"] = "Failed to open file: {0}";
        _strings["Error.SearchFile"] = "Failed to open file: {0}";

        // FileInfo
        _strings["FileInfo.Name"] = "Name: {0}";
        _strings["FileInfo.Path"] = "Path: {0}";
        _strings["FileInfo.Type"] = "Type: {0}";
        _strings["FileInfo.TypeDir"] = "Directory";
        _strings["FileInfo.TypeFile"] = "File";
        _strings["FileInfo.Size"] = "Size: {0}";
        _strings["FileInfo.Modified"] = "Modified: {0}";
        _strings["FileInfo.Created"] = "Created: {0}";
        _strings["FileInfo.Attributes"] = "Attributes: {0}";

        // FilePanel
        _strings["FilePanel.Tip.AutoRefresh"] = "Auto-refresh folder";
        _strings["FilePanel.SortBy"] = "Sort by: {0}";

        // Git tips
        _strings["Git.Tip.Ahead"] = "Ahead of remote branch";
        _strings["Git.Tip.Behind"] = "Behind remote branch";

        // Main window
        _strings["Main.Commands"] = "commands";
        _strings["Main.Panel"] = "panel";
        _strings["Main.Tools"] = "Tools";
        _strings["Main.Terminal"] = "Terminal";
        _strings["Main.Tip.Commands"] = "Commands (F1)";
        _strings["Main.Tip.Minimize"] = "Minimize (Esc)";
        _strings["Main.Tip.TerminalCmd"] = "Open Windows Command terminal";
        _strings["Main.Tip.TerminalPs"] = "Open PowerShell terminal";
        _strings["Main.Tip.TerminalPwsh"] = "Open PowerShell Core (pwsh) terminal";
        _strings["Main.Tip.TerminalClose"] = "Close (Shift+F10)";

        // Sync
        _strings["Sync.Ready"] = "Ready";

        // SSH
        _strings["Ssh.Tip.KeyFile"] = "Select private key file";

        // Quick Commands
        _strings["Quick.CloudStorage"] = "Cloud Storage: Panel";
        _strings["Quick.CloudStorageDesc"] = "Show cloud storage browser";
        _strings["Quick.CloudStorageResult"] = "Cloud Storage opened";

        // Settings
        _strings["Settings.PanelFont"] = "Panel font";
        _strings["Settings.PanelFontDesc"] = "Font family for file list panels";
        _strings["Settings.PanelFontSize"] = "Panel font size";
        _strings["Settings.PanelFontSizeDesc"] = "Font size in points for file list";
        _strings["Settings.CopyNtfsPermissionsCheck"] = "Copy NTFS permissions";
        _strings["Settings.CopyNtfsPermissionsDesc"] = "Copy NTFS ACL (requires administrator rights)";

        // Operation Queue
        _strings["OpQueue.Status.Running"] = "Running";
        _strings["OpQueue.Status.Completed"] = "Done";
        _strings["OpQueue.Status.Failed"] = "Failed";
        _strings["OpQueue.Status.Cancelled"] = "Cancelled";
        _strings["OpQueue.Status.Queued"] = "Queued";
        _strings["OpQueue.Status.Paused"] = "Paused";
        _strings["OpQueue.Title.Active"] = "Queue ({0} active, {1} pending)";
        _strings["OpQueue.Title.Completed"] = "Queue completed";
        _strings["OpQueue.Title.Operations"] = "Operations ({0})";
        _strings["OpQueue.Pause"] = "Pause";
        _strings["OpQueue.Resume"] = "Resume";
        _strings["OpQueue.NoActive"] = "Waiting for operations…";
        _strings["OpQueue.NoOperations"] = "No operations in queue";
        _strings["OpQueue.CloseConfirm"] = "Close window? Operations will continue in background.\n\nCancel all operations?";
        _strings["OpQueue.CloseTitle"] = "Close Queue";
        _strings["OpQueue.Col.Type"] = "Type";
        _strings["OpQueue.Col.Source"] = "Source";
        _strings["OpQueue.Col.Destination"] = "Destination";
        _strings["OpQueue.Operation.Copy"] = "Copy";
        _strings["OpQueue.Operation.Move"] = "Move";

        // Operation Dialog
        _strings["OpDlg.CurrentFile"] = "File:";
        _strings["OpDlg.Total"] = "Total:";
        _strings["OpDlg.Skip"] = "Skip";
        _strings["OpDlg.Pause"] = "Pause";
        _strings["OpDlg.Resume"] = "Resume";
        _strings["OpDlg.Overwrite.OverwriteAll"] = "Overwrite All";
        _strings["OpDlg.Overwrite.SkipAll"] = "Skip All";
        _strings["OpDlg.Overwrite.SizeInfo"] = "Size: {0}";
        _strings["OpDlg.Overwrite.DateInfo"] = "Modified: {0}";

        // Link window
        _strings["Link.Title.Symlink"] = "Create Symbolic Link";
        _strings["Link.Title.Hardlink"] = "Create Hard Link";
        _strings["Link.Target"] = "Target:";
        _strings["Link.Name"] = "Link name:";
        _strings["Link.Path"] = "Link path:";
        _strings["Link.Type"] = "Type:";
        _strings["Link.Type.Symlink"] = "Symbolic Link";
        _strings["Link.Type.Hardlink"] = "Hard Link";
        _strings["Link.Create"] = "Create";
        _strings["Link.Cancel"] = "Cancel";
        _strings["Link.Success"] = "Link created: {0}";
        _strings["Link.Error"] = "Error creating link: {0}";
        _strings["Link.AdminRequired"] = "Administrator rights required for symbolic links";
        _strings["Link.NameEmpty"] = "Link name cannot be empty";
        _strings["Link.AlreadyExists"] = "Link already exists: {0}";
        _strings["Link.InvalidChars"] = "Link name contains invalid characters";
        _strings["Link.MultipleTitle"] = "Create Links";
        _strings["Link.MultipleFiles"] = "Files:";
        _strings["Link.Done"] = "Links created: {0}, errors: {1}";

        // Macro manager
        _strings["Macro.Title"] = "Macro Manager";
        _strings["Macro.Name"] = "Name:";
        _strings["Macro.Description"] = "Description:";
        _strings["Macro.Hotkey"] = "Hotkey:";
        _strings["Macro.Enabled"] = "Enabled";
        _strings["Macro.Steps"] = "Steps:";
        _strings["Macro.AddStep"] = "Add step";
        _strings["Macro.RemoveStep"] = "Remove step";
        _strings["Macro.MoveUp"] = "Move up";
        _strings["Macro.MoveDown"] = "Move down";
        _strings["Macro.Execute"] = "Execute";
        _strings["Macro.Save"] = "Save";
        _strings["Macro.Cancel"] = "Cancel";
        _strings["Macro.Add"] = "Add macro";
        _strings["Macro.Delete"] = "Delete macro";
        _strings["Macro.DeleteConfirm"] = "Delete macro '{0}'?";
        _strings["Macro.ExecuteDone"] = "Macro executed: {0} steps";
        _strings["Macro.ExecuteError"] = "Macro error: {0}";
        _strings["Macro.NoCommand"] = "Command not found: {0}";
        _strings["Macro.NoSteps"] = "Macro has no steps";
        _strings["Macro.NameEmpty"] = "Macro name cannot be empty";
        _strings["Macro.ColCommand"] = "Command";
        _strings["Macro.ColParams"] = "Parameters";
        _strings["Macro.ColOrder"] = "#";
        _strings["Macro.StepCommandPrompt"] = "Command name (e.g. app.copy):";
        _strings["Macro.StepParamsPrompt"] = "Parameters (key=value;key2=value2):";
        _strings["Macro.Saved"] = "Macros saved";

        // Plugin manager
        _strings["Plugin.Title"] = "Plugin Manager";
        _strings["Plugin.Name"] = "Name";
        _strings["Plugin.Version"] = "Version";
        _strings["Plugin.Author"] = "Author";
        _strings["Plugin.Description"] = "Description";
        _strings["Plugin.Enabled"] = "Enabled";
        _strings["Plugin.Disabled"] = "Disabled";
        _strings["Plugin.Enable"] = "Enable";
        _strings["Plugin.Disable"] = "Disable";
        _strings["Plugin.Reload"] = "Reload";
        _strings["Plugin.Refresh"] = "Refresh";
        _strings["Plugin.OpenFolder"] = "Open plugins folder";
        _strings["Plugin.NoPlugins"] = "No plugins found. Place DLL files in the plugins folder.";
        _strings["Plugin.LoadError"] = "Failed to load plugin: {0}";
        _strings["Plugin.Initialized"] = "Plugin initialized: {0}";

        // Properties
        _strings["Props.EditAttributes"] = "Edit attributes";
        _strings["Props.EditTimestamps"] = "Edit timestamps";

        // ═══════════════════════════════════════════
        // SETTINGS — NEW KEYS (ph9.6)
        // ═══════════════════════════════════════════
        _strings["Settings.Panels"] = "Panels";
        _strings["Settings.Files"] = "Files";
        _strings["Settings.SortFoldersFirst"] = "Folders first";
        _strings["Settings.ShowFileSizeLabel"] = "Size";
        _strings["Settings.ShowDateLabel"] = "Date";
        _strings["Settings.ShowAttributesLabel"] = "Attributes";
        _strings["Settings.ShowPathInTitle"] = "Path in title";
        _strings["Settings.WindowOpacity"] = "Window opacity";
        _strings["Settings.SectionFont"] = "Font";
        _strings["Settings.SectionDisplay"] = "Display";
        _strings["Settings.SectionIndent"] = "Indentation";
        _strings["Settings.SectionShell"] = "Shell";
        _strings["Settings.SectionBehavior"] = "Behavior";
        _strings["Settings.EditorHighlightCurrentLine"] = "Current line";
        _strings["Settings.EditorHighlightBrackets"] = "Brackets";
        _strings["Settings.EditorAutoCloseBrackets"] = "Auto-brackets";
        _strings["Settings.EditorAutoCloseQuotes"] = "Auto-quotes";
        _strings["Settings.EditorMinHighlightLength"] = "Min word length";
        _strings["Settings.Width"] = "Width";
        _strings["Settings.ScrollbackLines"] = "Scrollback lines";
        _strings["Settings.Cursor"] = "Cursor";
        _strings["Settings.CursorBlock"] = "Block";
        _strings["Settings.CursorUnderline"] = "Underline";
        _strings["Settings.CursorBar"] = "Vertical bar";
        _strings["Settings.TerminalHeight"] = "Panel height";
        _strings["Settings.DirectoryTree"] = "Directory tree";
        _strings["Settings.DoubleClick"] = "Double-click";
        _strings["Settings.HiddenFolders"] = "Hidden folders";
        _strings["Settings.FullPaths"] = "Full paths";
        _strings["Settings.RememberPaths"] = "Remember paths";
        _strings["Settings.Ms"] = "ms";
        _strings["Settings.AutoRefreshCheck"] = "Auto-refresh";
        _strings["Settings.DoubleClickFile"] = "Double-click → file";
        _strings["Settings.Overwrite"] = "Overwrite";
        _strings["Settings.Copying"] = "Copying";
        _strings["Settings.CopyAttributesLabel"] = "Attributes";
        _strings["Settings.CopyTimestampsLabel"] = "Timestamps";
        _strings["Settings.ReserveSpaceLabel"] = "Reserve space";
        _strings["Settings.VerifyAfterCopy"] = "Verify";
        _strings["Settings.AbortOnError"] = "Abort on error";
        _strings["Settings.AutoShowQueue"] = "Show queue";
        _strings["Settings.Confirmation"] = "Confirmation";
        _strings["Settings.ConfirmDeleteLabel"] = "Delete";
        _strings["Settings.ConfirmOverwriteLabel"] = "Overwrite";
        _strings["Settings.MaxRecursion"] = "Max recursion depth";
    }

    /// <summary>
    /// Загружает переводы из .lng файла.
    /// Loads translations from a .lng file.
    /// </summary>
    private void LoadFromFile(string languageCode)
    {
        var langDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang");
        var filePath = Path.Combine(langDir, $"{languageCode}.lng");
        if (!File.Exists(filePath)) return;

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

            var eqIdx = line.IndexOf('=');
            if (eqIdx <= 0) continue;

            var key = line[..eqIdx].Trim();
            var value = line[(eqIdx + 1)..].Trim();
            if (!string.IsNullOrEmpty(key))
                _strings[key] = value;
        }
    }

    /// <summary>
    /// Возвращает список доступных языков (код → название).
    /// Returns available languages (code → display name).
    /// </summary>
    public static List<(string Code, string DisplayName)> GetAvailableLanguages()
    {
        var langs = new List<(string, string)>
        {
            ("en", "English"),
            ("ru", "Русский"),
        };

        // Сканируем папку lang для дополнительных языков
        var langDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang");
        if (Directory.Exists(langDir))
        {
            foreach (var file in Directory.GetFiles(langDir, "*.lng"))
            {
                var code = Path.GetFileNameWithoutExtension(file);
                if (code != "ru") // ru уже добавлен
                    langs.Add((code, code.ToUpperInvariant()));
            }
        }

        return langs;
    }
}
