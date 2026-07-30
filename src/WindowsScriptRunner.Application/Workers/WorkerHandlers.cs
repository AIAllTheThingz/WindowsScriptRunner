using System.Globalization;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Domain.Workers;

namespace WindowsScriptRunner.Application.Workers;

public sealed class RegisterWorkerHandler(
    IWorkerNodeRepository workerRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task<WorkerRegistrationResult> HandleAsync(
        RegisterWorkerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.WorkerNodeId);
        ArgumentNullException.ThrowIfNull(command.Capabilities);
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        var worker = await workerRepository.GetByIdAsync(
            command.WorkerNodeId,
            cancellationToken);
        var created = worker is null;
        var actor = WorkerActor(command.WorkerNodeId);
        bool capabilitiesChanged;
        if (worker is null)
        {
            worker = new WorkerNode(command.WorkerNodeId, command.Name, now);
            capabilitiesChanged = worker.SynchronizeCapabilities(command.Capabilities);
            worker.RecordHeartbeat(now);
            await workerRepository.AddAsync(worker, cancellationToken);
            await auditWriter.WriteAsync(
                CreateAudit(
                    "WorkerRegistered",
                    worker,
                    actor,
                    now,
                    "The worker node was registered.",
                    command.Capabilities.Count),
                cancellationToken);
        }
        else
        {
            if (!string.Equals(worker.Name, command.Name, StringComparison.Ordinal))
            {
                throw new ApplicationConflictException(
                    "The configured worker name does not match the persisted worker identity.");
            }

            if (!worker.IsEnabled)
            {
                throw new ApplicationValidationException(
                    "A disabled worker node cannot register or process work.");
            }

            capabilitiesChanged = worker.SynchronizeCapabilities(command.Capabilities);
            worker.RecordHeartbeat(now);
            await workerRepository.UpdateAsync(worker, cancellationToken);
            if (capabilitiesChanged)
            {
                await auditWriter.WriteAsync(
                    CreateAudit(
                        "WorkerCapabilitiesSynchronized",
                        worker,
                        actor,
                        now,
                        "The worker capability set was synchronized.",
                        command.Capabilities.Count),
                    cancellationToken);
            }
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return new WorkerRegistrationResult(worker.Id, created, capabilitiesChanged, now);
    }

    internal static UserIdentity WorkerActor(WorkerNodeId workerNodeId) =>
        new($"worker:{workerNodeId}");

    private static AuditEvent CreateAudit(
        string eventType,
        WorkerNode worker,
        UserIdentity actor,
        DateTimeOffset occurredUtc,
        string summary,
        int capabilityCount) =>
        new(
            AuditEventId.New(),
            eventType,
            nameof(WorkerNode),
            worker.Id.ToString(),
            actor,
            occurredUtc,
            summary,
            new Dictionary<string, string>
            {
                ["CapabilityCount"] = capabilityCount.ToString(CultureInfo.InvariantCulture),
            });
}

public sealed class RecordWorkerHeartbeatHandler(
    IWorkerNodeRepository workerRepository,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task<DateTimeOffset> HandleAsync(
        RecordWorkerHeartbeatCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.WorkerNodeId);
        var worker = await workerRepository.GetByIdAsync(
            command.WorkerNodeId,
            cancellationToken)
            ?? throw new EntityNotFoundException(
                nameof(WorkerNode),
                command.WorkerNodeId.ToString());
        if (!worker.IsEnabled)
        {
            throw new ApplicationValidationException(
                "A disabled worker node cannot record a heartbeat.");
        }

        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        worker.RecordHeartbeat(now);
        await workerRepository.UpdateAsync(worker, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return now;
    }
}
