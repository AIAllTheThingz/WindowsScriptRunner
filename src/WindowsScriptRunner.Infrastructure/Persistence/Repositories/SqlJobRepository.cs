using System.Data;
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
        var entity = FindTracked(id.Value);
        if (entity is null)
        {
            var executionStrategy = dbContext.Database.CreateExecutionStrategy();
            entity = await SqlExceptionTranslator.ExecuteAsync(
                () => executionStrategy.ExecuteAsync(
                    async strategyCancellationToken =>
                    {
                        await using var transaction = await dbContext.Database.BeginTransactionAsync(
                            IsolationLevel.Serializable,
                            strategyCancellationToken);
                        var result = await dbContext.Jobs
                            .Include(item => item.Targets)
                            .Include(item => item.Parameters)
                            .Include(item => item.Executions)
                            .Include(item => item.Approvals)
                            .Include(item => item.Lease)
                            .AsNoTrackingWithIdentityResolution()
                            .AsSplitQuery()
                            .SingleOrDefaultAsync(
                                item => item.Id == id.Value,
                                strategyCancellationToken);
                        await transaction.CommitAsync(strategyCancellationToken);
                        return result;
                    },
                    cancellationToken),
                logger);
            if (entity is not null)
            {
                dbContext.Attach(entity);
            }
        }

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

    public Task UpdateLeaseAsync(Job job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        var entity = FindTracked(job.Id.Value) ??
            throw new ApplicationConflictException(
                "The job must be loaded in the current persistence scope before its lease can be updated.");
        if (job.Lease is null || entity.Lease is null)
        {
            throw new ApplicationConflictException(
                "The job must have an active tracked lease before it can be updated.");
        }

        PersistenceMapper.SynchronizeLease(job, entity);
        logger.LogDebug(
            "Repository operation {Operation} for {EntityType} {EntityId} completed in {DurationMs} ms with {Outcome}",
            nameof(UpdateLeaseAsync),
            nameof(JobLease),
            job.Id,
            stopwatch.ElapsedMilliseconds,
            "Staged");
        return Task.CompletedTask;
    }

    public async Task<bool> TryRefreshLeaseAsync(
        JobId jobId,
        JobLeaseCredentials credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(credentials);
        cancellationToken.ThrowIfCancellationRequested();
        dbContext.ChangeTracker.DetectChanges();
        var jobEntry = dbContext.ChangeTracker
            .Entries<JobEntity>()
            .SingleOrDefault(entry => entry.Entity.Id == jobId.Value);
        var leaseEntry = dbContext.ChangeTracker
            .Entries<JobLeaseEntity>()
            .SingleOrDefault(entry =>
                entry.Entity.JobId == jobId.Value &&
                entry.Entity.LeaseId == credentials.LeaseId.Value &&
                entry.Entity.WorkerNodeId == credentials.WorkerNodeId.Value &&
                entry.Entity.FencingToken == credentials.FencingToken &&
                entry.State == EntityState.Deleted);
        if (jobEntry is null || leaseEntry is null)
        {
            return false;
        }

        var current = await SqlExceptionTranslator.ExecuteAsync(
            () => dbContext.JobLeases
                .AsNoTracking()
                .Where(entity =>
                    entity.JobId == jobId.Value &&
                    entity.LeaseId == credentials.LeaseId.Value &&
                    entity.WorkerNodeId == credentials.WorkerNodeId.Value &&
                    entity.FencingToken == credentials.FencingToken)
                .Select(entity => new LeaseRetrySnapshot(
                    entity.Job.RowVersion,
                    entity.RowVersion))
                .SingleOrDefaultAsync(cancellationToken),
            logger);
        var originalJobRowVersion = jobEntry.OriginalValues
            .GetValue<byte[]>(nameof(JobEntity.RowVersion));
        if (current is null ||
            !current.JobRowVersion.AsSpan().SequenceEqual(originalJobRowVersion))
        {
            return false;
        }

        leaseEntry.Property(entity => entity.RowVersion).OriginalValue =
            current.LeaseRowVersion;
        leaseEntry.Property(entity => entity.RowVersion).CurrentValue =
            current.LeaseRowVersion;
        logger.LogDebug(
            "Refreshed the current lease concurrency token for terminal retry on job {JobId}.",
            jobId);
        return true;
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

    private sealed record LeaseRetrySnapshot(
        byte[] JobRowVersion,
        byte[] LeaseRowVersion);
}
