using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// F093: GenerateAssemblyInfo=false (set so this hand-written file doesn't collide with SDK
// auto-gen) also suppresses the SDK's own auto-generated [assembly: SupportedOSPlatform("windows7.0")]
// that a "-windows"-suffixed TargetFramework normally gets for free - without it, CA1416 (platform
// compatibility) has no way to know this assembly is Windows-only and flags every single WinForms
// API call in the app (Label, Font, ToolStripButton, ...) as a platform-compatibility violation.
// Restoring it here is what makes CA1416 usable again for its actual purpose: catching genuinely
// version-gated APIs like the ConPTY functions this app already manually guards via
// Utils.OsVersion.MinConPtyBuild, instead of a blanket NoWarn hiding both the noise and any real
// finding together.
[assembly: SupportedOSPlatform("windows7.0")]

[assembly: AssemblyTitle("Coder Commander")]
[assembly: AssemblyDescription("Dual-panel file manager for programmers")]
[assembly: AssemblyCompany("CoderCommander")]
[assembly: AssemblyProduct("Coder Commander")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: ComVisible(false)]

// Audit Phase 6, CA5392 (DEBUG.md §0.4): every P/Invoke in this app targets a well-known
// Windows system DLL (kernel32/shell32/user32/imm32/dwmapi/uxtheme) by bare name with no path -
// without this, the OS default search order can be tricked into loading a same-named DLL planted
// in the process's working directory or an earlier PATH entry instead of the real system one
// (CWE-427). Restricting the search to System32 covers every DllImport/LibraryImport in the
// assembly at once, including the source-generated LibraryImport sites - this attribute is honored
// by both, not just classic DllImport.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

// Lets UiTests' Sandbox reuse the same internal JobObject PtySession/ConPTY already use to scope
// process-tree teardown to exactly the app instance a test launched, instead of reimplementing it.
[assembly: InternalsVisibleTo("CoderCommander.UiTests")]
