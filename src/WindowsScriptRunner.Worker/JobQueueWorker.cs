using Microsoft.Extensions.Options;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Infrastructure.Persistence;

namespace WindowsScriptRunner.Worker;

public sealed class JobQueueWorker(
    IServiceScopeFactory scopeFactory,
    JobWorkHandlerRegistry handlerRegistry,
    WorkerIdentity identity,
    WorkerRuntimeState state,
    WorkerMetrics metrics,
    IWorkerDelay delay,
    IWorkerRandom random,
    IClock clock,
    IOptions<WorkerOptions> options,
    ILogger<JobQueueWorker> logger) : BackgroundService
{
    private readonly Dictionary<Guid, DispatchControl> _active = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configured = options.Value;
        metrics.SetMaximumConcurrentJobs(configured.MaxConcurrentJobs);
        var pollInterval = TimeSpan.FromMilliseconds(
            configured.QueuePollingIntervalMilliseconds);
        var emptyBackoff = new WorkerBackoff(
            pollInterval,
            TimeSpan.FromSeconds(configured.EmptyQueueBackoffMaximumSeconds),
            random);
        var persistenceBackoff = new WorkerBackoff(
            pollInterval,
            TimeSpan.FromSeconds(configured.PersistenceFailureBackoffMaximumSeconds),
            random);

        logger.LogInformation(
            "Worker {WorkerNodeId} queue loop started with {SupportedWorkKindCount} supported work kinds and concurrency {MaximumConcurrency}.",
            identity.NodeId,
            handlerRegistry.SupportedWorkKinds.Count,
            configured.MaxConcurrentJobs);

        var failedUnexpectedly = false;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RemoveCompletedDispatchesAsync();
                if (!configured.QueueProcessingEnabled ||
                    !state.Registered ||
                    !state.HeartbeatHealthy ||
                    handlerRegistry.SupportedWorkKinds.Count == 0)
                {
                    await delay.DelayAsync(pollInterval, stoppingToken);
                    continue;
                }

                var availableSlots = configured.MaxConcurrentJobs - _active.Count;
                if (availableSlots <= 0)
                {
                    await delay.DelayAsync(pollInterval, stoppingToken);
                    continue;
                }

                IReadOnlyList<JobQueueCandidate> candidates;
                try
                {
                    await using var discovery = scopeFactory.CreateAsyncScope();
                    candidates = await discovery.ServiceProvider
                        .GetRequiredService<IJobQueueCandidateSource>()
                        .FindCandidatesAsync(
                            handlerRegistry.SupportedWorkKinds,
                            Math.Min(
                                configured.ClaimCandidateBatchSize,
                                availableSlots),
                            clock.UtcNow,
                            stoppingToken);
                    metrics.QueuePoll();
                    state.MarkPollSuccess(clock.UtcNow);
                    persistenceBackoff.Reset();
                    logger.LogDebug(
                        "Worker {WorkerNodeId} queue poll returned {CandidateCount} candidates with {ActiveDispatchCount} active dispatches.",
                        identity.NodeId,
                        candidates.Count,
                        _active.Count);
                }
                catch (PersistenceUnavailableException exception)
                {
                    metrics.QueuePoll();
                    state.MarkPollFailure();
                    logger.LogWarning(
                        exception,
                        "Worker {WorkerNodeId} queue candidate persistence is unavailable.",
                        identity.NodeId);
                    await delay.DelayAsync(persistenceBackoff.Next(), stoppingToken);
                    continue;
                }

                if (candidates.Count == 0)
                {
                    metrics.EmptyPoll();
                    await delay.DelayAsync(emptyBackoff.Next(), stoppingToken);
                    continue;
                }

                var claimedAny = false;
                var acquisitionPersistenceFailed = false;
                foreach (var candidate in candidates)
                {
                    if (stoppingToken.IsCancellationRequested ||
                        _active.Count >= configured.MaxConcurrentJobs ||
                        !state.HeartbeatHealthy)
                    {
                        break;
                    }

                    try
                    {
                        await using var acquisition = scopeFactory.CreateAsyncScope();
                        var claimed = await acquisition.ServiceProvider
                            .GetRequiredService<AcquireJobLeaseHandler>()
                            .HandleAsync(
                                new AcquireJobLeaseCommand(
                                    candidate.JobId,
                                    candidate.WorkKind,
                                    identity.NodeId,
                                    TimeSpan.FromSeconds(configured.LeaseDurationSeconds),
                                    TimeSpan.FromSeconds(configured.WorkerStaleAfterSeconds)),
                                stoppingToken);
                        if (claimed.WorkerNodeId != identity.NodeId)
                        {
                            throw new InvalidOperationException(
                                "Lease acquisition returned work owned by a different worker.");
                        }

                        var handler = handlerRegistry.GetRequired(claimed.WorkKind);
                        var control = new DispatchControl();
                        control.Task = DispatchAsync(
                            claimed,
                            handler,
                            control,
                            configured);
                        _active.Add(claimed.LeaseId.Value, control);
                        state.SetActiveDispatchCount(_active.Count);
                        metrics.Claim(claimed.WorkKind);
                        claimedAny = true;
                        logger.LogInformation(
                            "Worker {WorkerNodeId} acquired lease {LeaseId} for job {JobId}, work kind {WorkKind}, fencing token {FencingToken}, expiring {LeaseExpiresUtc}. Active dispatches: {ActiveDispatchCount}.",
                            identity.NodeId,
                            claimed.LeaseId,
                            claimed.JobId,
                            claimed.WorkKind,
                            claimed.FencingToken,
                            claimed.LeaseExpiresUtc,
                            _active.Count);
                    }
                    catch (ApplicationConflictException)
                    {
                        metrics.ClaimConflict(candidate.WorkKind);
                        logger.LogDebug(
                            "Worker {WorkerNodeId} lost the claim race for job {JobId}, work kind {WorkKind}.",
                            identity.NodeId,
                            candidate.JobId,
                            candidate.WorkKind);
                    }
                    catch (PersistenceUnavailableException exception)
                    {
                        state.MarkPollFailure();
                        acquisitionPersistenceFailed = true;
                        logger.LogWarning(
                            exception,
                            "Worker {WorkerNodeId} lease acquisition persistence is unavailable.",
                            identity.NodeId);
                        break;
                    }
                }

                if (claimedAny)
                {
                    emptyBackoff.Reset();
                }

                if (acquisitionPersistenceFailed)
                {
                    await delay.DelayAsync(persistenceBackoff.Next(), stoppingToken);
                }
                else if (!claimedAny)
                {
                    await delay.DelayAsync(pollInterval, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failedUnexpectedly = true;
            state.MarkPollFailure();
            logger.LogError(
                exception,
                "Worker {WorkerNodeId} queue loop failed unexpectedly.",
                identity.NodeId);
            throw;
        }
        finally
        {
            try
            {
                await DrainAsync(configured);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Worker {WorkerNodeId} shutdown drain failed.",
                    identity.NodeId);
            }

            if (!failedUnexpectedly)
            {
                logger.LogInformation(
                    "Worker {WorkerNodeId} queue loop stopped cleanly with {RemainingDispatchCount} remaining dispatches.",
                    identity.NodeId,
                    _active.Count);
            }
        }
    }

    private async Task DispatchAsync(
        ClaimedJobWork work,
        IJobWorkHandler handler,
        DispatchControl control,
        WorkerOptions configured)
    {
        metrics.DispatchStarted(work.WorkKind);
        var handlerTask = Task.CompletedTask;
        var renewalTask = Task.CompletedTask;
        var handlerSucceeded = false;
        try
        {
            handlerTask = handler.HandleAsync(work, control.HandlerCancellation.Token);
            renewalTask = RenewLeaseAsync(
                work,
                handlerTask,
                control,
                configured);
            await handlerTask;
            handlerSucceeded = true;
        }
        catch (OperationCanceledException)
            when (control.HandlerCancellation.IsCancellationRequested)
        {
            logger.LogInformation(
                "Dispatch for lease {LeaseId}, job {JobId}, work kind {WorkKind} observed cancellation.",
                work.LeaseId,
                work.JobId,
                work.WorkKind);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Dispatch for lease {LeaseId}, job {JobId}, work kind {WorkKind} failed.",
                work.LeaseId,
                work.JobId,
                work.WorkKind);
        }
        finally
        {
            control.RenewalCancellation.Cancel();
            try
            {
                await renewalTask;
            }
            catch (OperationCanceledException)
                when (control.RenewalCancellation.IsCancellationRequested)
            {
            }

            var resolved = await InspectAndReleaseIfSafeAsync(
                work,
                handlerSucceeded);
            if (handlerSucceeded && resolved && !control.LeaseLost)
            {
                metrics.DispatchCompleted(work.WorkKind);
                logger.LogInformation(
                    "Dispatch completed for lease {LeaseId}, job {JobId}, work kind {WorkKind}.",
                    work.LeaseId,
                    work.JobId,
                    work.WorkKind);
            }
            else
            {
                metrics.DispatchFailed(work.WorkKind);
            }

        }
    }

    private async Task RenewLeaseAsync(
        ClaimedJobWork work,
        Task handlerTask,
        DispatchControl control,
        WorkerOptions configured)
    {
        var renewalInterval = TimeSpan.FromSeconds(
            configured.LeaseRenewalIntervalSeconds);
        var expiration = work.LeaseExpiresUtc;
        var failureBackoff = new WorkerBackoff(
            TimeSpan.FromMilliseconds(configured.QueuePollingIntervalMilliseconds),
            TimeSpan.FromSeconds(configured.PersistenceFailureBackoffMaximumSeconds),
            random);
        while (!handlerTask.IsCompleted &&
            !control.RenewalCancellation.IsCancellationRequested)
        {
            await delay.DelayAsync(
                renewalInterval,
                control.RenewalCancellation.Token);
            if (handlerTask.IsCompleted ||
                control.RenewalCancellation.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await using var renewal = scopeFactory.CreateAsyncScope();
                expiration = await renewal.ServiceProvider
                    .GetRequiredService<RenewJobLeaseHandler>()
                    .HandleAsync(
                        new RenewJobLeaseCommand(
                            work.JobId,
                            work.Credentials,
                            TimeSpan.FromSeconds(configured.LeaseDurationSeconds)),
                        control.RenewalCancellation.Token);
                failureBackoff.Reset();
                metrics.LeaseRenewed(work.WorkKind);
                logger.LogDebug(
                    "Renewed lease {LeaseId} for job {JobId}, worker {WorkerNodeId}, fencing token {FencingToken}, through {LeaseExpiresUtc}.",
                    work.LeaseId,
                    work.JobId,
                    identity.NodeId,
                    work.FencingToken,
                    expiration);
            }
            catch (ApplicationConflictException exception)
            {
                metrics.LeaseLost(work.WorkKind);
                control.LeaseLost = true;
                logger.LogWarning(
                    exception,
                    "Lease {LeaseId} for job {JobId}, worker {WorkerNodeId}, fencing token {FencingToken} was lost.",
                    work.LeaseId,
                    work.JobId,
                    identity.NodeId,
                    work.FencingToken);
                control.HandlerCancellation.Cancel();
                return;
            }
            catch (PersistenceUnavailableException exception)
            {
                var retryDelay = failureBackoff.Next();
                if (clock.UtcNow + retryDelay >= expiration)
                {
                    metrics.LeaseLost(work.WorkKind);
                    control.LeaseLost = true;
                    logger.LogWarning(
                        exception,
                        "Lease {LeaseId} for job {JobId} can no longer be renewed safely before expiration.",
                        work.LeaseId,
                        work.JobId);
                    control.HandlerCancellation.Cancel();
                    return;
                }

                logger.LogWarning(
                    exception,
                    "Lease renewal persistence is unavailable for lease {LeaseId}; retrying within the current lease window.",
                    work.LeaseId);
                await delay.DelayAsync(
                    retryDelay,
                    control.RenewalCancellation.Token);
            }
            catch (OperationCanceledException)
                when (control.RenewalCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                metrics.LeaseLost(work.WorkKind);
                control.LeaseLost = true;
                logger.LogError(
                    exception,
                    "Lease renewal failed unexpectedly for lease {LeaseId}, job {JobId}.",
                    work.LeaseId,
                    work.JobId);
                control.HandlerCancellation.Cancel();
                return;
            }
        }
    }

    private async Task<bool> InspectAndReleaseIfSafeAsync(
        ClaimedJobWork work,
        bool handlerSucceeded)
    {
        JobLeaseInspection inspection;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            inspection = await scope.ServiceProvider
                .GetRequiredService<InspectJobLeaseHandler>()
                .HandleAsync(
                    new InspectJobLeaseQuery(work.JobId, work.Credentials),
                    CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not verify lease resolution for lease {LeaseId}, job {JobId}.",
                work.LeaseId,
                work.JobId);
            return false;
        }

        if (!inspection.IsCurrent)
        {
            return true;
        }

        if (handlerSucceeded)
        {
            logger.LogError(
                "Handler returned successfully while lease {LeaseId} remained active for job {JobId} in state {JobStatus}.",
                work.LeaseId,
                work.JobId,
                inspection.JobStatus);
        }

        try
        {
            await using var release = scopeFactory.CreateAsyncScope();
            await release.ServiceProvider
                .GetRequiredService<ReleaseUnstartedJobLeaseHandler>()
                .HandleAsync(
                    new ReleaseUnstartedJobLeaseCommand(
                        work.JobId,
                        work.Credentials),
                    CancellationToken.None);
            logger.LogWarning(
                "Safely released unstarted lease {LeaseId} for job {JobId} after dispatch termination.",
                work.LeaseId,
                work.JobId);
        }
        catch (ApplicationConflictException)
        {
            logger.LogWarning(
                "Lease {LeaseId} for job {JobId} is active work and was left for expiration recovery.",
                work.LeaseId,
                work.JobId);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to safely release unstarted lease {LeaseId} for job {JobId}.",
                work.LeaseId,
                work.JobId);
        }

        return false;
    }

    private async Task RemoveCompletedDispatchesAsync()
    {
        foreach (var completed in _active
            .Where(pair => pair.Value.Task.IsCompleted)
            .ToArray())
        {
            try
            {
                await completed.Value.Task;
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug(
                    "Dispatch task for lease {LeaseId} was canceled.",
                    completed.Key);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Dispatch task for lease {LeaseId} faulted.",
                    completed.Key);
            }
            finally
            {
                _active.Remove(completed.Key);
                completed.Value.Dispose();
            }
        }

        state.SetActiveDispatchCount(_active.Count);
    }

    private async Task DrainAsync(WorkerOptions configured)
    {
        await RemoveCompletedDispatchesAsync();
        foreach (var dispatch in _active.Values)
        {
            dispatch.HandlerCancellation.Cancel();
        }

        if (_active.Count == 0)
        {
            state.SetActiveDispatchCount(0);
            logger.LogInformation("Worker queue shutdown drain completed with no active dispatches.");
            return;
        }

        var allDispatches = Task.WhenAll(_active.Values.Select(value => value.Task));
        var timeout = delay.DelayAsync(
            TimeSpan.FromSeconds(configured.DrainTimeoutSeconds),
            CancellationToken.None);
        if (await Task.WhenAny(allDispatches, timeout) == allDispatches)
        {
            await RemoveCompletedDispatchesAsync();
            logger.LogInformation("Worker queue shutdown drain completed.");
            return;
        }

        foreach (var dispatch in _active.Values)
        {
            dispatch.RenewalCancellation.Cancel();
        }

        await RemoveCompletedDispatchesAsync();
        logger.LogWarning(
            "Worker queue shutdown drain timed out with {RemainingDispatchCount} dispatches; their leases will expire for recovery.",
            _active.Count);
    }

    private sealed class DispatchControl : IDisposable
    {
        public CancellationTokenSource HandlerCancellation { get; } = new();
        public CancellationTokenSource RenewalCancellation { get; } = new();
        public Task Task { get; set; } = Task.CompletedTask;
        public bool LeaseLost { get; set; }

        public void Dispose()
        {
            HandlerCancellation.Dispose();
            RenewalCancellation.Dispose();
        }
    }
}
