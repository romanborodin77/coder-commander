using CoderCommander.Terminal.Shells;

namespace CoderCommander.Models;

/// <summary>
/// Represents a single terminal tab session.
/// Each tab maintains its own shell process; output history lives in
/// EmbeddedTerminalPanel's own buffer dictionary, not on this model.
/// </summary>
public class TerminalTab
{
    /// <summary>Unique identifier for this tab.</summary>
    public Guid Id { get; }

    /// <summary>Display name of the tab (e.g., "cmd", "PowerShell #2").</summary>
    public string Name { get; set; }

    /// <summary>Which shell this tab runs.</summary>
    public ShellDescriptor Shell { get; }

    /// <summary>Current working directory for this terminal session.</summary>
    public string CurrentPath { get; set; }

    /// <summary>A panel->shell <c>cd</c> that <see cref="WinForms.EmbeddedTerminalPanel.SetWorkingDirectory"/>
    /// couldn't send immediately (the shell wasn't at an idle prompt) and is holding for the next
    /// <see cref="Terminal.Screen.TerminalScreen.BecameIdlePrompt"/> transition. Null when there's
    /// nothing pending. A later call while one is already pending simply overwrites it - only the
    /// most recent destination matters, so several quick panel navigations while a command is
    /// still running coalesce into a single <c>cd</c> once the shell is actually ready, instead of
    /// queuing (and eventually typing) every intermediate one.</summary>
    public string? PendingCwd { get; set; }

    /// <summary>Is this tab currently active?</summary>
    public bool IsActive { get; set; }

    /// <summary>Has this tab been closed/disposed?</summary>
    public bool IsDisposed { get; set; }

    /// <summary>Creation time of this tab.</summary>
    public DateTime CreatedAt { get; }

    /// <summary>Initialize a new terminal tab.</summary>
    public TerminalTab(ShellDescriptor shell, string displayName, string currentPath = "")
    {
        Id = Guid.NewGuid();
        Shell = shell;
        CurrentPath = string.IsNullOrEmpty(currentPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : currentPath;
        Name = displayName;
        IsActive = false;
        IsDisposed = false;
        CreatedAt = DateTime.Now;
    }

    /// <summary>Get display name for UI.</summary>
    public string GetDisplayName() => Name;

    public override string ToString() =>
        $"TerminalTab(Id={Id:N}, Name={Name}, Shell={Shell.Id}, Path={CurrentPath}, Disposed={IsDisposed})";
}
