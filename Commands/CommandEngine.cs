namespace CoderCommander.Commands;

/// <summary>
/// Every user action — hotkey, menu, toolbar — resolves to one of these command identifiers.
/// </summary>
public static class CommandIds
{
    // File operations
    /// <summary>View file contents (F3).</summary>
    public const string View = "cm_View";
    /// <summary>Edit file in external editor (F4).</summary>
    public const string Edit = "cm_Edit";
    /// <summary>Copy selected files (F5).</summary>
    public const string Copy = "cm_Copy";
    /// <summary>Move/rename selected files (F6).</summary>
    public const string Move = "cm_Move";
    /// <summary>Rename a single file (F2).</summary>
    public const string Rename = "cm_Rename";
    /// <summary>Create a new directory (F7).</summary>
    public const string MakeDir = "cm_MakeDir";
    /// <summary>Delete selected files (F8).</summary>
    public const string Delete = "cm_Delete";
    /// <summary>Securely wipe selected files (Shift+F8).</summary>
    public const string Wipe = "cm_Wipe";

    // Batch operations
    /// <summary>Multi-file rename dialog (Ctrl+M).</summary>
    public const string MultiRename = "cm_MultiRename";
    /// <summary>Create and open a new empty file for editing (Shift+F4).</summary>
    public const string EditNew = "cm_EditNew";
    /// <summary>Pack files into an archive (Alt+F5).</summary>
    public const string PackFiles = "cm_PackFiles";
    /// <summary>Unpack/extract an archive (Alt+F9).</summary>
    public const string UnpackFiles = "cm_UnpackFiles";
    /// <summary>Compute checksums for selected files.</summary>
    public const string Checksum = "cm_Checksum";

    // Navigation
    /// <summary>Navigate to the parent directory (Backspace).</summary>
    public const string GoToParent = "cm_GoToParent";
    /// <summary>Navigate to the drive root (Ctrl+\).</summary>
    public const string GoToRoot = "cm_GoToRoot";
    /// <summary>Navigate to the user's home directory (Ctrl+Home).</summary>
    public const string GoToHome = "cm_GoToHome";
    /// <summary>Refresh the current directory listing (Ctrl+R).</summary>
    public const string Refresh = "cm_Refresh";
    /// <summary>Change directory by path input (Ctrl+G).</summary>
    public const string ChangeDir = "cm_ChangeDir";

    // Selection
    /// <summary>Select all items in the current panel (Ctrl+A).</summary>
    public const string SelectAll = "cm_SelectAll";
    /// <summary>Deselect all items in the current panel (Ctrl+D).</summary>
    public const string DeselectAll = "cm_DeselectAll";
    /// <summary>Invert the current selection (Num+).</summary>
    public const string InvertSelection = "cm_InvertSelection";
    /// <summary>Select a group of items by pattern (Ctrl+Num+).</summary>
    public const string SelectGroup = "cm_SelectGroup";
    /// <summary>Deselect a group of items by pattern (Ctrl+Num-).</summary>
    public const string DeselectGroup = "cm_DeselectGroup";

    // Panel
    /// <summary>Swap the source and target panels (Ctrl+U).</summary>
    public const string SwapPanels = "cm_SwapPanels";
    /// <summary>Set the target panel path equal to the source panel path.</summary>
    public const string TargetEqualSource = "cm_TargetEqualSource";
    /// <summary>Toggle visibility of hidden/system files (Ctrl+.).</summary>
    public const string ToggleHidden = "cm_ToggleHidden";
    /// <summary>Toggle flat (recursive) view mode (Ctrl+P).</summary>
    public const string ToggleFlatView = "cm_ToggleFlatView";

    // View
    /// <summary>Switch the application theme (Ctrl+1 for Dark, Ctrl+2 for Light).</summary>
    public const string SetTheme = "cm_SetTheme";
    /// <summary>Change the sort column of the active panel.</summary>
    public const string SetSortColumn = "cm_SetSortColumn";
    /// <summary>Toggle ascending/descending sort order.</summary>
    public const string SetSortDescending = "cm_SetSortDescending";
    /// <summary>Toggle the directories-first sort option.</summary>
    public const string SetDirectoriesFirst = "cm_SetDirectoriesFirst";
    /// <summary>Toggle showing file extensions in the Name column.</summary>
    public const string ToggleShowExtensionInName = "cm_ToggleShowExtensionInName";

    // Application
    /// <summary>Exit the application (F10 or Alt+X).</summary>
    public const string Exit = "cm_Exit";
    /// <summary>Toggle the embedded terminal panel visibility (F9).</summary>
    public const string ToggleTerminal = "cm_ToggleTerminal";
    /// <summary>Create a new terminal tab (Ctrl+T).</summary>
    public const string CreateTerminalTab = "cm_CreateTerminalTab";
    /// <summary>Close the active terminal tab (Ctrl+W).</summary>
    public const string CloseTerminalTab = "cm_CloseTerminalTab";
    /// <summary>Switch to the next terminal tab (Ctrl+Tab).</summary>
    public const string NextTerminalTab = "cm_NextTerminalTab";
    /// <summary>Switch to the previous terminal tab (Ctrl+Shift+Tab).</summary>
    public const string PreviousTerminalTab = "cm_PreviousTerminalTab";
    /// <summary>Rename the active terminal tab. Not wired through
    /// <see cref="CommandEngine"/> — use right-click context menu instead.</summary>
    public const string RenameTerminalTab = "cm_RenameTerminalTab";
    /// <summary>Show file/folder properties dialog (Alt+Enter).</summary>
    public const string ShowProperties = "cm_ShowProperties";
    /// <summary>Recursively compute and display the total size of selected directories.</summary>
    public const string CalculateFolderSize = "cm_CalculateFolderSize";
    /// <summary>Show the About dialog.</summary>
    public const string About = "cm_About";
}

/// <summary>
/// A command with optional string parameters, passed through the <see cref="CommandEngine"/> pipeline.
/// </summary>
/// <param name="Id">The command identifier from <see cref="CommandIds"/>.</param>
/// <param name="Param">Optional parameter string (e.g. theme name, sort column).</param>
public readonly record struct Command(string Id, string? Param = null);

/// <summary>
/// Delegate that handles a command execution.
/// </summary>
/// <param name="param">Optional parameter passed from the <see cref="Command"/>.</param>
public delegate void CommandHandler(string? param);

/// <summary>
/// Decouples input sources (hotkeys, menus, toolbar) from command implementation via a
/// registry of <see cref="CommandHandler"/> delegates keyed by <see cref="CommandIds"/>.
/// </summary>
public sealed class CommandEngine
{
    private readonly Dictionary<string, CommandHandler> _handlers = new();

    /// <summary>Registers a handler for the specified command identifier.</summary>
    /// <param name="commandId">The command identifier from <see cref="CommandIds"/>.</param>
    /// <param name="handler">The delegate to invoke when the command is executed.</param>
    public void Register(string commandId, CommandHandler handler)
    {
        _handlers[commandId] = handler;
    }

    /// <summary>Executes a <see cref="Command"/> by looking up its handler and invoking it.
    /// Logs success or failure via <see cref="Services.LogService"/>.</summary>
    /// <param name="command">The command to execute.</param>
    /// <returns><c>true</c> if a handler was found and executed; <c>false</c> otherwise.</returns>
    public bool Execute(Command command)
    {
        if (_handlers.TryGetValue(command.Id, out var handler))
        {
            try
            {
                handler(command.Param);
                Services.LogService.LogCommand(command.Id, "ok");
            }
            catch (Exception ex)
            {
                Services.LogService.LogCommand(command.Id, "failed");
                Services.LogService.Error($"Command '{command.Id}' failed: {ex.Message}", ex);
            }
            return true;
        }
        return false;
    }

    /// <summary>Convenience overload that creates a <see cref="Command"/> from a command ID
    /// and optional parameter, then executes it.</summary>
    /// <param name="commandId">The command identifier from <see cref="CommandIds"/>.</param>
    /// <param name="param">Optional parameter string.</param>
    /// <returns><c>true</c> if a handler was found and executed; <c>false</c> otherwise.</returns>
    public bool Execute(string commandId, string? param = null)
        => Execute(new Command(commandId, param));

    /// <summary>Returns all command identifiers that have registered handlers.</summary>
    public IEnumerable<string> RegisteredCommands => _handlers.Keys;
}
