using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;

namespace WindowsScriptRunner.Application.Queue;

public sealed record JobQueueCandidate(
    JobId JobId,
    JobWorkKind WorkKind,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record ExpiredJobLeaseCandidate(
    JobId JobId,
    JobLeaseCredentials Credentials,
    DateTimeOffset ExpiresUtc);

public sealed record ClaimedJobWork(
    JobId JobId,
    JobWorkKind WorkKind,
    JobLeaseId LeaseId,
    WorkerNodeId WorkerNodeId,
    long FencingToken,
    DateTimeOffset LeaseExpiresUtc)
{
    public JobLeaseCredentials Credentials =>
        new(LeaseId, WorkerNodeId, FencingToken);
}

public sealed record JobLeaseInspection(
    bool IsCurrent,
    JobStatus JobStatus);

public interface IJobWorkHandler
{
    JobWorkKind WorkKind { get; }

    Task HandleAsync(ClaimedJobWork work, CancellationToken cancellationToken);
}
