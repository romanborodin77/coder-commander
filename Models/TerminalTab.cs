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

    /// <summary>Type of shell (cmd.exe or PowerShell).</summary>
    public ShellType ShellType { get; }

    /// <summary>Current working directory for this terminal session.</summary>
    public string CurrentPath { get; set; }

    /// <summary>Is this tab currently active?</summary>
    public bool IsActive { get; set; }

    /// <summary>Has this tab been closed/disposed?</summary>
    public bool IsDisposed { get; set; }

    /// <summary>Creation time of this tab.</summary>
    public DateTime CreatedAt { get; }

    /// <summary>Initialize a new terminal tab.</summary>
    public TerminalTab(ShellType shellType, string currentPath = "")
    {
        Id = Guid.NewGuid();
        ShellType = shellType;
        CurrentPath = string.IsNullOrEmpty(currentPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : currentPath;
        Name = $"{shellType.GetDisplayName()}";
        IsActive = false;
        IsDisposed = false;
        CreatedAt = DateTime.Now;
    }

    /// <summary>Get display name for UI.</summary>
    public string GetDisplayName() => Name;

    public override string ToString() =>
        $"TerminalTab(Id={Id:N}, Name={Name}, Shell={ShellType}, Path={CurrentPath}, Disposed={IsDisposed})";
}
