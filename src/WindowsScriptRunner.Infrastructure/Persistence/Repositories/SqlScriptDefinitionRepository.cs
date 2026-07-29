using System.Data;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Infrastructure.Persistence.Entities;
using WindowsScriptRunner.Infrastructure.Persistence.Mapping;

namespace WindowsScriptRunner.Infrastructure.Persistence.Repositories;

public sealed class SqlScriptDefinitionRepository(
    WindowsScriptRunnerDbContext dbContext,
    ILogger<SqlScriptDefinitionRepository> logger) : IScriptDefinitionRepository
{
    public async Task<ScriptDefinition?> GetByIdAsync(
        ScriptDefinitionId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        var stopwatch = Stopwatch.StartNew();
        var entity = await SqlExceptionTranslator.ExecuteAsync(
            async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
                var result = await dbContext.ScriptDefinitions
                    .Include(item => item.Versions)
                        .ThenInclude(item => item.SupportedPhases)
                    .Include(item => item.Versions)
                        .ThenInclude(item => item.SupportedReportFormats)
                    .Include(item => item.Versions)
                        .ThenInclude(item => item.ParameterDefinitions)
                            .ThenInclude(item => item.AllowedValues)
                    .AsSplitQuery()
                    .SingleOrDefaultAsync(item => item.Id == id.Value, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            },
            logger);
        logger.LogDebug(
            "Repository operation {Operation} for {EntityType} {EntityId} completed in {DurationMs} ms with {Outcome}",
            nameof(GetByIdAsync),
            nameof(ScriptDefinition),
            id,
            stopwatch.ElapsedMilliseconds,
            entity is null ? "NotFound" : "Found");
        return entity is null ? null : PersistenceMapper.ToDomain(entity);
    }

    public Task AddAsync(ScriptDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        RejectDuplicateTracking(definition.Id.Value);
        dbContext.ScriptDefinitions.Add(PersistenceMapper.ToEntity(definition));
        logger.LogDebug(
            "Repository operation {Operation} for {EntityType} {EntityId} completed in {DurationMs} ms with {Outcome}",
            nameof(AddAsync),
            nameof(ScriptDefinition),
            definition.Id,
            stopwatch.ElapsedMilliseconds,
            "Staged");
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ScriptDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        var entity = FindTracked(definition.Id.Value) ??
            throw new ApplicationConflictException(
                "The script definition must be loaded in the current persistence scope before it can be updated.");
        PersistenceMapper.Synchronize(definition, entity);
        dbContext.Entry(entity).Property(item => item.UpdatedUtc).IsModified = true;
        logger.LogDebug(
            "Repository operation {Operation} for {EntityType} {EntityId} completed in {DurationMs} ms with {Outcome}",
            nameof(UpdateAsync),
            nameof(ScriptDefinition),
            definition.Id,
            stopwatch.ElapsedMilliseconds,
            "Staged");
        return Task.CompletedTask;
    }

    private ScriptDefinitionEntity? FindTracked(Guid id) =>
        dbContext.ChangeTracker
            .Entries<ScriptDefinitionEntity>()
            .Select(entry => entry.Entity)
            .SingleOrDefault(entity => entity.Id == id);

    private void RejectDuplicateTracking(Guid id)
    {
        if (FindTracked(id) is not null)
        {
            throw new ApplicationConflictException(
                "A script definition with the same identifier is already tracked in this persistence scope.");
        }
    }
}
