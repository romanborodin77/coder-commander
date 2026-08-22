namespace CoderCommander.Models;

/// <summary>
/// A user-defined terminal shell (Settings ▸ Terminal ▸ Custom Shells), merged by
/// <see cref="Terminal.Shells.ShellCatalog"/> into the discovered shell list alongside the
/// built-in ones (cmd, PowerShell, pwsh, Git Bash, WSL). <see cref="Command"/> is resolved via
/// <see cref="Terminal.Shells.PathResolver.Which"/> - an absolute path to the executable, or a
/// bare command name looked up on <c>%PATH%</c> - deliberately never the current working
/// directory, which is the classic PATH-hijack vector this app's built-in shells avoid entirely
/// by using absolute, known install locations instead.
/// </summary>
public sealed class CustomShellDefinition
{
    /// <summary>Stable identity, independent of <see cref="Name"/> so renaming doesn't orphan a
    /// restored terminal tab's saved shell id. Generated once when the entry is created.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Display name shown in the shell picker and Settings list.</summary>
    public string Name { get; set; } = "";

    /// <summary>Absolute path or bare command name, resolved via <see cref="Terminal.Shells.PathResolver.Which"/>.</summary>
    public string Command { get; set; } = "";

    /// <summary>Space-separated startup arguments (no quoting support - an argument containing a
    /// space isn't expressible here, same limitation as a plain shell command line typed without
    /// quotes). Each resulting array element is still passed to the process as one argument, so
    /// this is a UI convenience limit, not an injection surface.</summary>
    public string Arguments { get; set; } = "";
}
