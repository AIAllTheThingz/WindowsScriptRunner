using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;

namespace WindowsScriptRunner.Application.Queue;

public sealed record JobWorkRoute(
    JobWorkKind WorkKind,
    ScriptVersionId ScriptVersionId);

public sealed record JobQueueCandidate(
    JobId JobId,
    JobWorkKind WorkKind,
    ScriptVersionId ScriptVersionId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record ExpiredJobLeaseCandidate(
    JobId JobId,
    JobLeaseCredentials Credentials,
    DateTimeOffset ExpiresUtc);

public sealed record ClaimedJobWork(
    JobId JobId,
    JobWorkKind WorkKind,
    ScriptVersionId ScriptVersionId,
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
    IReadOnlySet<JobWorkRoute> SupportedRoutes { get; }

    Task HandleAsync(ClaimedJobWork work, CancellationToken cancellationToken);
}
