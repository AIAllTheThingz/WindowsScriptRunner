using Microsoft.Extensions.Options;

namespace WindowsScriptRunner.Worker;

public sealed class HeartbeatWorker(
    ILogger<HeartbeatWorker> logger,
    IOptions<WorkerOptions> options,
    IHeartbeatTimer? heartbeatTimer = null) : BackgroundService
{
    private readonly ILogger<HeartbeatWorker> _logger = logger;
    private readonly IHeartbeatTimer _heartbeatTimer = heartbeatTimer ??
        new PeriodicHeartbeatTimer(TimeSpan.FromSeconds(options.Value.HeartbeatIntervalSeconds));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Windows Script Runner worker started.");
        _logger.LogInformation("Job execution is not implemented in this Phase 1 scaffold.");

        try
        {
            while (await _heartbeatTimer.WaitForNextTickAsync(stoppingToken))
            {
                _logger.LogInformation("Worker heartbeat at {HeartbeatTime}.", DateTimeOffset.UtcNow);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worker cancellation requested.");
        }
        finally
        {
            await _heartbeatTimer.DisposeAsync();
            _logger.LogInformation("Windows Script Runner worker stopped cleanly.");
        }
    }
}
