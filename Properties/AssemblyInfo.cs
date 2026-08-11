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

// Audit Phase 6, CA5392 (AUDIT-FINDINGS.md §3): every P/Invoke in this app targets a well-known
// Windows system DLL (kernel32/shell32/user32/imm32/dwmapi/uxtheme) by bare name with no path -
// without this, the OS default search order can be tricked into loading a same-named DLL planted
// in the process's working directory or an earlier PATH entry instead of the real system one
// (CWE-427). Restricting the search to System32 covers every DllImport/LibraryImport in the
// assembly at once, including the source-generated LibraryImport sites - this attribute is honored
// by both, not just classic DllImport.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

// Lets UiTests call internal-only members that operate purely on in-memory objects (no file I/O)
// directly, instead of exercising them through SettingsService.Load()/Save() - which would read
// and write the real %AppData%\CoderCommander\settings.json on whatever machine runs the tests.
[assembly: InternalsVisibleTo("CoderCommander.UiTests")]
