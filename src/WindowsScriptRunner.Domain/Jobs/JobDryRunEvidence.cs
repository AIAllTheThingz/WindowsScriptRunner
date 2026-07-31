using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;

namespace WindowsScriptRunner.Domain.Jobs;

public enum JobDryRunEvidenceSource
{
    InternalLifecycle,
    LeasedWorker,
}

public sealed class JobDryRunEvidence
{
    public JobDryRunEvidence(
        JobWorkKind workKind,
        JobDryRunEvidenceSource source,
        WorkerNodeId? workerNodeId,
        JobLeaseId? leaseId,
        long? fencingToken,
        DateTimeOffset executionWindowOpenedUtc,
        DateTimeOffset completedUtc)
    {
        if (workKind != JobWorkKind.DryRun)
        {
            throw new DomainValidationException(
                "Accepted approval evidence must originate from DryRun work.");
        }

        WorkKind = workKind;
        Source = EnumGuard.RequireDefined(source, nameof(Source));
        ValidateProvenance(source, workerNodeId, leaseId, fencingToken);
        if (completedUtc < executionWindowOpenedUtc)
        {
            throw new DomainValidationException(
                "Dry-run evidence cannot complete before its execution window opens.");
        }

        WorkerNodeId = workerNodeId;
        LeaseId = leaseId;
        FencingToken = fencingToken;
        ExecutionWindowOpenedUtc = executionWindowOpenedUtc;
        CompletedUtc = completedUtc;
    }

    public JobWorkKind WorkKind { get; }
    public JobDryRunEvidenceSource Source { get; }
    public WorkerNodeId? WorkerNodeId { get; }
    public JobLeaseId? LeaseId { get; }
    public long? FencingToken { get; }
    public DateTimeOffset ExecutionWindowOpenedUtc { get; }
    public DateTimeOffset CompletedUtc { get; }

    private static void ValidateProvenance(
        JobDryRunEvidenceSource source,
        WorkerNodeId? workerNodeId,
        JobLeaseId? leaseId,
        long? fencingToken)
    {
        var hasWorkerProvenance = workerNodeId is not null || leaseId is not null || fencingToken is not null;
        switch (source)
        {
            case JobDryRunEvidenceSource.InternalLifecycle when hasWorkerProvenance:
                throw new DomainValidationException(
                    "Internal dry-run evidence cannot contain worker lease provenance.");
            case JobDryRunEvidenceSource.LeasedWorker when
                workerNodeId is null || leaseId is null || fencingToken is null || fencingToken <= 0:
                throw new DomainValidationException(
                    "Leased dry-run evidence requires complete positive worker lease provenance.");
        }
    }
}
