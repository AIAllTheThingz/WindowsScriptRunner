using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Infrastructure.Persistence.Entities;

namespace WindowsScriptRunner.Infrastructure.Persistence;

public sealed class SqlUnitOfWork(
    WindowsScriptRunnerDbContext dbContext,
    ILogger<SqlUnitOfWork> logger) : IUnitOfWork
{
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var executionStrategy = dbContext.Database.CreateExecutionStrategy();
            await executionStrategy.ExecuteAsync(
                async strategyCancellationToken =>
                {
                    if (!HasReadDependencies())
                    {
                        await dbContext.SaveChangesAsync(
                            acceptAllChangesOnSuccess: false,
                            strategyCancellationToken);
                        return;
                    }

                    await using var transaction = await dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        strategyCancellationToken);
                    await RevalidateReadDependenciesAsync(strategyCancellationToken);
                    await dbContext.SaveChangesAsync(
                        acceptAllChangesOnSuccess: false,
                        strategyCancellationToken);
                    await transaction.CommitAsync(strategyCancellationToken);
                },
                cancellationToken);
            dbContext.ChangeTracker.AcceptAllChanges();
            logger.LogDebug(
                "Persistence unit of work committed in {DurationMs} ms with {Outcome}",
                stopwatch.ElapsedMilliseconds,
                "Succeeded");
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug(
                "Persistence unit of work ended in {DurationMs} ms with {Outcome}",
                stopwatch.ElapsedMilliseconds,
                "Cancelled");
            throw;
        }
        catch (DbUpdateException exception)
        {
            throw SqlExceptionTranslator.Translate(exception, logger);
        }
        catch (RetryLimitExceededException exception)
        {
            throw SqlExceptionTranslator.TranslateRetryLimitExceeded(exception, logger);
        }
        catch (InvalidOperationException exception)
            when (SqlExceptionTranslator.TryGetSqlException(exception, out var sqlException))
        {
            throw SqlExceptionTranslator.Translate(exception, sqlException, logger);
        }
        catch (SqlException exception)
        {
            throw SqlExceptionTranslator.Translate(exception, logger);
        }
    }

    private bool HasReadDependencies() =>
        dbContext.ChangeTracker.Entries<ScriptDefinitionEntity>()
            .Any(entry => entry.State == EntityState.Unchanged) ||
        dbContext.ChangeTracker.Entries<WorkerNodeEntity>()
            .Any(entry => entry.State == EntityState.Unchanged) ||
        dbContext.ChangeTracker.Entries<CredentialReferenceEntity>()
            .Any(entry => entry.State == EntityState.Unchanged);

    private async Task RevalidateReadDependenciesAsync(CancellationToken cancellationToken)
    {
        var scriptDependencies = dbContext.ChangeTracker
            .Entries<ScriptDefinitionEntity>()
            .Where(entry => entry.State == EntityState.Unchanged)
            .Select(entry => (entry.Entity.Id, RowVersion: entry.Entity.RowVersion.ToArray()))
            .ToArray();
        foreach (var dependency in scriptDependencies)
        {
            var currentRowVersion = await dbContext.ScriptDefinitions
                .AsNoTracking()
                .Where(entity => entity.Id == dependency.Id)
                .Select(entity => entity.RowVersion)
                .SingleOrDefaultAsync(cancellationToken);
            EnsureUnchanged(currentRowVersion, dependency.RowVersion, "script definition");
        }

        var workerDependencies = dbContext.ChangeTracker
            .Entries<WorkerNodeEntity>()
            .Where(entry => entry.State == EntityState.Unchanged)
            .Select(entry => (entry.Entity.Id, RowVersion: entry.Entity.RowVersion.ToArray()))
            .ToArray();
        foreach (var dependency in workerDependencies)
        {
            var currentRowVersion = await dbContext.WorkerNodes
                .AsNoTracking()
                .Where(entity => entity.Id == dependency.Id)
                .Select(entity => entity.RowVersion)
                .SingleOrDefaultAsync(cancellationToken);
            EnsureUnchanged(currentRowVersion, dependency.RowVersion, "worker node");
        }

        var credentialDependencies = dbContext.ChangeTracker
            .Entries<CredentialReferenceEntity>()
            .Where(entry => entry.State == EntityState.Unchanged)
            .Select(entry => (entry.Entity.Id, RowVersion: entry.Entity.RowVersion.ToArray()))
            .ToArray();
        foreach (var dependency in credentialDependencies)
        {
            var currentRowVersion = await dbContext.CredentialReferences
                .AsNoTracking()
                .Where(entity => entity.Id == dependency.Id)
                .Select(entity => entity.RowVersion)
                .SingleOrDefaultAsync(cancellationToken);
            EnsureUnchanged(currentRowVersion, dependency.RowVersion, "credential reference");
        }
    }

    private static void EnsureUnchanged(
        byte[]? currentRowVersion,
        byte[] expectedRowVersion,
        string entityType)
    {
        if (currentRowVersion is not null &&
            currentRowVersion.AsSpan().SequenceEqual(expectedRowVersion))
        {
            return;
        }

        throw new ApplicationConflictException(
            $"The validated {entityType} changed before the operation could be committed.");
    }
}
