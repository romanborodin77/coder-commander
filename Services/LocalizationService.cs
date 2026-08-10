using System.Text;

namespace CoderCommander.Services;

/// <summary>
/// Centralized localization service.
/// English defaults are built-in; additional languages loaded from lang/*.lng files.
/// </summary>
public sealed class LocalizationService
{
    public static LocalizationService Current { get; } = new();

    private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
    private string _currentLanguage = "en";

    // Report(...) calls made from a running file operation (Operations/FileOperation.cs uses
    // ConfigureAwait(false) throughout, so progress/state callbacks run on a thread-pool thread,
    // not the UI thread) reach GetString via MainViewModel.OnOperationChanged concurrently with
    // whatever thread is mid-LoadLanguage - switching the UI language while a copy/move is
    // running races a read against Clear()+repopulate on the same plain Dictionary. Guards every
    // access to _strings.
    private readonly object _lock = new();

    public string CurrentLanguage => _currentLanguage;

    public event EventHandler? LanguageChanged;

    private LocalizationService()
    {
        LoadDefaults();
    }

    /// <summary>Gets a localized string, formatting with optional args.</summary>
    public string GetString(string key, params object[] args)
    {
        string? value;
        lock (_lock)
        {
            _strings.TryGetValue(key, out value);
        }

        if (value == null) return key;

        if (args.Length > 0)
        {
            try { return string.Format(value, args); }
            catch { return value; }
        }
        return value;
    }

    /// <summary>Shortcut: L(key, args).</summary>
    public string this[string key, params object[] args] => GetString(key, args);

    /// <summary>Loads a language file from lang/{code}.lng.</summary>
    public void LoadLanguage(string code)
    {
        _currentLanguage = code;

        // LoadDefaults() is the fail-safe fallback (baked into the binary) for every language,
        // English included - lang/english.lng is loaded on top of it just like russian.lng is for
        // "ru", so the two don't have to be kept in sync by hand.
        var fileName = code switch
        {
            "en" => "english",
            "ru" => "russian",
            // code round-trips through settings.json - sanitize before it becomes a path. Without
            // this, a code like "..\..\..\some\file" would make File.Exists/LoadFromFile below
            // read and display an arbitrary file's contents as UI strings.
            _ => SanitizeLanguageFileStem(code)
        };
        var path = Path.Combine(AppContext.BaseDirectory, "lang", $"{fileName}.lng");

        // Clear+repopulate must be atomic from GetString's point of view - a reader landing
        // between Clear() and the end of LoadFromFile() would see a partially (or not at all)
        // populated dictionary instead of either the old or the new language.
        lock (_lock)
        {
            _strings.Clear();
            LoadDefaults();
            if (File.Exists(path))
                LoadFromFile(path);
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns available (code, name) pairs.</summary>
    public IReadOnlyList<(string code, string name)> GetAvailableLanguages()
    {
        var list = new List<(string, string)> { ("en", "English"), ("ru", "Русский") };
        var langDir = Path.Combine(AppContext.BaseDirectory, "lang");
        if (Directory.Exists(langDir))
        {
            foreach (var file in Directory.GetFiles(langDir, "*.lng"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                // Map file names to language codes
                var code = fileName switch
                {
                    "english" => "en",
                    "russian" => "ru",
                    _ => fileName
                };
                // Skip if already in list
                if (code != "en" && code != "ru")
                    list.Add((code, code.ToUpperInvariant()));
            }
        }
        return list;
    }

    /// <summary>Strips any directory traversal a language code might contain (e.g. from a hand-edited
    /// or tampered settings.json), so it can only ever name a file directly inside lang/.</summary>
    private static string SanitizeLanguageFileStem(string code)
    {
        try
        {
            var name = Path.GetFileName(code);
            return string.IsNullOrEmpty(name) ? "invalid" : name;
        }
        catch (ArgumentException)
        {
            return "invalid";
        }
    }

    private void LoadFromFile(string path)
    {
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();
            val = val.Replace("\\n", "\n");
            _strings[key] = val;
        }
    }

    private void LoadDefaults()
    {
        // ═══ Common ═══
        _strings["Common.OK"] = "OK";
        _strings["Common.Cancel"] = "Cancel";
        _strings["Common.Close"] = "Close";
        _strings["Common.Browse"] = "Browse…";
        _strings["Common.Yes"] = "Yes";
        _strings["Common.No"] = "No";
        _strings["Common.Save"] = "Save";
        _strings["Common.Reset"] = "Reset";
        _strings["Common.Apply"] = "Apply";
        _strings["Common.Delete"] = "Delete";
        _strings["Common.Folder"] = "Folder";
        _strings["Common.Rename"] = "Rename";
        _strings["Common.Refresh"] = "Refresh";
        _strings["Common.Error"] = "Error";

        // ═══ Menu ═══
        _strings["Menu.File"] = "&File";
        _strings["Menu.File.View"] = "&View";
        _strings["Menu.File.Edit"] = "&Edit";
        _strings["Menu.File.EditNew"] = "Edit &New";
        _strings["Menu.File.Copy"] = "&Copy";
        _strings["Menu.File.Move"] = "&Move/Rename";
        _strings["Menu.File.Rename"] = "Rena&me";
        _strings["Menu.File.CreateFolder"] = "M&ake Dir";
        _strings["Menu.File.Delete"] = "&Delete";
        _strings["Menu.File.Wipe"] = "&Wipe";
        _strings["Menu.File.Properties"] = "&Properties";
        _strings["Menu.File.Attributes"] = "&Attributes";
        _strings["Menu.File.Pack"] = "&Pack";
        _strings["Menu.File.Extract"] = "&Extract";
        _strings["Menu.File.Split"] = "Spl&it";
        _strings["Menu.File.Combine"] = "Com&bine";
        _strings["Menu.File.Checksum"] = "Chec&ksum";
        _strings["Menu.File.Exit"] = "E&xit";

        _strings["Menu.Selection"] = "&Selection";
        _strings["Menu.Selection.All"] = "Select &All";
        _strings["Menu.Selection.None"] = "&Deselect All";
        _strings["Menu.Selection.Invert"] = "&Invert Selection";
        _strings["Menu.Selection.Group"] = "Select &Group…";
        _strings["Menu.Selection.DeselectGroup"] = "Deselect &Group…";

        _strings["Menu.Commands"] = "&Commands";
        _strings["Menu.Commands.Search"] = "&Search…";
        _strings["Menu.Commands.MultiRename"] = "&Multi-Rename…";
        _strings["Menu.Commands.SyncDirs"] = "Synchronize &Dirs…";
        _strings["Menu.Commands.SwapPanels"] = "Swap &Panels";
        _strings["Menu.Commands.SyncPanels"] = "&Target = Source";
        _strings["Menu.Commands.Terminal"] = "&Terminal";
        _strings["Menu.Commands.DirTree"] = "Directory &Tree";
        _strings["Menu.Commands.DiskInfo"] = "Disk &Info";
        _strings["Menu.Commands.OpQueue"] = "Operation &Queue";
        _strings["Menu.Commands.CalculateFolderSize"] = "Calculate &Folder Size";

        _strings["Menu.View"] = "&View";
        _strings["Menu.View.Hidden"] = "Show &Hidden";
        _strings["Menu.View.Refresh"] = "&Refresh";
        _strings["Menu.View.Theme"] = "&Theme";
        _strings["Menu.View.Theme.Dark"] = "&Dark";
        _strings["Menu.View.Theme.Light"] = "&Light";
        _strings["Menu.View.Language"] = "&Language";
        _strings["Menu.View.Sort"] = "Sort &by";
        _strings["Menu.View.Sort.Name"] = "&Name";
        _strings["Menu.View.Sort.Extension"] = "E&xtension";
        _strings["Menu.View.Sort.Size"] = "&Size";
        _strings["Menu.View.Sort.Modified"] = "Date &modified";
        _strings["Menu.View.Sort.DirsFirst"] = "&Directories first";
        _strings["Menu.View.Sort.Descending"] = "&Descending";
        _strings["Menu.View.ShowExtInName"] = "Show e&xtension in name";
        _strings["Language.English"] = "English";
        _strings["Language.Russian"] = "Русский";

        _strings["Menu.Config"] = "&Configuration";
        _strings["Menu.Config.Settings"] = "&Settings…";
        _strings["Menu.Config.Bookmarks"] = "&Bookmarks…";

        _strings["Menu.Help"] = "&Help";
        _strings["Menu.Help.About"] = "&About";

        // ═══ Toolbar ═══
        _strings["Toolbar.Back"] = "Back";
        _strings["Toolbar.Forward"] = "Forward";
        _strings["Toolbar.Up"] = "Up";
        _strings["Toolbar.Copy"] = "Copy";
        _strings["Toolbar.Move"] = "Move";
        _strings["Toolbar.Delete"] = "Delete";
        _strings["Toolbar.NewDir"] = "New Dir";
        _strings["Toolbar.Refresh"] = "Refresh";
        _strings["Toolbar.Search"] = "Search";
        _strings["Toolbar.Settings"] = "Settings";

        // ═══ Function buttons ═══
        _strings["Fn.View"] = "F3 View";
        _strings["Fn.Edit"] = "F4 Edit";
        _strings["Fn.Copy"] = "F5 Copy";
        _strings["Fn.Move"] = "F6 Move";
        _strings["Fn.MkDir"] = "F7 MkDir";
        _strings["Fn.Delete"] = "F8 Delete";
        _strings["Fn.Terminal"] = "F9 Terminal";
        _strings["Fn.Exit"] = "F10 Exit";

        // ═══ Panel ═══
        _strings["Panel.Name"] = "Name";
        _strings["Panel.Ext"] = "Type";
        _strings["Panel.Size"] = "Size";
        _strings["Panel.Modified"] = "Modified";
        _strings["Panel.Attributes"] = "Attr";
        _strings["Panel.Path"] = "Path";
        _strings["Panel.Created"] = "Created";
        _strings["Panel.Dir"] = "<DIR>";
        _strings["Panel.Parent"] = "..";
        _strings["Panel.Filter"] = "Filter";
        _strings["Panel.Selected"] = "{0} selected";
        _strings["Panel.Items"] = "{0} items";
        _strings["Panel.FileInfo"] = "{0}  {1}  {2}";
        _strings["Panel.DirInfo"] = "[DIR] {0}";
        _strings["Panel.DriveTooltip"] = "Go to {0}";

        // ═══ Status bar ═══
        _strings["Status.ElementsCount"] = "{0} items";
        _strings["Status.FreeSpace"] = "{0} free / {1} total";

        // ═══ About ═══
        _strings["About.Title"] = "About";
        _strings["About.AppName"] = "Coder Commander";
        _strings["About.Version"] = "Version {0}";
        _strings["About.Subtitle"] = "Dual-panel file manager for programmers";
        _strings["About.Description"] = "Modern file manager for developers";
        _strings["About.Tech"] = "Built with WinForms + MVVM + Command Pattern";
        _strings["About.Display"] = "Display";
        _strings["About.Terminal"] = "Terminal";
        _strings["About.Formats"] = "Archive formats";
        _strings["About.NotAvailable"] = "not available";
        _strings["About.SystemInfo"] = "System information";
        _strings["About.Runtime"] = ".NET runtime";
        _strings["About.Os"] = "Operating system";
        _strings["About.Architecture"] = "Architecture";
        _strings["About.Memory"] = "Memory in use";
        _strings["About.ConfigFolder"] = "Settings folder";
        _strings["About.CopyInfo"] = "Copy info";
        _strings["About.Copied"] = "Copied";
        _strings["About.OpenFolder"] = "Open";

        // ═══ Copy/Move dialog ═══
        _strings["CopyMove.Title.Copy"] = "Copy";
        _strings["CopyMove.Title.Move"] = "Move";
        _strings["CopyMove.Source"] = "Source:";
        _strings["CopyMove.Destination"] = "Destination:";
        _strings["CopyMove.Files"] = "{0} file(s)";
        _strings["CopyMove.TotalSize"] = "Total: {0}";
        _strings["CopyMove.OverwritePolicy"] = "Overwrite policy:";
        _strings["CopyMove.CopyAttributes"] = "Copy attributes";
        _strings["CopyMove.CopyTimestamps"] = "Copy timestamps";
        _strings["CopyMove.Queue"] = "Add to queue";

        // ═══ Overwrite policy ═══
        _strings["Overwrite.Ask"] = "Ask";
        _strings["Overwrite.Skip"] = "Skip";
        _strings["Overwrite.Overwrite"] = "Overwrite";
        _strings["Overwrite.OverwriteOlder"] = "Overwrite if older";
        _strings["Overwrite.OverwriteAll"] = "Overwrite all";
        _strings["Overwrite.SkipAll"] = "Skip all";
        _strings["Overwrite.Rename"] = "Rename";

        // ═══ Overwrite dialog ═══
        _strings["OverwriteDlg.Title"] = "File exists";
        _strings["OverwriteDlg.Source"] = "Source:";
        _strings["OverwriteDlg.Destination"] = "Destination:";

        // ═══ Operation dialog ═══
        _strings["OpDlg.CurrentFile"] = "Current file:";
        _strings["OpDlg.Total"] = "Overall:";
        _strings["OpDlg.Skip"] = "Skip";
        _strings["OpDlg.Pause"] = "Pause";
        _strings["OpDlg.Resume"] = "Resume";
        _strings["OpDlg.Cancel"] = "Cancel";
        _strings["OpDlg.Speed"] = "Speed: {0}/s";
        _strings["OpDlg.ETA"] = "ETA: {0:m\\:ss}";
        _strings["OpDlg.Files"] = "Files: {0} / {1}";
        _strings["OpDlg.Running"] = "Running…";
        _strings["OpDlg.Paused"] = "Paused";
        _strings["OpDlg.Completed"] = "Completed";
        _strings["OpDlg.Canceled"] = "Canceled";
        _strings["OpDlg.Failed"] = "Failed";

        // ═══ Operation queue ═══
        _strings["OpQueue.Title"] = "Operation Queue";
        _strings["OpQueue.Pause"] = "Pause";
        _strings["OpQueue.Resume"] = "Resume";
        _strings["OpQueue.CancelAll"] = "Cancel All";
        _strings["OpQueue.Clear"] = "Clear Completed";
        _strings["OpQueue.Close"] = "Close";
        _strings["OpQueue.Col.Type"] = "Type";
        _strings["OpQueue.Col.Source"] = "Source";
        _strings["OpQueue.Col.Destination"] = "Destination";
        _strings["OpQueue.Col.Status"] = "Status";
        _strings["OpQueue.Empty"] = "No operations";
        _strings["OpQueue.Count"] = "{0} operation(s)";
        _strings["OpQueue.Status.Running"] = "Running";
        _strings["OpQueue.Status.Paused"] = "Paused";
        _strings["OpQueue.Status.Completed"] = "Done";
        _strings["OpQueue.Status.Canceled"] = "Canceled";
        _strings["OpQueue.Status.Failed"] = "Failed";
        _strings["OpQueue.Status.Queued"] = "Queued";
        _strings["OpQueue.Type.Copy"] = "Copy";
        _strings["OpQueue.Type.Move"] = "Move";
        _strings["OpQueue.Type.Delete"] = "Delete";
        _strings["OpQueue.Type.Pack"] = "Pack";
        _strings["OpQueue.Type.Unpack"] = "Unpack";

        // ═══ Settings ═══
        _strings["Settings.Title"] = "Settings";
        _strings["Settings.Appearance"] = "Appearance";
        _strings["Settings.Editor"] = "Editor";
        _strings["Settings.FileOps"] = "File Operations";
        _strings["Settings.Confirmations"] = "Confirmations";
        _strings["Settings.Theme"] = "Theme:";
        _strings["Settings.Theme.Dark"] = "Dark";
        _strings["Settings.Theme.Light"] = "Light";
        _strings["Settings.Language"] = "Language:";
        _strings["Settings.ShowHidden"] = "Show hidden files";
        _strings["Settings.ShowToolbar"] = "Show toolbar";
        _strings["Settings.ShowStatusBar"] = "Show status bar";
        _strings["Settings.ShowFunctionButtons"] = "Show function buttons";
        _strings["Settings.DirectoriesFirst"] = "Directories first";
        _strings["Settings.ShowExtInName"] = "Show extension in file name";
        _strings["Settings.ConfirmDelete"] = "Confirm delete";
        _strings["Settings.ConfirmOverwrite"] = "Confirm overwrite";
        _strings["Settings.CopyAttributes"] = "Copy attributes by default";
        _strings["Settings.CopyTimestamps"] = "Copy timestamps by default";
        _strings["Settings.ArchiveCompressionFormat"] = "Archive format:";
        _strings["Settings.ArchiveCompressionPreset"] = "Compression:";

        // ═══ Search ═══
        _strings["Search.Title"] = "Search";
        _strings["Search.Path"] = "Path:";
        _strings["Search.Pattern"] = "File mask:";
        _strings["Search.Content"] = "Containing text:";
        _strings["Search.CaseSensitive"] = "Case sensitive";
        _strings["Search.UseRegex"] = "Regular expression";
        _strings["Search.Subdirs"] = "Search subdirectories";
        _strings["Search.Start"] = "Search";
        _strings["Search.Stop"] = "Stop";
        _strings["Search.Results"] = "Results";
        _strings["Search.Found"] = "Found {0} file(s)";
        _strings["Search.Searching"] = "Searching…";
        _strings["Search.NoResults"] = "No files found";

        // ═══ Bookmarks ═══
        _strings["Conn.NoProviders"] = "No connection types are available in this build yet.";
        _strings["Conn.Title"] = "Connections";
        _strings["Conn.Col.Name"] = "Name";
        _strings["Conn.Col.Address"] = "Address";
        _strings["Conn.Col.Auto"] = "Auto-connect";
        _strings["Conn.Add"] = "Add";
        _strings["Conn.Edit"] = "Edit";
        _strings["Conn.Remove"] = "Remove";
        _strings["Conn.Empty"] = "No connections configured";
        _strings["Conn.RemoveConfirm"] = "Remove connection \"{0}\"? The saved password will be deleted too.";
        _strings["Conn.Edit.Title"] = "Connection";
        _strings["Conn.Field.Name"] = "Name";
        _strings["Conn.Field.Type"] = "Type";
        _strings["Conn.Field.Url"] = "Address";
        _strings["Conn.Field.User"] = "User name";
        _strings["Conn.Field.Password"] = "Password";
        _strings["Conn.Field.SavePassword"] = "Save password";
        _strings["Conn.Field.AutoConnect"] = "Connect on startup";
        _strings["Conn.PasswordStored"] = "A password is saved. Leave empty to keep it.";
        _strings["Conn.Invalid.Name"] = "Enter a name for the connection.";
        _strings["Conn.Invalid.Url"] = "Enter an address, for example https://example.com/dav";
        _strings["Conn.AutoConnectNeedsPassword"] = "Connect on startup needs a saved password, or an empty user name.";
        _strings["Settings.Connections"] = "Connections";
        _strings["Bookmark.Title"] = "Bookmarks";
        _strings["Bookmark.Col.Name"] = "Name";
        _strings["Bookmark.Col.Path"] = "Path";
        _strings["Bookmark.Add"] = "Add…";
        _strings["Bookmark.Remove"] = "Remove";
        _strings["Bookmark.Empty"] = "No bookmarks";

        // ═══ Directory tree ═══
        _strings["DirTree.Title"] = "Directory Tree";
        _strings["DirTree.Root"] = "Root";

        // ═══ Properties ═══
        _strings["Props.Title"] = "Properties";
        _strings["Props.Name"] = "Name:";
        _strings["Props.Path"] = "Path:";
        _strings["Props.Size"] = "Size:";
        _strings["Props.Modified"] = "Modified:";
        _strings["Props.Created"] = "Created:";
        _strings["Props.Attributes"] = "Attributes:";
        _strings["Props.Files"] = "Files:";
        _strings["Props.Subdirs"] = "Subdirectories:";
        _strings["Props.TotalSize"] = "Total size:";
        _strings["Props.EditAttributes"] = "Change attributes:";
        _strings["Props.EditTimestamps"] = "Change timestamps:";
        _strings["Props.ReadOnly"] = "Read-only";
        _strings["Props.Hidden"] = "Hidden";
        _strings["Props.System"] = "System";
        _strings["Props.Archive"] = "Archive";
        _strings["Props.Accessed"] = "Accessed:";
        _strings["Props.Applied"] = "Properties applied.";
        _strings["Props.Reseted"] = "Changes reverted to original.";
        _strings["Props.MultiTitle"] = "Multiple items ({0})";
        _strings["Props.Selection"] = "Selected:";
        _strings["Props.Type"] = "Type:";
        _strings["Props.Recursive"] = "Apply recursively";
        _strings["Props.TimestampHint"] = "Check the box beside a timestamp to change it; uncheck to leave unchanged.";
        _strings["Props.AttrHint"] = "Use the drop-down to set, clear or keep each attribute.";
        _strings["SelectionChanged"] = "Set on all";
        _strings["SelectionUnchanged"] = "Keep original";
        _strings["SelectionCleared"] = "Clear on all";
        _strings["Props.Folder"] = "Folder";
        _strings["Props.File"] = "File";
        _strings["Props.ApplyToAll"] = "Apply to {0} item(s)";
        _strings["Props.Stale"] = "Reopen dialog to refresh size and stats.";
        _strings["Props.CountFilesDirs"] = "{0} files, {1} folders";

        // ═══ Input dialog ═══
        _strings["Input.Title"] = "Input";
        _strings["Input.CreateDir"] = "Create Directory";
        _strings["Input.CreateDirPrompt"] = "Folder name:";
        _strings["Input.Rename"] = "Rename";
        _strings["Input.RenamePrompt"] = "New name:";
        _strings["Input.AddBookmark"] = "Add Bookmark";
        _strings["Input.BookmarkName"] = "Bookmark name:";
        _strings["Input.BookmarkPath"] = "Path:";
        _strings["Input.ChangeDirPrompt"] = "Path:";
        _strings["Input.SelectPattern"] = "Pattern (e.g. *.txt):";
        _strings["Menu.Commands.ChangeDir"] = "Change &Directory…";

        // ═══ Confirmations ═══
        _strings["Confirm.Delete"] = "Delete {0} item(s)?";
        _strings["Confirm.DeleteItems"] = "Delete {0} item(s)?\n\n{1}";
        _strings["Confirm.RecycleBinFailedPermanent"] = "The Recycle Bin could not be used for {0} item(s). Permanently delete them instead? This cannot be undone.\n\n{1}";
        _strings["Confirm.Wipe"] = "Wipe {0} item(s)?";
        _strings["Confirm.WipeItems"] = "Wipe {0} item(s)? This cannot be undone.\n\nNote: on SSDs, a single-pass overwrite does not guarantee the data is unrecoverable - wear-leveling can leave copies on other physical cells.\n\n{1}";

        // ═══ Context menu ═══
        _strings["Ctx.View"] = "View";
        _strings["Ctx.Edit"] = "Edit";
        _strings["Ctx.Copy"] = "Copy";
        _strings["Ctx.Move"] = "Move";
        _strings["Ctx.Rename"] = "Rename";
        _strings["Ctx.Delete"] = "Delete";
        _strings["Ctx.Properties"] = "Properties";
        _strings["Ctx.CreateLink"] = "Create Link";
        _strings["Ctx.CreateSymlink"] = "Symbolic Link…";
        _strings["Ctx.CreateHardlink"] = "Hard Link…";
        _strings["Ctx.CopyPath"] = "Copy Path";
        _strings["Ctx.CopyPath.Full"] = "Full Path";
        _strings["Ctx.CopyPath.Name"] = "Name Only";
        _strings["Ctx.CopyPath.NoExt"] = "Path Without Extension";
        _strings["Ctx.SelectAll"] = "Select All";
        _strings["Ctx.InvertSelection"] = "Invert Selection";
        _strings["Ctx.OpenWith"] = "Open With…";

        // ═══ Errors ═══
        _strings["Err.PathNotFound"] = "Path not found: {0}";
        _strings["Err.AccessDenied"] = "Access denied: {0}";
        _strings["Err.FileExists"] = "File already exists: {0}";
        _strings["Err.InvalidName"] = "Invalid file name: {0}";

        // ═══ Multi-rename ═══
        _strings["MultiRename.Title"] = "Multi-Rename";
        _strings["MultiRename.Pattern"] = "Pattern:";
        _strings["MultiRename.Extension"] = "Extension:";
        _strings["MultiRename.StartAt"] = "Counter start:";
        _strings["MultiRename.Step"] = "Step:";
        _strings["MultiRename.Hint"] = "[N]=name  [E]=ext  [N3]=first 3 chars  [N-3]=last 3  [C]=counter  [C2:10]=width 2 start 10  [D]=date  [T]=time  [P]=parent dir";
        _strings["MultiRename.OldName"] = "Old Name";
        _strings["MultiRename.NewName"] = "New Name";
        _strings["MultiRename.Status"] = "Status";
        _strings["MultiRename.ErrDuplicate"] = "Duplicate target names detected. Please adjust the pattern.";

        // ═══ Viewer ═══
        _strings["View.Title"] = "Viewer";
        _strings["View.Text"] = "Text";
        _strings["View.Hex"] = "Hex";
        _strings["View.Image"] = "Image";
        _strings["View.WordWrap"] = "Wrap";
        _strings["View.TextMode"] = "Text mode — {0}";
        _strings["View.HexMode"] = "Hex mode — {0}";
        _strings["View.ImageMode"] = "Image mode";
        _strings["View.TooBigForText"] = "File too large for text mode ({0}). Limit: {1}.";
        _strings["View.HexTruncated"] = "Showing first {0} of {1}.";
        _strings["View.Error"] = "Error";
        _strings["View.FileNotFound"] = "File not found.";
        _strings["View.Toolbar.Previous"] = "Previous";
        _strings["View.Toolbar.Next"] = "Next";

        // ═══ Editor ═══
        _strings["Edit.Title"] = "Editor";
        _strings["Edit.NewFile"] = "Untitled";
        _strings["Edit.SaveAs"] = "Save As";
        _strings["Edit.Saved"] = "Saved to {0}";
        _strings["Edit.NotFound"] = "Text not found";
        _strings["Edit.UnsavedChanges"] = "Save changes to {0}?";
        _strings["Edit.WordWrap"] = "Word wrap";
        _strings["Edit.ShowWhitespace"] = "Show whitespace";
        _strings["Edit.FilterAll"] = "All files (*.*)|*.*|Text files (*.txt)|*.txt";
        _strings["Edit.Find"] = "Find";
        _strings["Edit.Modified"] = "Modified";
        _strings["Edit.Bytes"] = "bytes";
        _strings["Edit.KB"] = "KB";
        _strings["Edit.FindBar.MatchCount"] = "{0} of {1}";
        _strings["Edit.FindBar.MatchCase"] = "Match case";
        _strings["Edit.FindBar.ReplaceAll"] = "Replace All";
        _strings["Edit.FindBar.ReplaceAllConfirm"] = "Replace all {0} occurrences?";
        _strings["Edit.FindBar.ReplacedCount"] = "Replaced: {0}";
        _strings["Edit.FindBar.WrappedToTop"] = "Search wrapped to top";
        _strings["Edit.FindBar.WrappedToBottom"] = "Search wrapped to bottom";
        _strings["Edit.GoToLine"] = "Go to Line";
        _strings["Edit.GoToLinePrompt"] = "Line number:";
        _strings["Edit.Toolbar.New"] = "New";
        _strings["Edit.Toolbar.Open"] = "Open";
        _strings["Edit.Toolbar.Save"] = "Save";
        _strings["Edit.Toolbar.SaveAll"] = "Save All";
        _strings["Edit.Toolbar.Undo"] = "Undo";
        _strings["Edit.Toolbar.Redo"] = "Redo";
        _strings["Edit.Toolbar.Cut"] = "Cut";
        _strings["Edit.Toolbar.Copy"] = "Copy";
        _strings["Edit.Toolbar.Paste"] = "Paste";
        _strings["Edit.Toolbar.Find"] = "Find";
        _strings["Edit.Toolbar.Replace"] = "Replace";
        _strings["Common.Confirm"] = "Confirm";
        _strings["Common.Confirm"] = "Confirm";

        // ═══ Archive ═══
        _strings["Archive.Title"] = "Archive";
        _strings["Archive.ChooseTarget"] = "Choose target folder";
        _strings["Archive.UnsupportedFormat"] = "\"{0}\" is not a supported archive.";
        _strings["Archive.PackTitle"] = "Pack Files";
        _strings["Archive.PackPrompt"] = "Archive name:";
        _strings["Archive.PackFormat"] = "Format:";
        _strings["Archive.PackCompression"] = "Compression:";
        _strings["Archive.PackMoveOriginals"] = "Delete originals after packing (move)";
        _strings["Archive.Format.Zip"] = "ZIP";
        _strings["Archive.Format.Tar"] = "TAR";
        _strings["Archive.Format.TarGz"] = "TAR.GZ";
        _strings["Archive.Format.SevenZip"] = "7Z";
        _strings["Archive.Format.Rar"] = "RAR";
        _strings["Archive.Format.TarBz2"] = "TAR.BZ2";
        _strings["Archive.Format.TarXz"] = "TAR.XZ";
        _strings["Archive.ReadOnlyFormat"] = "\"{0}\" is a read-only archive format and cannot be modified.";
        _strings["Archive.Compression.Store"] = "None (store only)";
        _strings["Archive.Compression.Fastest"] = "Fastest";
        _strings["Archive.Compression.Balanced"] = "Balanced";
        _strings["Archive.Compression.Maximum"] = "Maximum";
        _strings["Archive.PackExists"] = "Archive \"{0}\" already exists. Add files to it?";
        _strings["Archive.UnpackTitle"] = "Unpack Files";
        _strings["Archive.UnpackNoArchive"] = "Select an archive file or navigate inside an archive.";
        _strings["Archive.Packed"] = "Packed {0} file(s) into {1}";
        _strings["Archive.Unpacked"] = "Unpacked {0} file(s) to {1}";
        _strings["Archive.PackFailed"] = "Pack failed: {0}";
        _strings["Archive.UnpackFailed"] = "Unpack failed: {0}";
        _strings["Archive.EnterArchive"] = "Enter Archive";
        _strings["Archive.ExitArchive"] = "Exit Archive";
        _strings["Archive.SameArchiveTransfer"] = "Copying inside the same archive is not supported. Unpack the files first.";
        _strings["Transfer.SourceEqualsDestination"] = "Source and destination are the same. Choose a different destination folder.";
        _strings["Archive.WipeUnsupported"] = "Secure wipe is not available for archive entries. Use Delete instead.";
        _strings["Archive.CalculateSizeUnsupported"] = "Folder size calculation is not available for archive entries.";
        _strings["Archive.PackUnsupported"] = "Packing is not available for archive entries. Unpack the files first.";
        _strings["Archive.NestedUnsupported"] = "Archives inside archives cannot be opened directly. Unpack \"{0}\" first.";

        // ═══ SyncDirs ═══
        _strings["SyncDirs.Title"] = "Synchronize Dirs";
        _strings["SyncDirs.Left"] = "Left:";
        _strings["SyncDirs.Right"] = "Right:";
        _strings["SyncDirs.Subdirs"] = "Include subdirs";
        _strings["SyncDirs.IgnoreTime"] = "Ignore time (size only)";
        _strings["SyncDirs.Compare"] = "Compare";
        _strings["SyncDirs.Status"] = "Status";
        _strings["SyncDirs.Path"] = "Path";
        _strings["SyncDirs.LeftSize"] = "Left size";
        _strings["SyncDirs.RightSize"] = "Right size";
        _strings["SyncDirs.Action"] = "Action";
        _strings["SyncDirs.CopyToLeft"] = "Copy → Left";
        _strings["SyncDirs.CopyToRight"] = "Copy → Right";
        _strings["SyncDirs.BadPaths"] = "Both directories must exist.";
        _strings["SyncDirs.Scanning"] = "Scanning…";
        _strings["SyncDirs.Summary"] = "Total {0} | equal {1} | differ {2} | only-left {3} | only-right {4}";
        _strings["SyncDirs.NothingToCopy"] = "Nothing selected to copy.";
        _strings["SyncDirs.StatusEqual"] = "Equal";
        _strings["SyncDirs.StatusSize"] = "Size differs";
        _strings["SyncDirs.StatusTime"] = "Time differs";
        _strings["SyncDirs.StatusType"] = "Type differs";
        _strings["SyncDirs.StatusLeftOnly"] = "Only on left";
        _strings["SyncDirs.StatusRightOnly"] = "Only on right";

        // ═══ Checksum ═══
        _strings["Checksum.Title"] = "Checksum";
        _strings["Checksum.FileName"] = "File";
        _strings["Checksum.FileSize"] = "Size";
        _strings["Checksum.Algorithm"] = "Algorithm:";
        _strings["Checksum.Hash"] = "Hash";
        _strings["Checksum.Calculate"] = "Calculate";
        _strings["Checksum.CopyToClipboard"] = "Copy Hash";
        _strings["Checksum.Calculating"] = "Calculating…";
        _strings["Checksum.Done"] = "Done — {0} file(s)";
        _strings["Checksum.Copied"] = "Hash copied to clipboard";
        _strings["Checksum.SelectFiles"] = "Select file(s) to calculate checksum.";
        _strings["Menu.Commands.Checksum"] = "Calculate &Checksum…";

        // ═══ Differ ═══
        _strings["Differ.Title"] = "File Compare";
        _strings["Differ.Left"] = "Left:";
        _strings["Differ.Right"] = "Right:";
        _strings["Differ.Compare"] = "Compare";
        _strings["Differ.FilesNotFound"] = "Both files must exist.";
        _strings["Differ.Summary"] = "Left: {0} lines | Right: {1} lines | Differences: {2}";
        _strings["Differ.FilterAll"] = "All files (*.*)|*.*|Text files (*.txt)|*.txt";
        _strings["Menu.Commands.Differ"] = "Compare &Files…";

        // ═══ Terminal ═══
        _strings["Terminal.SelectType"] = "Select Terminal Type";
        _strings["Terminal.Shell.Cmd"] = "Command Prompt (cmd.exe)";
        _strings["Terminal.Shell.WindowsPowerShell"] = "Windows PowerShell";
        _strings["Terminal.Shell.PowerShellCore"] = "PowerShell 7";
        _strings["Terminal.Shell.GitBash"] = "Git Bash";
        _strings["Terminal.Shell.Wsl"] = "WSL: {0}";
        _strings["Terminal.UnsupportedOs"] = "The embedded terminal needs Windows 10 version 1809 (build 17763) or later.";
        _strings["Terminal.Ctx.OpenLink"] = "Open Link";
        _strings["Terminal.Ctx.CopyLink"] = "Copy Link Address";
        _strings["Terminal.Ctx.ShowInPanel"] = "Show in Panel";
        _strings["Terminal.Ctx.Copy"] = "Copy";
        _strings["Terminal.Ctx.Paste"] = "Paste";
        _strings["Terminal.NewTab"] = "New Terminal Tab";
        _strings["Terminal.CloseTab"] = "Close Tab";
        _strings["Terminal.NextTab"] = "Next Tab";
        _strings["Terminal.PreviousTab"] = "Previous Tab";
        _strings["Terminal.MaxTabsReached"] = "Maximum number of terminal tabs reached ({0})";
        _strings["Terminal.ProcessTerminated"] = "Process terminated";
        _strings["Terminal.NoShellAvailable"] = "No terminal shell (cmd.exe or PowerShell) is available on this system";
        _strings["Settings.Terminal"] = "Terminal";
        _strings["Settings.DefaultShell"] = "Default shell:";
        _strings["Settings.Terminal.KeyBindingPreset"] = "Key bindings:";
        _strings["Settings.Terminal.KeyBindingPreset.WindowsTerminal"] = "Windows Terminal";
        _strings["Settings.Terminal.KeyBindingPreset.Classic"] = "Classic";
        _strings["Settings.Terminal.KeyBindingPreset.Custom"] = "Custom";
        _strings["Settings.Terminal.Customize"] = "Customize…";
        _strings["Settings.Terminal.FollowPanelCwd"] = "Sync panel path to terminal:";
        _strings["Settings.Terminal.FollowPanelCwd.Never"] = "Never";
        _strings["Settings.Terminal.FollowPanelCwd.OnOpen"] = "When terminal opens";
        _strings["Settings.Terminal.FollowPanelCwd.Always"] = "Always";
        _strings["Settings.Terminal.LoadShellProfile"] = "Load PowerShell profile (oh-my-posh, PSReadLine, …)";
        _strings["Settings.Terminal.KeyBindings"] = "Terminal Key Bindings";
        _strings["Settings.Terminal.KeyBindings.Action"] = "Action";
        _strings["Settings.Terminal.KeyBindings.Shortcut"] = "Shortcut";
        _strings["Settings.Terminal.KeyBindings.Hint"] = "Double-click a row to set a new shortcut, or Escape to cancel.";
        _strings["Settings.Terminal.KeyBindings.Clear"] = "Clear Shortcut";
        _strings["Settings.Terminal.KeyBindings.ResetAll"] = "Reset All to Defaults";
        _strings["Settings.Terminal.KeyBindings.PressKeys"] = "Press a key combination…";
        _strings["Settings.Terminal.KeyBindings.ConflictConfirm"] = "\"{0}\" is already used by \"{1}\". Reassign it to this action instead?";
    }
}
