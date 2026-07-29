using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Workers;
using WindowsScriptRunner.Infrastructure.Persistence.Entities;
using WindowsScriptRunner.Infrastructure.Persistence.Mapping;

namespace WindowsScriptRunner.Infrastructure.Persistence.Repositories;

public sealed class SqlWorkerNodeRepository(
    WindowsScriptRunnerDbContext dbContext,
    ILogger<SqlWorkerNodeRepository> logger) : IWorkerNodeRepository
{
    public async Task<WorkerNode?> GetByIdAsync(
        WorkerNodeId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        var stopwatch = Stopwatch.StartNew();
        var entity = await SqlExceptionTranslator.ExecuteAsync(
            () => dbContext.WorkerNodes
                .Include(item => item.Capabilities)
                .SingleOrDefaultAsync(item => item.Id == id.Value, cancellationToken),
            logger);
        logger.LogDebug(
            "Repository operation {Operation} for {EntityType} {EntityId} completed in {DurationMs} ms with {Outcome}",
            nameof(GetByIdAsync),
            nameof(WorkerNode),
            id,
            stopwatch.ElapsedMilliseconds,
            entity is null ? "NotFound" : "Found");
        return entity is null ? null : PersistenceMapper.ToDomain(entity);
    }

    public Task AddAsync(WorkerNode workerNode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workerNode);
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        RejectDuplicateTracking(workerNode.Id.Value);
        dbContext.WorkerNodes.Add(PersistenceMapper.ToEntity(workerNode));
        logger.LogDebug(
            "Repository operation {Operation} for {EntityType} {EntityId} completed in {DurationMs} ms with {Outcome}",
            nameof(AddAsync),
            nameof(WorkerNode),
            workerNode.Id,
            stopwatch.ElapsedMilliseconds,
            "Staged");
        return Task.CompletedTask;
    }

    public Task UpdateAsync(WorkerNode workerNode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workerNode);
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        var entity = FindTracked(workerNode.Id.Value) ??
            throw new ApplicationConflictException(
                "The worker node must be loaded in the current persistence scope before it can be updated.");
        PersistenceMapper.Synchronize(workerNode, entity);
        dbContext.Entry(entity).Property(item => item.IsEnabled).IsModified = true;
        logger.LogDebug(
            "Repository operation {Operation} for {EntityType} {EntityId} completed in {DurationMs} ms with {Outcome}",
            nameof(UpdateAsync),
            nameof(WorkerNode),
            workerNode.Id,
            stopwatch.ElapsedMilliseconds,
            "Staged");
        return Task.CompletedTask;
    }

    private WorkerNodeEntity? FindTracked(Guid id) =>
        dbContext.ChangeTracker
            .Entries<WorkerNodeEntity>()
            .Select(entry => entry.Entity)
            .SingleOrDefault(entity => entity.Id == id);

    private void RejectDuplicateTracking(Guid id)
    {
        if (FindTracked(id) is not null)
        {
            throw new ApplicationConflictException(
                "A worker node with the same identifier is already tracked in this persistence scope.");
        }
    }
}
