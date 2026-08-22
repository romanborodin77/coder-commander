using CoderCommander.Commands;

namespace CoderCommander.Views;

/// <summary>
/// One customizable button: a <see cref="Commands.CommandEngine"/> command id paired with the
/// icon/tooltip (or, for the function bar, icon/label) it's always shown with - icon and text are
/// properties of the COMMAND, not something a user picks per button, so the catalog carries them
/// rather than <see cref="Services.SettingsService.AppSettings"/> needing to store localized text.
/// </summary>
public readonly record struct ToolbarButtonSpec(string CommandId, string IconKey, string LabelKey);

/// <summary>
/// Every command offerable as a main-toolbar or function-bar button (F5.2), plus each bar's
/// default layout - what <c>MainForm.BuildToolbar</c>/<c>BuildFunctionButtons</c> fall back to
/// when <c>AppSettings.ToolbarButtons</c>/<c>FunctionBarButtons</c> is empty (a fresh install, or a
/// settings file from before this feature existed), so upgrading never changes anyone's toolbar
/// until they actually open the editor. Extracted from the hardcoded sequences both methods used
/// to build inline - unlike a hardcoded sequence, a settings-driven list needs a lookup from
/// "which command is this" back to "what icon/label does it get", which is exactly what this
/// catalog is for.
/// </summary>
public static class ToolbarButtonCatalog
{
    /// <summary>Sentinel layout entry for a visual separator - meaningful only in
    /// <see cref="ToolbarLayout"/>/the main toolbar (a thin divider between button groups); the
    /// function bar has never had one and its editor doesn't offer it.</summary>
    public const string Separator = "|sep|";

    /// <summary>Commands offerable on the main (icon-only) toolbar.</summary>
    public static readonly IReadOnlyList<ToolbarButtonSpec> ToolbarCommands =
    [
        new(CommandIds.GoBack, "back", "Toolbar.Back"),
        new(CommandIds.GoForward, "forward", "Toolbar.Forward"),
        new(CommandIds.GoToParent, "up", "Toolbar.Up"),
        new(CommandIds.Copy, "copy", "Toolbar.Copy"),
        new(CommandIds.Move, "move", "Toolbar.Move"),
        new(CommandIds.Delete, "delete", "Toolbar.Delete"),
        new(CommandIds.MakeDir, "newdir", "Toolbar.NewDir"),
        new(CommandIds.FindFiles, "search", "Toolbar.Search"),
        new(CommandIds.Refresh, "refresh", "Toolbar.Refresh"),
        new(CommandIds.OpenSettings, "settings", "Toolbar.Settings"),
    ];

    /// <summary>Default main-toolbar layout - identical to the hardcoded sequence this feature
    /// replaced, so nothing visibly changes for a settings file that predates it.</summary>
    public static readonly IReadOnlyList<string> DefaultToolbarLayout =
    [
        CommandIds.GoBack, CommandIds.GoForward, CommandIds.GoToParent, Separator,
        CommandIds.Copy, CommandIds.Move, CommandIds.Delete, CommandIds.MakeDir, Separator,
        CommandIds.FindFiles, CommandIds.Refresh, Separator,
        CommandIds.OpenSettings,
    ];

    /// <summary>Commands offerable on the function (F-key) bar.</summary>
    public static readonly IReadOnlyList<ToolbarButtonSpec> FunctionBarCommands =
    [
        new(CommandIds.View, "view", "Fn.View"),
        new(CommandIds.Edit, "edit", "Fn.Edit"),
        new(CommandIds.Copy, "copy", "Fn.Copy"),
        new(CommandIds.Move, "move", "Fn.Move"),
        new(CommandIds.MakeDir, "newdir", "Fn.MkDir"),
        new(CommandIds.Delete, "delete", "Fn.Delete"),
        new(CommandIds.ToggleTerminal, "terminal", "Fn.Terminal"),
        new(CommandIds.Exit, "exit", "Fn.Exit"),
    ];

    /// <summary>Default function-bar layout - identical to the hardcoded sequence this feature
    /// replaced.</summary>
    public static readonly IReadOnlyList<string> DefaultFunctionBarLayout =
    [
        CommandIds.View, CommandIds.Edit, CommandIds.Copy, CommandIds.Move,
        CommandIds.MakeDir, CommandIds.Delete, CommandIds.ToggleTerminal, CommandIds.Exit,
    ];

    /// <summary>Looks up a toolbar command's spec by id, or <c>null</c> if <paramref name="commandId"/>
    /// isn't (or is no longer) in <see cref="ToolbarCommands"/> - a settings file can reference a
    /// command id that a later version renamed/removed; the caller skips it rather than crashing.</summary>
    public static ToolbarButtonSpec? FindToolbarCommand(string commandId) =>
        Find(ToolbarCommands, commandId);

    /// <summary>See <see cref="FindToolbarCommand"/> - the function-bar counterpart.</summary>
    public static ToolbarButtonSpec? FindFunctionBarCommand(string commandId) =>
        Find(FunctionBarCommands, commandId);

    private static ToolbarButtonSpec? Find(IReadOnlyList<ToolbarButtonSpec> catalog, string commandId)
    {
        foreach (var spec in catalog)
            if (spec.CommandId == commandId)
                return spec;
        return null;
    }
}
