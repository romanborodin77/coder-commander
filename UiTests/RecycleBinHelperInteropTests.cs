using System.Reflection;
using System.Runtime.InteropServices;
using CoderCommander.FileSystem;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the interop mismatch fixed on <see cref="RecycleBinHelper"/>'s
/// SHEmptyRecycleBinW P/Invoke declaration: it was missing CharSet.Unicode, so a rootPath with
/// non-ANSI characters would get silently mangled by the marshaller before reaching a function
/// whose "W" suffix and wchar_t* signature both promise wide characters. Checks the declaration's
/// metadata directly via reflection rather than calling Empty() itself, since that call would
/// actually empty the real Windows Recycle Bin - not something a test may ever do.
/// </summary>
public class RecycleBinHelperInteropTests
{
    [Test]
    public void SHEmptyRecycleBinW_DllImport_DeclaresUnicodeCharSet()
    {
        var method = typeof(RecycleBinHelper).GetMethod("SHEmptyRecycleBinW", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null, "RecycleBinHelper must still declare a private static SHEmptyRecycleBinW P/Invoke method");

        var attr = method!.GetCustomAttribute<DllImportAttribute>();
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr!.CharSet, Is.EqualTo(CharSet.Unicode),
            "SHEmptyRecycleBinW must marshal strings as Unicode to match its 'W' (wide-char) Win32 signature");
    }
}
