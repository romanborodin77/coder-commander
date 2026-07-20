using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Coder Commander")]
[assembly: AssemblyDescription("Dual-panel file manager for programmers")]
[assembly: AssemblyCompany("CoderCommander")]
[assembly: AssemblyProduct("Coder Commander")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: ComVisible(false)]

// Lets UiTests call internal-only members that operate purely on in-memory objects (no file I/O)
// directly, instead of exercising them through SettingsService.Load()/Save() - which would read
// and write the real %AppData%\CoderCommander\settings.json on whatever machine runs the tests.
[assembly: InternalsVisibleTo("CoderCommander.UiTests")]
