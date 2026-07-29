using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;

namespace WindowsScriptRunner.Domain.Jobs;

public sealed record JobLeaseCredentials
{
    public JobLeaseCredentials(
        JobLeaseId leaseId,
        WorkerNodeId workerNodeId,
        long fencingToken)
    {
        LeaseId = leaseId ?? throw new DomainValidationException("Job lease identifier is required.");
        WorkerNodeId = workerNodeId ??
            throw new DomainValidationException("Worker node identifier is required.");
        if (fencingToken <= 0)
        {
            throw new DomainValidationException("Job lease fencing token must be positive.");
        }

        FencingToken = fencingToken;
    }

    public JobLeaseId LeaseId { get; }
    public WorkerNodeId WorkerNodeId { get; }
    public long FencingToken { get; }
}

public enum JobLeaseRecoveryDisposition
{
    ReleasedQueuedDryRun,
    RequeuedUnstartedExecution,
    TimedOutDryRun,
    TimedOutExecution,
}

public sealed class JobLease
{
    public JobLease(
        JobLeaseId id,
        WorkerNodeId workerNodeId,
        JobWorkKind workKind,
        long fencingToken,
        DateTimeOffset acquiredUtc,
        DateTimeOffset lastRenewedUtc,
        DateTimeOffset expiresUtc)
    {
        Id = id ?? throw new DomainValidationException("Job lease identifier is required.");
        WorkerNodeId = workerNodeId ??
            throw new DomainValidationException("Worker node identifier is required.");
        WorkKind = EnumGuard.RequireDefined(workKind, nameof(WorkKind));
        if (fencingToken <= 0)
        {
            throw new DomainValidationException("Job lease fencing token must be positive.");
        }

        ValidateTimestamps(acquiredUtc, lastRenewedUtc, expiresUtc);
        FencingToken = fencingToken;
        AcquiredUtc = acquiredUtc;
        LastRenewedUtc = lastRenewedUtc;
        ExpiresUtc = expiresUtc;
    }

    public JobLeaseId Id { get; }
    public WorkerNodeId WorkerNodeId { get; }
    public JobWorkKind WorkKind { get; }
    public long FencingToken { get; }
    public DateTimeOffset AcquiredUtc { get; }
    public DateTimeOffset LastRenewedUtc { get; private set; }
    public DateTimeOffset ExpiresUtc { get; private set; }
    public JobLeaseCredentials Credentials => new(Id, WorkerNodeId, FencingToken);

    internal void Renew(DateTimeOffset renewedUtc, DateTimeOffset expiresUtc)
    {
        if (renewedUtc < LastRenewedUtc)
        {
            throw new DomainValidationException("Job lease renewal timestamps cannot move backward.");
        }

        if (renewedUtc >= ExpiresUtc)
        {
            throw new DomainValidationException("An expired job lease cannot be renewed.");
        }

        if (expiresUtc <= ExpiresUtc || expiresUtc <= renewedUtc)
        {
            throw new DomainValidationException(
                "Job lease renewal must extend expiration beyond the current expiration.");
        }

        LastRenewedUtc = renewedUtc;
        ExpiresUtc = expiresUtc;
    }

    internal void ValidateCredentials(JobLeaseCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (credentials.LeaseId != Id ||
            credentials.WorkerNodeId != WorkerNodeId ||
            credentials.FencingToken != FencingToken)
        {
            throw new DomainValidationException("Job lease credentials are stale or do not own the lease.");
        }
    }

    private static void ValidateTimestamps(
        DateTimeOffset acquiredUtc,
        DateTimeOffset lastRenewedUtc,
        DateTimeOffset expiresUtc)
    {
        if (acquiredUtc > lastRenewedUtc)
        {
            throw new DomainValidationException(
                "Job lease acquisition cannot occur after its last renewal.");
        }

        if (lastRenewedUtc >= expiresUtc)
        {
            throw new DomainValidationException(
                "Job lease expiration must be after its last renewal.");
        }
    }
}
