using Microsoft.Extensions.Options;
using WindowsScriptRunner.Application.Workers;
using WindowsScriptRunner.Domain.Workers;

namespace WindowsScriptRunner.Worker;

public sealed class WorkerRegistrationHostedService(
    IServiceScopeFactory scopeFactory,
    WorkerIdentity identity,
    WorkerRuntimeState state,
    IOptions<WorkerOptions> options,
    ILogger<WorkerRegistrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var capabilities = options.Value.Capabilities
            .Select(capability => new WorkerCapability(
                capability.Name,
                capability.Value))
            .ToArray();
        await using var scope = scopeFactory.CreateAsyncScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<RegisterWorkerHandler>()
            .HandleAsync(
                new RegisterWorkerCommand(identity.NodeId, identity.Name, capabilities),
                cancellationToken);
        state.MarkRegistered(result.HeartbeatUtc);
        logger.LogInformation(
            "Worker {WorkerNodeId} ({WorkerName}) registered. Created: {Created}; capabilities changed: {CapabilitiesChanged}; capability count: {CapabilityCount}.",
            identity.NodeId,
            identity.Name,
            result.Created,
            result.CapabilitiesChanged,
            capabilities.Length);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
