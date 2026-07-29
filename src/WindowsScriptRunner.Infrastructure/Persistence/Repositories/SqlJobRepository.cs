using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Infrastructure.Persistence.Entities;
using WindowsScriptRunner.Infrastructure.Persistence.Mapping;

namespace WindowsScriptRunner.Infrastructure.Persistence.Repositories;

public sealed class SqlJobRepository(
    WindowsScriptRunnerDbContext dbContext,
    ILogger<SqlJobRepository> logger) : IJobRepository
{
    public async Task<Job?> GetByIdAsync(JobId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        var stopwatch = Stopwatch.StartNew();
        var entity = await SqlExceptionTranslator.ExecuteAsync(
            () => dbContext.Jobs
                .Include(item => item.Targets)
                .Include(item => item.Parameters)
                .Include(item => item.Executions)
                .Include(item => item.Approvals)
                .AsSingleQuery()
                .SingleOrDefaultAsync(item => item.Id == id.Value, cancellationToken),
            logger);
        logger.LogDebug(
            "Repository operation {Operation} for {EntityType} {EntityId} completed in {DurationMs} ms with {Outcome}",
            nameof(GetByIdAsync),
            nameof(Job),
            id,
            stopwatch.ElapsedMilliseconds,
            entity is null ? "NotFound" : "Found");
        return entity is null ? null : PersistenceMapper.ToDomain(entity);
    }

    public async Task<bool> ExistsAsync(JobId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        var stopwatch = Stopwatch.StartNew();
        var exists = await SqlExceptionTranslator.ExecuteAsync(
            () => dbContext.Jobs
                .AsNoTracking()
                .AnyAsync(item => item.Id == id.Value, cancellationToken),
            logger);
        logger.LogDebug(
            "Repository operation {Operation} for {EntityType} {EntityId} completed in {DurationMs} ms with {Outcome}",
            nameof(ExistsAsync),
            nameof(Job),
            id,
            stopwatch.ElapsedMilliseconds,
            exists ? "Found" : "NotFound");
        return exists;
    }

    public Task AddAsync(Job job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        RejectDuplicateTracking(job.Id.Value);
        dbContext.Jobs.Add(PersistenceMapper.ToEntity(job));
        logger.LogDebug(
            "Repository operation {Operation} for {EntityType} {EntityId} completed in {DurationMs} ms with {Outcome}",
            nameof(AddAsync),
            nameof(Job),
            job.Id,
            stopwatch.ElapsedMilliseconds,
            "Staged");
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Job job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        var entity = FindTracked(job.Id.Value) ??
            throw new ApplicationConflictException(
                "The job must be loaded in the current persistence scope before it can be updated.");
        PersistenceMapper.Synchronize(job, entity);
        dbContext.Entry(entity).Property(item => item.UpdatedUtc).IsModified = true;
        logger.LogDebug(
            "Repository operation {Operation} for {EntityType} {EntityId} completed in {DurationMs} ms with {Outcome}",
            nameof(UpdateAsync),
            nameof(Job),
            job.Id,
            stopwatch.ElapsedMilliseconds,
            "Staged");
        return Task.CompletedTask;
    }

    private JobEntity? FindTracked(Guid id) =>
        dbContext.ChangeTracker
            .Entries<JobEntity>()
            .Select(entry => entry.Entity)
            .SingleOrDefault(entity => entity.Id == id);

    private void RejectDuplicateTracking(Guid id)
    {
        if (FindTracked(id) is not null)
        {
            throw new ApplicationConflictException(
                "A job with the same identifier is already tracked in this persistence scope.");
        }
    }
}
