using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Workers;

namespace WindowsScriptRunner.Application.Workers;

public sealed record RegisterWorkerCommand(
    WorkerNodeId WorkerNodeId,
    string Name,
    IReadOnlyCollection<WorkerCapability> Capabilities);

public sealed record RecordWorkerHeartbeatCommand(WorkerNodeId WorkerNodeId);

public sealed record WorkerRegistrationResult(
    WorkerNodeId WorkerNodeId,
    bool Created,
    bool CapabilitiesChanged,
    DateTimeOffset HeartbeatUtc);
