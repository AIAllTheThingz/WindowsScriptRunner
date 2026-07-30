namespace WindowsScriptRunner.Worker;

public sealed class WorkerRuntimeState
{
    private readonly object _sync = new();
    private bool _registered;
    private bool _heartbeatHealthy;
    private bool _queuePollingHealthy;
    private DateTimeOffset? _lastSuccessfulPoll;
    private int _activeDispatchCount;

    public bool Registered { get { lock (_sync) { return _registered; } } }
    public bool HeartbeatHealthy { get { lock (_sync) { return _heartbeatHealthy; } } }
    public bool QueuePollingHealthy { get { lock (_sync) { return _queuePollingHealthy; } } }
    public DateTimeOffset? LastSuccessfulPoll { get { lock (_sync) { return _lastSuccessfulPoll; } } }
    public int ActiveDispatchCount { get { lock (_sync) { return _activeDispatchCount; } } }

    internal void MarkRegistered(DateTimeOffset heartbeatUtc)
    {
        lock (_sync)
        {
            _registered = true;
            _heartbeatHealthy = true;
        }
    }

    internal void MarkHeartbeat(bool healthy)
    {
        lock (_sync)
        {
            _heartbeatHealthy = healthy;
        }
    }

    internal void MarkPollSuccess(DateTimeOffset occurredUtc)
    {
        lock (_sync)
        {
            _queuePollingHealthy = true;
            _lastSuccessfulPoll = occurredUtc;
        }
    }

    internal void MarkPollFailure()
    {
        lock (_sync)
        {
            _queuePollingHealthy = false;
        }
    }

    internal void SetActiveDispatchCount(int count)
    {
        lock (_sync)
        {
            _activeDispatchCount = count;
        }
    }
}
