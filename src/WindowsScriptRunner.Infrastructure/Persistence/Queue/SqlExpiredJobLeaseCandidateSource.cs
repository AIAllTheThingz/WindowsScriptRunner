using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;

namespace WindowsScriptRunner.Infrastructure.Persistence.Queue;

public sealed class SqlExpiredJobLeaseCandidateSource(
    WindowsScriptRunnerDbContext dbContext,
    ILogger<SqlExpiredJobLeaseCandidateSource> logger) : IExpiredJobLeaseCandidateSource
{
    private const int MaximumCandidateCount = 100;

    public async Task<IReadOnlyList<ExpiredJobLeaseCandidate>> FindExpiredAsync(
        DateTimeOffset now,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        if (maximumCount is < 1 or > MaximumCandidateCount)
        {
            throw new DomainValidationException(
                $"Expired lease candidate count must be between 1 and {MaximumCandidateCount}.");
        }

        var candidates = await SqlExceptionTranslator.ExecuteAsync(
            () => dbContext.JobLeases
                .AsNoTracking()
                .Where(lease => lease.ExpiresUtc <= now)
                .OrderBy(lease => lease.ExpiresUtc)
                .ThenBy(lease => lease.JobId)
                .Take(maximumCount)
                .Select(lease => new
                {
                    lease.JobId,
                    lease.LeaseId,
                    lease.WorkerNodeId,
                    lease.FencingToken,
                    lease.ExpiresUtc,
                })
                .ToListAsync(cancellationToken),
            logger);

        return candidates
            .Select(candidate => new ExpiredJobLeaseCandidate(
                new JobId(candidate.JobId),
                new JobLeaseCredentials(
                    new JobLeaseId(candidate.LeaseId),
                    new WorkerNodeId(candidate.WorkerNodeId),
                    candidate.FencingToken),
                candidate.ExpiresUtc))
            .ToArray();
    }
}
