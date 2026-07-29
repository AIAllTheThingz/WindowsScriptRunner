using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Domain.Credentials;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Infrastructure.Persistence.Entities;
using WindowsScriptRunner.Infrastructure.Persistence.Mapping;

namespace WindowsScriptRunner.Infrastructure.Persistence.Repositories;

public sealed class SqlCredentialReferenceRepository(
    WindowsScriptRunnerDbContext dbContext,
    ILogger<SqlCredentialReferenceRepository> logger) : ICredentialReferenceRepository
{
    public async Task<CredentialReference?> GetByIdAsync(
        CredentialReferenceId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        var stopwatch = Stopwatch.StartNew();
        var entity = await dbContext.CredentialReferences
            .SingleOrDefaultAsync(item => item.Id == id.Value, cancellationToken);
        logger.LogDebug(
            "Repository operation {Operation} for {EntityType} {EntityId} completed in {DurationMs} ms with {Outcome}",
            nameof(GetByIdAsync),
            nameof(CredentialReference),
            id,
            stopwatch.ElapsedMilliseconds,
            entity is null ? "NotFound" : "Found");
        return entity is null ? null : PersistenceMapper.ToDomain(entity);
    }

    public async Task AddAsync(
        CredentialReference credentialReference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentialReference);
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        RejectDuplicateTracking(credentialReference.Id.Value);
        var candidate = PersistenceMapper.ToEntity(credentialReference);
        var hashMatch = await dbContext.CredentialReferences
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.NormalizedProviderType == candidate.NormalizedProviderType &&
                    item.ExternalIdentifierHash == candidate.ExternalIdentifierHash,
                cancellationToken);
        if (hashMatch is not null)
        {
            var message = hashMatch.ExternalIdentifier == candidate.ExternalIdentifier
                ? "A credential reference with the same provider and external identifier already exists."
                : "A credential reference identifier hash collision prevents this insert.";
            throw new ApplicationConflictException(message);
        }

        dbContext.CredentialReferences.Add(candidate);
        logger.LogDebug(
            "Repository operation {Operation} for {EntityType} {EntityId} completed in {DurationMs} ms with {Outcome}",
            nameof(AddAsync),
            nameof(CredentialReference),
            credentialReference.Id,
            stopwatch.ElapsedMilliseconds,
            "Staged");
    }

    public Task UpdateAsync(
        CredentialReference credentialReference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentialReference);
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        var entity = FindTracked(credentialReference.Id.Value) ??
            throw new ApplicationConflictException(
                "The credential reference must be loaded in the current persistence scope before it can be updated.");
        PersistenceMapper.Synchronize(credentialReference, entity);
        logger.LogDebug(
            "Repository operation {Operation} for {EntityType} {EntityId} completed in {DurationMs} ms with {Outcome}",
            nameof(UpdateAsync),
            nameof(CredentialReference),
            credentialReference.Id,
            stopwatch.ElapsedMilliseconds,
            "Staged");
        return Task.CompletedTask;
    }

    private CredentialReferenceEntity? FindTracked(Guid id) =>
        dbContext.ChangeTracker
            .Entries<CredentialReferenceEntity>()
            .Select(entry => entry.Entity)
            .SingleOrDefault(entity => entity.Id == id);

    private void RejectDuplicateTracking(Guid id)
    {
        if (FindTracked(id) is not null)
        {
            throw new ApplicationConflictException(
                "A credential reference with the same identifier is already tracked in this persistence scope.");
        }
    }
}
