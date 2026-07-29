using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Jobs;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;

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
        var actor = new UserIdentity("DOMAIN\\integration-user");
        var version = new ScriptVersion(
            ScriptVersionId.New(),
            ScriptVersionNumber.Parse("1.0.0"),
            "scripts/Test.ps1",
            new string('a', 64),
            null,
            "7.4",
            30,
            [ExecutionPhase.Validation],
            [],
            clock.UtcNow,
            actor);
        var script = ScriptDefinition.Create(
            ScriptDefinitionId.New(),
            new ScriptName("integration.script"),
            "Integration Script",
            string.Empty,
            RiskLevel.Low,
            actor,
            clock.UtcNow);
        script.AddVersion(version, clock.UtcNow);
        var scriptRepository = new InMemoryScriptRepository(script);
        var handler = new CreateDraftJobHandler(
            scriptRepository,
            repository,
            auditWriter,
            unitOfWork,
            clock);

        var id = await handler.HandleAsync(
            new CreateDraftJobCommand(
                script.Id,
                version.Id,
                ExecutionPhase.Validation,
                actor),
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

    private sealed class InMemoryScriptRepository(ScriptDefinition script) :
        IScriptDefinitionRepository
    {
        public Task<ScriptDefinition?> GetByIdAsync(
            ScriptDefinitionId id,
            CancellationToken cancellationToken) =>
            Task.FromResult(script.Id == id ? script : null);

        public Task AddAsync(ScriptDefinition definition, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task UpdateAsync(ScriptDefinition definition, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
