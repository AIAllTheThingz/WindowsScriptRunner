using System.Diagnostics.Metrics;
using WindowsScriptRunner.Domain;

namespace WindowsScriptRunner.Worker;

public sealed class WorkerMetrics : IDisposable
{
    private readonly Meter _meter = new("WindowsScriptRunner.Worker");
    private readonly WorkerRuntimeState _state;
    private readonly Counter<long> _queuePolls;
    private readonly Counter<long> _claims;
    private readonly Counter<long> _claimConflicts;
    private readonly Counter<long> _emptyPolls;
    private readonly Counter<long> _dispatchStarted;
    private readonly Counter<long> _dispatchCompleted;
    private readonly Counter<long> _dispatchFailed;
    private readonly Counter<long> _leaseRenewed;
    private readonly Counter<long> _leaseLost;
    private readonly Counter<long> _leaseRecovered;
    private readonly Counter<long> _heartbeatSuccess;
    private readonly Counter<long> _heartbeatFailure;

    public WorkerMetrics(WorkerRuntimeState state)
    {
        _state = state;
        _queuePolls = _meter.CreateCounter<long>("worker.queue.polls");
        _claims = _meter.CreateCounter<long>("worker.queue.claims");
        _claimConflicts = _meter.CreateCounter<long>("worker.queue.claim_conflicts");
        _emptyPolls = _meter.CreateCounter<long>("worker.queue.empty_polls");
        _dispatchStarted = _meter.CreateCounter<long>("worker.queue.dispatch_started");
        _dispatchCompleted = _meter.CreateCounter<long>("worker.queue.dispatch_completed");
        _dispatchFailed = _meter.CreateCounter<long>("worker.queue.dispatch_failed");
        _leaseRenewed = _meter.CreateCounter<long>("worker.lease.renewed");
        _leaseLost = _meter.CreateCounter<long>("worker.lease.lost");
        _leaseRecovered = _meter.CreateCounter<long>("worker.lease.recovered");
        _heartbeatSuccess = _meter.CreateCounter<long>("worker.heartbeat.success");
        _heartbeatFailure = _meter.CreateCounter<long>("worker.heartbeat.failure");
        _meter.CreateObservableGauge(
            "worker.active_dispatches",
            () => _state.ActiveDispatchCount);
        _meter.CreateObservableGauge(
            "worker.available_slots",
            () => Math.Max(0, _maximumConcurrentJobs - _state.ActiveDispatchCount));
    }

    private int _maximumConcurrentJobs = 1;

    internal void SetMaximumConcurrentJobs(int maximum) => _maximumConcurrentJobs = maximum;
    internal void QueuePoll() => _queuePolls.Add(1);
    internal void Claim(JobWorkKind kind) => _claims.Add(1, WorkKindTag(kind));
    internal void ClaimConflict(JobWorkKind kind) => _claimConflicts.Add(1, WorkKindTag(kind));
    internal void EmptyPoll() => _emptyPolls.Add(1);
    internal void DispatchStarted(JobWorkKind kind) => _dispatchStarted.Add(1, WorkKindTag(kind));
    internal void DispatchCompleted(JobWorkKind kind) => _dispatchCompleted.Add(1, WorkKindTag(kind));
    internal void DispatchFailed(JobWorkKind kind) => _dispatchFailed.Add(1, WorkKindTag(kind));
    internal void LeaseRenewed(JobWorkKind kind) => _leaseRenewed.Add(1, WorkKindTag(kind));
    internal void LeaseLost(JobWorkKind kind) => _leaseLost.Add(1, WorkKindTag(kind));
    internal void LeaseRecovered() => _leaseRecovered.Add(1);
    internal void HeartbeatSuccess() => _heartbeatSuccess.Add(1);
    internal void HeartbeatFailure() => _heartbeatFailure.Add(1);

    private static KeyValuePair<string, object?> WorkKindTag(JobWorkKind kind) =>
        new("work.kind", kind.ToString());

    public void Dispose() => _meter.Dispose();
}
