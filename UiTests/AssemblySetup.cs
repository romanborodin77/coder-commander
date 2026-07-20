using System.Text;
using CoderCommander.Archives;
using CoderCommander.Archives.SharpCompress;
using CoderCommander.Archives.Tar;
using CoderCommander.Archives.Zip;

namespace CoderCommander.UiTests;

/// <summary>
/// CoderCommander.Program.Main() registers CodePagesEncodingProvider (needed for CP866, the OEM
/// codepage ZipArchiveFileSystem uses for legacy archive names) and the archive format registry
/// before anything else runs. Tests that touch ZipArchiveFileSystem or ArchiveFormatRegistry
/// directly (bypassing Main entirely) need the same registration, or lookups silently miss /
/// the CP866 static constructor throws NotSupportedException the first time it's touched.
/// </summary>
[SetUpFixture]
public class AssemblySetup
{
    [OneTimeSetUp]
    public void RegisterCodePages()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ArchiveFormatRegistry.Register(ZipArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(TarArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(TarGzArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(SevenZipArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(RarArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(TarBz2ArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(TarXzArchiveFormat.Instance);
    }
}
