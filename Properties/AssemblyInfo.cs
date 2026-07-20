// AssemblyInfo.cs — явное описание сборки (вместо генерируемого SDK).
// Explicit assembly metadata (replaces SDK-generated AssemblyInfo).
// Нужно, т.к. автогенерация отключена из-за конфликта wpftmp.csproj (CS0579) при сборке WPF через CLI.
// Required because auto-generation is disabled to avoid the wpftmp.csproj duplicate-attribute (CS0579) under CLI WPF builds.

using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: AssemblyTitle("CoderCommander")]
[assembly: AssemblyDescription("Двухпанельный файловый менеджер для программиста / Two-panel file manager for developers")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("CoderCommander")]
[assembly: AssemblyProduct("CoderCommander")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0.0")]

// Целевая платформа / Target framework (net8.0-windows).
[assembly: TargetFramework(".NETCoreApp,Version=v8.0", FrameworkDisplayName = ".NET 8.0")]
[assembly: TargetPlatform("Windows")]
