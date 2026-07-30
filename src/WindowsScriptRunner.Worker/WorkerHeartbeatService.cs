using Microsoft.Extensions.Options;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Workers;
using WindowsScriptRunner.Infrastructure.Persistence;

namespace WindowsScriptRunner.Worker;

public sealed class WorkerHeartbeatService(
    IServiceScopeFactory scopeFactory,
    WorkerIdentity identity,
    WorkerRuntimeState state,
    WorkerMetrics metrics,
    IWorkerDelay delay,
    IWorkerRandom random,
    IClock clock,
    IOptions<WorkerOptions> options,
    ILogger<WorkerHeartbeatService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configured = options.Value;
        var interval = TimeSpan.FromSeconds(configured.HeartbeatIntervalSeconds);
        var staleAfter = TimeSpan.FromSeconds(configured.WorkerStaleAfterSeconds);
        var failureBackoff = new WorkerBackoff(
            interval,
            TimeSpan.FromSeconds(configured.PersistenceFailureBackoffMaximumSeconds),
            random);
        var lastSuccess = clock.UtcNow;
        var nextDelay = interval;
        PersistenceUnavailableException? lastPersistenceFailure = null;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await delay.DelayAsync(nextDelay, stoppingToken);
                if (lastPersistenceFailure is not null &&
                    clock.UtcNow - lastSuccess >= staleAfter)
                {
                    throw new InvalidOperationException(
                        "Worker heartbeat could not be persisted within the configured liveness window.",
                        lastPersistenceFailure);
                }

                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var heartbeatUtc = await scope.ServiceProvider
                        .GetRequiredService<RecordWorkerHeartbeatHandler>()
                        .HandleAsync(
                            new RecordWorkerHeartbeatCommand(identity.NodeId),
                            stoppingToken);
                    lastSuccess = clock.UtcNow;
                    lastPersistenceFailure = null;
                    nextDelay = interval;
                    state.MarkHeartbeat(true);
                    metrics.HeartbeatSuccess();
                    failureBackoff.Reset();
                    logger.LogDebug(
                        "Worker {WorkerNodeId} heartbeat persisted at {HeartbeatUtc}.",
                        identity.NodeId,
                        heartbeatUtc);
                }
                catch (PersistenceUnavailableException exception)
                {
                    lastPersistenceFailure = exception;
                    state.MarkHeartbeat(false);
                    metrics.HeartbeatFailure();
                    logger.LogWarning(
                        exception,
                        "Worker {WorkerNodeId} heartbeat persistence is unavailable.",
                        identity.NodeId);
                    var remaining = staleAfter - (clock.UtcNow - lastSuccess);
                    if (remaining <= TimeSpan.Zero)
                    {
                        throw new InvalidOperationException(
                            "Worker heartbeat could not be persisted within the configured liveness window.",
                            exception);
                    }

                    var retryDelay = failureBackoff.Next();
                    nextDelay = retryDelay < remaining
                        ? retryDelay
                        : remaining;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "Worker {WorkerNodeId} heartbeat service stopped cleanly.",
                identity.NodeId);
        }
        catch (Exception exception)
        {
            state.MarkHeartbeat(false);
            logger.LogError(
                exception,
                "Worker {WorkerNodeId} heartbeat service failed unexpectedly.",
                identity.NodeId);
            throw;
        }
    }
}
