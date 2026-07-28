namespace WindowsScriptRunner.Worker;

public interface IHeartbeatTimer : IAsyncDisposable
{
    ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken);
}
