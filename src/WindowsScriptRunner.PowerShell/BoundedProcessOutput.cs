using System.Text;

namespace WindowsScriptRunner.PowerShell;

internal sealed class BoundedProcessOutput : IDisposable
{
    private const int BufferSize = 4096;
    private readonly object _sync = new();
    private readonly int _standardOutputLimit;
    private readonly int _standardErrorLimit;
    private readonly int _combinedLimit;
    private readonly MemoryStream _standardOutput = new();
    private readonly MemoryStream _standardError = new();
    private readonly TaskCompletionSource _limitExceeded =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _standardOutputBytes;
    private long _standardErrorBytes;
    private bool _standardOutputTruncated;
    private bool _standardErrorTruncated;
    private bool _storeOutput = true;

    public BoundedProcessOutput(
        int standardOutputLimit,
        int standardErrorLimit,
        int combinedLimit)
    {
        _standardOutputLimit = standardOutputLimit;
        _standardErrorLimit = standardErrorLimit;
        _combinedLimit = combinedLimit;
    }

    public Task LimitExceeded => _limitExceeded.Task;

    public Task PumpStandardOutputAsync(Stream stream) => PumpAsync(stream, true);

    public Task PumpStandardErrorAsync(Stream stream) => PumpAsync(stream, false);

    public void StopStoring()
    {
        lock (_sync)
        {
            _storeOutput = false;
        }
    }

    public CapturedProcessOutput Snapshot()
    {
        lock (_sync)
        {
            return new CapturedProcessOutput(
                Encoding.UTF8.GetString(_standardOutput.ToArray()),
                Encoding.UTF8.GetString(_standardError.ToArray()),
                _standardOutputBytes,
                _standardErrorBytes,
                _standardOutputTruncated,
                _standardErrorTruncated);
        }
    }

    public void Dispose()
    {
        _standardOutput.Dispose();
        _standardError.Dispose();
    }

    private async Task PumpAsync(Stream stream, bool isStandardOutput)
    {
        var buffer = new byte[BufferSize];
        while (true)
        {
            var count = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (count == 0)
            {
                return;
            }

            Record(buffer, count, isStandardOutput);
        }
    }

    private void Record(byte[] buffer, int count, bool isStandardOutput)
    {
        lock (_sync)
        {
            var combinedBefore = _standardOutputBytes + _standardErrorBytes;
            var streamBefore = isStandardOutput
                ? _standardOutputBytes
                : _standardErrorBytes;
            var streamLimit = isStandardOutput
                ? _standardOutputLimit
                : _standardErrorLimit;
            var streamExceeded = streamBefore + count > streamLimit;
            var combinedExceeded = combinedBefore + count > _combinedLimit;

            if (isStandardOutput)
            {
                _standardOutputBytes += count;
            }
            else
            {
                _standardErrorBytes += count;
            }

            if (_storeOutput)
            {
                var target = isStandardOutput ? _standardOutput : _standardError;
                var streamRemaining = Math.Max(0, streamLimit - (int)target.Length);
                var combinedStored = checked((int)(_standardOutput.Length + _standardError.Length));
                var combinedRemaining = Math.Max(0, _combinedLimit - combinedStored);
                var storeCount = Math.Min(count, Math.Min(streamRemaining, combinedRemaining));
                if (storeCount > 0)
                {
                    target.Write(buffer, 0, storeCount);
                }
            }

            if (!streamExceeded && !combinedExceeded)
            {
                return;
            }

            if (isStandardOutput)
            {
                _standardOutputTruncated = true;
            }
            else
            {
                _standardErrorTruncated = true;
            }

            _storeOutput = false;
            _limitExceeded.TrySetResult();
        }
    }
}

internal sealed record CapturedProcessOutput(
    string StandardOutput,
    string StandardError,
    long StandardOutputBytes,
    long StandardErrorBytes,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated);
