using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;

namespace WindowsScriptRunner.Infrastructure.Persistence.Queue;

public sealed class SqlJobQueueCandidateSource(
    WindowsScriptRunnerDbContext dbContext,
    ILogger<SqlJobQueueCandidateSource> logger) : IJobQueueCandidateSource
{
    private const int MaximumCandidateCount = 100;

    public async Task<IReadOnlyList<JobQueueCandidate>> FindCandidatesAsync(
        IReadOnlySet<JobWorkKind> supportedWorkKinds,
        int maximumCount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(supportedWorkKinds);
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumCount is < 1 or > MaximumCandidateCount)
        {
            throw new DomainValidationException(
                $"Queue candidate count must be between 1 and {MaximumCandidateCount}.");
        }

        if (supportedWorkKinds.Any(workKind => !Enum.IsDefined(workKind)))
        {
            throw new DomainValidationException("Supported queue work kinds must be defined.");
        }

        if (supportedWorkKinds.Count == 0)
        {
            return [];
        }

        _ = now;
        var statuses = supportedWorkKinds
            .Select(workKind => workKind switch
            {
                JobWorkKind.DryRun => JobStatus.DryRunQueued.ToString(),
                JobWorkKind.Execute => JobStatus.ExecutionQueued.ToString(),
                _ => throw new DomainValidationException("Supported queue work kind is undefined."),
            })
            .ToArray();
        var candidates = await SqlExceptionTranslator.ExecuteAsync(
            () => dbContext.Jobs
                .AsNoTracking()
                .Where(job => statuses.Contains(job.Status) && job.Lease == null)
                .OrderBy(job => job.UpdatedUtc)
                .ThenBy(job => job.CreatedUtc)
                .ThenBy(job => job.Id)
                .Take(maximumCount)
                .Select(job => new
                {
                    job.Id,
                    job.Status,
                    job.CreatedUtc,
                    job.UpdatedUtc,
                })
                .ToListAsync(cancellationToken),
            logger);

        return candidates
            .Select(candidate => new JobQueueCandidate(
                new JobId(candidate.Id),
                candidate.Status == JobStatus.DryRunQueued.ToString()
                    ? JobWorkKind.DryRun
                    : JobWorkKind.Execute,
                candidate.CreatedUtc,
                candidate.UpdatedUtc))
            .ToArray();
    }
}
