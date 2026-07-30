using System.Text;
using WindowsScriptRunner.PowerShell;

namespace WindowsScriptRunner.PowerShellTests;

public sealed class BoundedProcessOutputTests
{
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
