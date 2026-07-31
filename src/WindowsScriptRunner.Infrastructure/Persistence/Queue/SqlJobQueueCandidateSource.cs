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
        IReadOnlySet<JobWorkRoute> supportedRoutes,
        int maximumCount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(supportedRoutes);
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumCount is < 1 or > MaximumCandidateCount)
        {
            throw new DomainValidationException(
                $"Queue candidate count must be between 1 and {MaximumCandidateCount}.");
        }

        if (supportedRoutes.Any(route =>
            route is null ||
            route.ScriptVersionId is null ||
            !Enum.IsDefined(route.WorkKind)))
        {
            throw new DomainValidationException("Supported queue routes must be valid.");
        }

        if (supportedRoutes.Count == 0)
        {
            return [];
        }

        _ = now;
        var dryRunVersionIds = supportedRoutes
            .Where(route => route.WorkKind == JobWorkKind.DryRun)
            .Select(route => route.ScriptVersionId.Value)
            .Distinct()
            .ToArray();
        var executeVersionIds = supportedRoutes
            .Where(route => route.WorkKind == JobWorkKind.Execute)
            .Select(route => route.ScriptVersionId.Value)
            .Distinct()
            .ToArray();
        var candidates = await SqlExceptionTranslator.ExecuteAsync(
            () => dbContext.Jobs
                .AsNoTracking()
                .Where(job =>
                    job.Lease == null &&
                    ((job.Status == nameof(JobStatus.DryRunQueued) &&
                      dryRunVersionIds.Contains(job.ScriptVersionId)) ||
                     (job.Status == nameof(JobStatus.ExecutionQueued) &&
                      executeVersionIds.Contains(job.ScriptVersionId))))
                .OrderBy(job => job.UpdatedUtc)
                .ThenBy(job => job.CreatedUtc)
                .ThenBy(job => job.Id)
                .Take(maximumCount)
                .Select(job => new
                {
                    job.Id,
                    job.Status,
                    job.ScriptVersionId,
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
                new ScriptVersionId(candidate.ScriptVersionId),
                candidate.CreatedUtc,
                candidate.UpdatedUtc))
            .ToArray();
    }
}
