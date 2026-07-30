namespace WindowsScriptRunner.Worker;

public interface IWorkerDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemWorkerDelay : IWorkerDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

public interface IWorkerRandom
{
    double NextDouble();
}

public sealed class SystemWorkerRandom : IWorkerRandom
{
    public double NextDouble() => Random.Shared.NextDouble();
}

internal sealed class WorkerBackoff(
    TimeSpan initialDelay,
    TimeSpan maximumDelay,
    IWorkerRandom random)
{
    private readonly TimeSpan _initialDelay = initialDelay > TimeSpan.Zero
        ? initialDelay
        : throw new ArgumentOutOfRangeException(nameof(initialDelay));
    private readonly TimeSpan _maximumDelay = maximumDelay >= initialDelay
        ? maximumDelay
        : throw new ArgumentOutOfRangeException(nameof(maximumDelay));
    private readonly IWorkerRandom _random = random;
    private int _failureCount;

    internal TimeSpan Next()
    {
        var multiplier = Math.Pow(2, Math.Min(_failureCount++, 20));
        var boundedMilliseconds = Math.Min(
            _initialDelay.TotalMilliseconds * multiplier,
            _maximumDelay.TotalMilliseconds);
        var jitter = 0.9 + (Math.Clamp(_random.NextDouble(), 0, 1) * 0.2);
        return TimeSpan.FromMilliseconds(
            Math.Min(boundedMilliseconds * jitter, _maximumDelay.TotalMilliseconds));
    }

    internal void Reset() => _failureCount = 0;
}
