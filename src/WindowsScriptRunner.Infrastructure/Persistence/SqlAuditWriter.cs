using Microsoft.Extensions.Logging;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Infrastructure.Persistence.Entities;
using WindowsScriptRunner.Infrastructure.Persistence.Mapping;

namespace WindowsScriptRunner.Infrastructure.Persistence;

public sealed class SqlAuditWriter(
    WindowsScriptRunnerDbContext dbContext,
    ILogger<SqlAuditWriter> logger) : IAuditWriter
{
    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        cancellationToken.ThrowIfCancellationRequested();
        if (dbContext.ChangeTracker
            .Entries<AuditEventEntity>()
            .Any(entry => entry.Entity.Id == auditEvent.Id.Value))
        {
            throw new ApplicationConflictException(
                "An audit event with the same identifier is already tracked in this persistence scope.");
        }

        dbContext.AuditEvents.Add(PersistenceMapper.ToEntity(auditEvent));
        logger.LogDebug(
            "Audit writer staged event {AuditEventId} for entity type {EntityType}",
            auditEvent.Id,
            auditEvent.EntityType);
        return Task.CompletedTask;
    }
}
