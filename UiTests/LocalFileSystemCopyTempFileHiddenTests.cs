using CoderCommander.FileSystem;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the bug fixed in <see cref="LocalFileSystem.CopyFromStreamAsync"/>: the
/// ".tmp-&lt;32 hex chars&gt;" staging file it writes to before renaming into place used to sit
/// visibly in the destination folder, indistinguishable from a real file, for as long as the
/// transfer took.
/// </summary>
public class LocalFileSystemCopyTempFileHiddenTests
{
    /// <summary>Blocks the first Read until <see cref="Release"/> is called, so the test can
    /// inspect the destination directory mid-copy.</summary>
    private sealed class GatedStream : Stream
    {
        private readonly byte[] _data;
        private int _position;
        private readonly TaskCompletionSource _gate = new();
        public Task ReadStarted => _readStarted.Task;
        private readonly TaskCompletionSource _readStarted = new();

        public GatedStream(byte[] data) => _data = data;
        public void Release() => _gate.TrySetResult();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            _readStarted.TrySetResult();
            await _gate.Task.ConfigureAwait(false);
            var toCopy = Math.Min(count, _data.Length - _position);
            Array.Copy(_data, _position, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Test]
    public async Task CopyFromStreamAsync_WhileInProgress_TempFileIsHidden()
    {
        var destDir = Directory.CreateTempSubdirectory("cc_copy_temp_hidden_").FullName;
        try
        {
            var destPath = Path.Combine(destDir, "target.bin");
            var fs = new LocalFileSystem();
            using var source = new GatedStream(new byte[] { 1, 2, 3, 4 });

            var copyTask = fs.CopyFromStreamAsync(destPath, source);
            await source.ReadStarted;

            var tempFile = Directory.EnumerateFiles(destDir).SingleOrDefault(f => Path.GetFileName(f).Contains(".tmp-", StringComparison.Ordinal));
            Assert.That(tempFile, Is.Not.Null, "The staging temp file must exist while the copy is in progress");
            Assert.That(File.GetAttributes(tempFile!).HasFlag(FileAttributes.Hidden), Is.True,
                "The in-progress staging temp file must be hidden, not sit visibly next to real destination files");

            source.Release();
            await copyTask;
        }
        finally
        {
            Directory.Delete(destDir, recursive: true);
        }
    }
}
