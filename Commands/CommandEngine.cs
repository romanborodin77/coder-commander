namespace CoderCommander.Commands;

/// <summary>
//// Every user action — hotkey, menu, toolbar — resolves to one of these.
/// </summary>
public static class CommandIds
{
    // File operations
    public const string View = "cm_View";
    public const string Edit = "cm_Edit";
    public const string Copy = "cm_Copy";
    public const string Move = "cm_Move";
    public const string Rename = "cm_Rename";
    public const string MakeDir = "cm_MakeDir";
    public const string Delete = "cm_Delete";
    public const string Wipe = "cm_Wipe";

    // Batch operations
    public const string MultiRename = "cm_MultiRename";
    public const string EditNew = "cm_EditNew";
    public const string PackFiles = "cm_PackFiles";
    public const string UnpackFiles = "cm_UnpackFiles";
    public const string Checksum = "cm_Checksum";

    // Navigation
    public const string GoToParent = "cm_GoToParent";
    public const string GoToRoot = "cm_GoToRoot";
    public const string GoToHome = "cm_GoToHome";
    public const string Refresh = "cm_Refresh";
    public const string ChangeDir = "cm_ChangeDir";

    // Selection
    public const string SelectAll = "cm_SelectAll";
    public const string DeselectAll = "cm_DeselectAll";
    public const string InvertSelection = "cm_InvertSelection";
    public const string SelectGroup = "cm_SelectGroup";
    public const string DeselectGroup = "cm_DeselectGroup";

    // Panel
    public const string SwapPanels = "cm_SwapPanels";
    public const string TargetEqualSource = "cm_TargetEqualSource";
    public const string ToggleHidden = "cm_ToggleHidden";
    public const string ToggleFlatView = "cm_ToggleFlatView";

    // View
    public const string SetTheme = "cm_SetTheme";
    public const string SetSortColumn = "cm_SetSortColumn";
    public const string SetSortDescending = "cm_SetSortDescending";
    public const string SetDirectoriesFirst = "cm_SetDirectoriesFirst";
    public const string ToggleShowExtensionInName = "cm_ToggleShowExtensionInName";

    // Application
    public const string Exit = "cm_Exit";
    public const string ToggleTerminal = "cm_ToggleTerminal";
    public const string CreateTerminalTab = "cm_CreateTerminalTab";
    public const string CloseTerminalTab = "cm_CloseTerminalTab";
    public const string NextTerminalTab = "cm_NextTerminalTab";
    public const string PreviousTerminalTab = "cm_PreviousTerminalTab";
    // Intentionally not wired through CommandEngine/HotkeyManager/menu: F2 is already bound
    // globally to file rename, and reusing it while terminal-focused would need the same
    // ProcessCmdKey interception EmbeddedTerminalPanel uses for Ctrl+T/W/Tab, just for a minor
    // nicety. Renaming a terminal tab is done by right-clicking its header instead - see
    // EmbeddedTerminalPanel.TabControl_TabRightClicked.
    public const string RenameTerminalTab = "cm_RenameTerminalTab";
    public const string ShowProperties = "cm_ShowProperties";
    public const string About = "cm_About";
}

/// <summary>
/// A command with optional string parameters.
/// </summary>
public readonly record struct Command(string Id, string? Param = null);

/// <summary>
/// Command handler delegate.
/// </summary>
public delegate void CommandHandler(string? param);

/// <summary>
//// Decouples input (hotkeys, menus, toolbar) from implementation.
/// </summary>
public sealed class CommandEngine
{
    private readonly Dictionary<string, CommandHandler> _handlers = new();

    public void Register(string commandId, CommandHandler handler)
    {
        _handlers[commandId] = handler;
    }

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

    public bool Execute(string commandId, string? param = null)
        => Execute(new Command(commandId, param));

    public IEnumerable<string> RegisteredCommands => _handlers.Keys;
}
