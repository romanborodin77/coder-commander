using CoderCommander.FileSystem;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the bug fixed in <see cref="LocalFileSystem.GetDriveSpaceAsync"/>: the old
/// implementation only recognized lettered drives via <see cref="DriveInfo"/>. A destination
/// under a mapped/substituted path whose root isn't a plain "X:\" - or, in production, any UNC
/// share - never matched any entry in DriveInfo.GetDrives() and silently fell through to (0, 0),
/// which UnpackOperation.RejectIfWouldExhaustDisk treats as "can't determine, skip the check" -
/// quietly disabling the decompression-bomb guard. The fix (GetDiskFreeSpaceExW) also has to work
/// when the destination path itself doesn't exist yet (RejectIfWouldExhaustDisk runs before
/// CreateDirectoryAsync), so this also covers that case directly against a real temp directory.
/// </summary>
public class LocalFileSystemDriveSpaceTests
{
    [Test]
    public async Task GetDriveSpaceAsync_ExistingLocalDirectory_ReturnsNonZero()
    {
        var fs = new LocalFileSystem();
        var (free, total) = await fs.GetDriveSpaceAsync(Path.GetTempPath());

        Assert.That(free, Is.GreaterThan(0), "Must report real free space for an ordinary local path, not the 'unknown' (0,0) fallback");
        Assert.That(total, Is.GreaterThan(0));
    }

    [Test]
    public async Task GetDriveSpaceAsync_NotYetCreatedSubdirectory_StillWalksUpToAnExistingAncestor()
    {
        var fs = new LocalFileSystem();
        var notYetCreated = Path.Combine(Path.GetTempPath(), $"cc_drivespace_missing_{Guid.NewGuid():N}", "nested");

        var (free, total) = await fs.GetDriveSpaceAsync(notYetCreated);

        Assert.That(free, Is.GreaterThan(0), "A destination that doesn't exist yet must still resolve to its existing ancestor's volume, not fall back to (0,0)");
        Assert.That(total, Is.GreaterThan(0));
    }

    // Documents the actual root cause the old DriveInfo-based implementation tripped over:
    // DriveInfo.GetDrives() only enumerates lettered local drives, so any lookup keyed off it can
    // never match a UNC root - not a hypothetical, this is exactly why GetDriveSpaceAsync fell
    // through to its (0,0) "unknown" fallback for every network destination. No UNC share is
    // reachable in this sandboxed test environment (confirmed: \\localhost\C$ isn't accessible
    // here even though it's a real Windows admin share), so the full GetDriveSpaceAsync(uncPath)
    // path isn't exercised end-to-end here - this pins down the specific .NET behavior the bug and
    // the fix both hinge on instead.
    [Test]
    public void DriveInfo_GetDrives_DoesNotRecognizeUncRoot()
    {
        const string uncRoot = @"\\server\share\";
        Assert.That(DriveInfo.GetDrives().Any(d => string.Equals(d.Name, uncRoot, StringComparison.OrdinalIgnoreCase)), Is.False);
    }
}
