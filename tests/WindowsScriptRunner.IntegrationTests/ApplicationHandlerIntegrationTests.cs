using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Jobs;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Credentials;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Domain.Workers;

namespace WindowsScriptRunner.IntegrationTests;

public sealed class ApplicationHandlerIntegrationTests
{
    [Fact]
    public async Task CreateDraftHandlerExecutesAgainstDatabaseFreeFakes()
    {
        var repository = new InMemoryJobRepository();
        var auditWriter = new RecordingAuditWriter();
        var unitOfWork = new RecordingUnitOfWork();
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        var handler = new CreateDraftJobHandler(repository, auditWriter, unitOfWork, clock);

        var id = await handler.HandleAsync(
            new CreateDraftJobCommand(
                ScriptDefinitionId.New(),
                ScriptVersionId.New(),
                ExecutionPhase.Validation,
                new UserIdentity("DOMAIN\\integration-user")),
            CancellationToken.None);

        Assert.Equal(id, repository.Job?.Id);
        Assert.Single(auditWriter.Events);
        Assert.True(unitOfWork.Committed);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class InMemoryJobRepository : IJobRepository
    {
        public Job? Job { get; private set; }

        public Task<Job?> GetByIdAsync(JobId id, CancellationToken cancellationToken) =>
            Task.FromResult(Job?.Id == id ? Job : null);
        public Task<bool> ExistsAsync(JobId id, CancellationToken cancellationToken) =>
            Task.FromResult(Job?.Id == id);

        public Task AddAsync(Job job, CancellationToken cancellationToken)
        {
            Job = job;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Job job, CancellationToken cancellationToken)
        {
            Job = job;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = [];

        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public bool Committed { get; private set; }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            Committed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class InterfaceCoverageRepository :
        IScriptDefinitionRepository,
        IWorkerNodeRepository,
        ICredentialReferenceRepository
    {
        public Task<ScriptDefinition?> GetByIdAsync(
            ScriptDefinitionId id,
            CancellationToken cancellationToken) =>
            Task.FromResult<ScriptDefinition?>(null);
        public Task<WorkerNode?> GetByIdAsync(
            WorkerNodeId id,
            CancellationToken cancellationToken) =>
            Task.FromResult<WorkerNode?>(null);
        public Task<CredentialReference?> GetByIdAsync(
            CredentialReferenceId id,
            CancellationToken cancellationToken) =>
            Task.FromResult<CredentialReference?>(null);
        public Task AddAsync(ScriptDefinition definition, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task AddAsync(WorkerNode workerNode, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task AddAsync(
            CredentialReference credentialReference,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task UpdateAsync(ScriptDefinition definition, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task UpdateAsync(WorkerNode workerNode, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task UpdateAsync(
            CredentialReference credentialReference,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
