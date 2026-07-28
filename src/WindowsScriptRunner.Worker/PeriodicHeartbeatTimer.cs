namespace WindowsScriptRunner.Worker;

public sealed class PeriodicHeartbeatTimer(TimeSpan interval) : IHeartbeatTimer
{
    private readonly PeriodicTimer _timer = new(interval);

    public ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken) =>
        _timer.WaitForNextTickAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        _timer.Dispose();
        return ValueTask.CompletedTask;
    }
}
