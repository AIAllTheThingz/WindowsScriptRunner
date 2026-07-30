using System.Text;
using WindowsScriptRunner.PowerShell;

namespace WindowsScriptRunner.PowerShellTests;

public sealed class BoundedProcessOutputTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SnapshotTrimsIncompleteUtf8FromEitherStream(bool standardOutput)
    {
        var bytes = Encoding.UTF8.GetBytes("✓");
        using var stream = new MemoryStream(bytes);
        using var capture = new BoundedProcessOutput(1, 1, 2);

        if (standardOutput)
        {
            await capture.PumpStandardOutputAsync(stream);
        }
        else
        {
            await capture.PumpStandardErrorAsync(stream);
        }

        var output = capture.Snapshot();
        var capturedText = standardOutput
            ? output.StandardOutput
            : output.StandardError;
        var capturedBytes = standardOutput
            ? output.StandardOutputBytes
            : output.StandardErrorBytes;

        Assert.Equal(string.Empty, capturedText);
        Assert.Equal(bytes.Length, capturedBytes);
        Assert.DoesNotContain("\uFFFD", capturedText, StringComparison.Ordinal);
        Assert.True(
            standardOutput
                ? output.StandardOutputTruncated
                : output.StandardErrorTruncated);
        Assert.True(Encoding.UTF8.GetByteCount(capturedText) <= 1);
    }

    [Fact]
    public async Task SnapshotPreservesLastCompleteUtf8CharacterWithinLimit()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("A✓B"));
        using var capture = new BoundedProcessOutput(4, 4, 8);

        await capture.PumpStandardOutputAsync(stream);
        var output = capture.Snapshot();

        Assert.Equal("A✓", output.StandardOutput);
        Assert.Equal(4, Encoding.UTF8.GetByteCount(output.StandardOutput));
        Assert.True(output.StandardOutputTruncated);
    }

    [Fact]
    public async Task DiscardedBytesMarkEachAffectedStreamAsTruncated()
    {
        using var standardOutput = new MemoryStream(Encoding.UTF8.GetBytes("ab"));
        using var standardError = new MemoryStream(Encoding.UTF8.GetBytes("discarded"));
        using var capture = new BoundedProcessOutput(1, 1024, 2048);

        await capture.PumpStandardOutputAsync(standardOutput);
        await capture.PumpStandardErrorAsync(standardError);
        var output = capture.Snapshot();

        Assert.True(output.StandardOutputTruncated);
        Assert.True(output.StandardErrorTruncated);
        Assert.Equal(string.Empty, output.StandardError);
        Assert.Equal(Encoding.UTF8.GetByteCount("discarded"), output.StandardErrorBytes);
    }

    [Fact]
    public async Task DisposeDoesNotRaceWithInFlightPumpStorage()
    {
        using var stream = new DelayedSingleReadStream("captured");
        using var capture = new BoundedProcessOutput(1024, 1024, 2048);
        var pump = capture.PumpStandardOutputAsync(stream);
        await stream.ReadStarted;

        capture.Dispose();
        stream.ReleaseRead();

        await pump;
    }

    private sealed class DelayedSingleReadStream(string value) : Stream
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(value);
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readCount;

        public Task ReadStarted => _readStarted.Task;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void ReleaseRead() => _releaseRead.TrySetResult();

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _readCount) > 1)
            {
                return 0;
            }

            _readStarted.TrySetResult();
            await _releaseRead.Task.WaitAsync(cancellationToken);
            _bytes.CopyTo(buffer);
            return _bytes.Length;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
