using Microsoft.Extensions.Options;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Infrastructure.Persistence;

namespace WindowsScriptRunner.Worker;

public sealed class ExpiredLeaseRecoveryService(
    IServiceScopeFactory scopeFactory,
    WorkerRuntimeState state,
    WorkerMetrics metrics,
    IWorkerDelay delay,
    IWorkerRandom random,
    IClock clock,
    IOptions<WorkerOptions> options,
    ILogger<ExpiredLeaseRecoveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configured = options.Value;
        var interval = TimeSpan.FromSeconds(configured.LeaseRecoveryIntervalSeconds);
        var failureBackoff = new WorkerBackoff(
            TimeSpan.FromMilliseconds(configured.QueuePollingIntervalMilliseconds),
            TimeSpan.FromSeconds(configured.PersistenceFailureBackoffMaximumSeconds),
            random);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await delay.DelayAsync(interval, stoppingToken);
                if (!state.Registered)
                {
                    continue;
                }

                try
                {
                    IReadOnlyList<ExpiredJobLeaseCandidate> candidates;
                    await using (var discovery = scopeFactory.CreateAsyncScope())
                    {
                        candidates = await discovery.ServiceProvider
                            .GetRequiredService<IExpiredJobLeaseCandidateSource>()
                            .FindExpiredAsync(
                                clock.UtcNow,
                                configured.ClaimCandidateBatchSize,
                                stoppingToken);
                    }

                    failureBackoff.Reset();
                    foreach (var candidate in candidates)
                    {
                        try
                        {
                            await using var recovery = scopeFactory.CreateAsyncScope();
                            var disposition = await recovery.ServiceProvider
                                .GetRequiredService<RecoverExpiredJobLeaseHandler>()
                                .HandleAsync(
                                    new RecoverExpiredJobLeaseCommand(candidate),
                                    stoppingToken);
                            metrics.LeaseRecovered();
                            logger.LogInformation(
                                "Recovered expired lease {LeaseId} for job {JobId} owned by worker {WorkerNodeId} with fencing token {FencingToken}. Disposition: {RecoveryDisposition}.",
                                candidate.Credentials.LeaseId,
                                candidate.JobId,
                                candidate.Credentials.WorkerNodeId,
                                candidate.Credentials.FencingToken,
                                disposition);
                        }
                        catch (ApplicationConflictException)
                        {
                            logger.LogDebug(
                                "Expired lease candidate {LeaseId} changed before recovery.",
                                candidate.Credentials.LeaseId);
                        }
                    }
                }
                catch (PersistenceUnavailableException exception)
                {
                    logger.LogWarning(
                        exception,
                        "Expired lease recovery persistence is unavailable.");
                    await delay.DelayAsync(failureBackoff.Next(), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Expired lease recovery service stopped cleanly.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Expired lease recovery service failed unexpectedly.");
            throw;
        }
    }
}
