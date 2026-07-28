using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Jobs;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Credentials;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Domain.Workers;

namespace WindowsScriptRunner.UnitTests;

public sealed class ApplicationHandlerTests
{
    [Fact]
    public async Task CreateDraftPersistsAuditsCommitsAndPropagatesCancellation()
    {
        var fixture = new HandlerFixture();
        using var source = new CancellationTokenSource();
        var command = new CreateDraftJobCommand(
            ScriptDefinitionId.New(),
            ScriptVersionId.New(),
            ExecutionPhase.DryRun,
            TestDomainFactory.User);

        var id = await fixture.CreateHandler.HandleAsync(command, source.Token);

        Assert.Equal(id, fixture.Jobs.Job?.Id);
        Assert.Equal("JobDraftCreated", Assert.Single(fixture.Audits.Events).EventType);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
        Assert.All(fixture.ObservedTokens, token => Assert.Equal(source.Token, token));
    }

    [Fact]
    public async Task AddTargetHandlerUsesDomainBehavior()
    {
        var fixture = HandlerFixture.WithDraftJob();

        await fixture.AddTargetHandler.HandleAsync(
            new AddJobTargetCommand(
                fixture.Jobs.Job!.Id,
                new TargetName("server-02"),
                TestDomainFactory.OtherUser),
            CancellationToken.None);

        Assert.Equal("server-02", Assert.Single(fixture.Jobs.Job.Targets).Name.Value);
        Assert.Equal("JobTargetAdded", Assert.Single(fixture.Audits.Events).EventType);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task SetSensitiveParameterValidatesAndRedactsAudit()
    {
        var definition = TestDomainFactory.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            required: true,
            sensitive: true);
        var fixture = HandlerFixture.WithDraftJob(definition);

        await fixture.SetParameterHandler.HandleAsync(
            new SetJobParameterCommand(
                fixture.Jobs.Job!.Id,
                definition.Name,
                "external-reference-1",
                TestDomainFactory.User),
            CancellationToken.None);

        var audit = Assert.Single(fixture.Audits.Events);
        Assert.Equal("[REDACTED]", audit.Properties["Value"]);
        Assert.DoesNotContain(
            "external-reference-1",
            string.Join(' ', audit.Properties.Values),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitAndTransitionHandlersEnforceLifecycle()
    {
        var fixture = HandlerFixture.WithDraftJob();
        fixture.Jobs.Job!.AddTarget(
            new TargetName("server-01"),
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));

        await fixture.SubmitHandler.HandleAsync(
            new SubmitJobCommand(fixture.Jobs.Job.Id, TestDomainFactory.User),
            CancellationToken.None);
        await fixture.TransitionHandler.HandleAsync(
            new TransitionJobCommand(
                fixture.Jobs.Job.Id,
                JobStatus.Validated,
                TestDomainFactory.OtherUser),
            CancellationToken.None);

        Assert.Equal(JobStatus.Validated, fixture.Jobs.Job.Status);
        Assert.Equal(2, fixture.UnitOfWork.CommitCount);
        Assert.Equal(["JobSubmitted", "JobStatusChanged"], fixture.Audits.Events.Select(item => item.EventType));
    }

    [Fact]
    public async Task GetJobQueryNeverReturnsSensitiveValue()
    {
        var definition = TestDomainFactory.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            sensitive: true);
        var fixture = HandlerFixture.WithDraftJob(definition);
        fixture.Jobs.Job!.SetParameter(
            definition,
            "credential-reference-1",
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));

        var response = await fixture.GetHandler.HandleAsync(
            new GetJobQuery(fixture.Jobs.Job.Id),
            CancellationToken.None);
        var parameter = Assert.Single(response.Parameters);

        Assert.True(parameter.IsRedacted);
        Assert.Equal("[REDACTED]", parameter.DisplayValue);
        Assert.DoesNotContain("credential-reference-1", parameter.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingJobThrowsExplicitApplicationError()
    {
        var fixture = new HandlerFixture();

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => fixture.AddTargetHandler.HandleAsync(
                new AddJobTargetCommand(
                    JobId.New(),
                    new TargetName("server-01"),
                    TestDomainFactory.User),
                CancellationToken.None));
    }

    private sealed class HandlerFixture
    {
        public HandlerFixture()
        {
            CreateHandler = new CreateDraftJobHandler(Jobs, Audits, UnitOfWork, Clock);
            AddTargetHandler = new AddJobTargetHandler(Jobs, Audits, UnitOfWork, Clock);
            SetParameterHandler = new SetJobParameterHandler(
                Jobs,
                Scripts,
                Audits,
                UnitOfWork,
                Clock);
            SubmitHandler = new SubmitJobHandler(Jobs, Scripts, Audits, UnitOfWork, Clock);
            TransitionHandler = new TransitionJobHandler(Jobs, Audits, UnitOfWork, Clock);
            GetHandler = new GetJobHandler(Jobs);
        }

        public FakeJobRepository Jobs { get; } = new();
        public FakeScriptRepository Scripts { get; } = new();
        public FakeAuditWriter Audits { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public TestClock Clock { get; } = new(TestDomainFactory.Time.AddHours(1));
        public CreateDraftJobHandler CreateHandler { get; }
        public AddJobTargetHandler AddTargetHandler { get; }
        public SetJobParameterHandler SetParameterHandler { get; }
        public SubmitJobHandler SubmitHandler { get; }
        public TransitionJobHandler TransitionHandler { get; }
        public GetJobHandler GetHandler { get; }
        public IEnumerable<CancellationToken> ObservedTokens =>
            Jobs.ObservedTokens.Concat(Audits.ObservedTokens).Concat(UnitOfWork.ObservedTokens);

        public static HandlerFixture WithDraftJob(ScriptParameterDefinition? parameter = null)
        {
            var fixture = new HandlerFixture();
            var version = TestDomainFactory.Version(parameter is null ? [] : [parameter]);
            var script = TestDomainFactory.Script(version);
            fixture.Scripts.Script = script;
            fixture.Jobs.Job = TestDomainFactory.DraftJob(script, version);
            return fixture;
        }
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class FakeJobRepository : IJobRepository
    {
        public Job? Job { get; set; }
        public List<CancellationToken> ObservedTokens { get; } = [];

        public Task<Job?> GetByIdAsync(JobId id, CancellationToken cancellationToken)
        {
            ObservedTokens.Add(cancellationToken);
            return Task.FromResult(Job?.Id == id ? Job : null);
        }

        public Task<bool> ExistsAsync(JobId id, CancellationToken cancellationToken)
        {
            ObservedTokens.Add(cancellationToken);
            return Task.FromResult(Job?.Id == id);
        }

        public Task AddAsync(Job job, CancellationToken cancellationToken)
        {
            ObservedTokens.Add(cancellationToken);
            Job = job;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Job job, CancellationToken cancellationToken)
        {
            ObservedTokens.Add(cancellationToken);
            Job = job;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeScriptRepository : IScriptDefinitionRepository
    {
        public ScriptDefinition? Script { get; set; }

        public Task<ScriptDefinition?> GetByIdAsync(
            ScriptDefinitionId id,
            CancellationToken cancellationToken) =>
            Task.FromResult(Script?.Id == id ? Script : null);

        public Task AddAsync(ScriptDefinition definition, CancellationToken cancellationToken)
        {
            Script = definition;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ScriptDefinition definition, CancellationToken cancellationToken)
        {
            Script = definition;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuditWriter : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = [];
        public List<CancellationToken> ObservedTokens { get; } = [];

        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            ObservedTokens.Add(cancellationToken);
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int CommitCount { get; private set; }
        public List<CancellationToken> ObservedTokens { get; } = [];

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            ObservedTokens.Add(cancellationToken);
            CommitCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class UnusedWorkerRepository : IWorkerNodeRepository
    {
        public Task<WorkerNode?> GetByIdAsync(WorkerNodeId id, CancellationToken cancellationToken) =>
            Task.FromResult<WorkerNode?>(null);
        public Task AddAsync(WorkerNode workerNode, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task UpdateAsync(WorkerNode workerNode, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class UnusedCredentialRepository : ICredentialReferenceRepository
    {
        public Task<CredentialReference?> GetByIdAsync(
            CredentialReferenceId id,
            CancellationToken cancellationToken) =>
            Task.FromResult<CredentialReference?>(null);
        public Task AddAsync(
            CredentialReference credentialReference,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task UpdateAsync(
            CredentialReference credentialReference,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
