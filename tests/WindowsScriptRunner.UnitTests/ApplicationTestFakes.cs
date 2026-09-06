using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Domain.Auditing;

namespace WindowsScriptRunner.UnitTests;

internal sealed class FakeAuditWriter : IAuditWriter
{
    internal List<AuditEvent> Events { get; } = [];

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Events.Add(auditEvent);
        return Task.CompletedTask;
    }
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    internal int CommitCount { get; private set; }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CommitCount++;
        return Task.CompletedTask;
    }
}
