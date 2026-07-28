using System.Reflection;
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
        var credential = new CredentialReference(
            CredentialReferenceId.New(),
            "TestVault",
            "path/to/credential",
            "Test Credential",
            TestDomainFactory.Time,
            TestDomainFactory.User);
        fixture.Credentials.CredentialReference = credential;

        await fixture.SetParameterHandler.HandleAsync(
            new SetJobParameterCommand(
                fixture.Jobs.Job!.Id,
                definition.Name,
                credential.Id.ToString(),
                TestDomainFactory.User),
            CancellationToken.None);

        var audit = Assert.Single(fixture.Audits.Events);
        Assert.Equal("[REDACTED]", audit.Properties["Value"]);
        Assert.DoesNotContain(
            credential.Id.ToString(),
            string.Join(' ', audit.Properties.Values),
            StringComparison.Ordinal);
        Assert.Equal(credential.Id.ToString(), Assert.Single(fixture.Jobs.Job.Parameters).SerializedValue);
    }

    [Theory]
    [InlineData("hunter2")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task SecureReferenceParameterRejectsInvalidCredentialReferenceFormat(string value)
    {
        var definition = TestDomainFactory.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            required: true,
            sensitive: true);
        var fixture = HandlerFixture.WithDraftJob(definition);

        await Assert.ThrowsAsync<ApplicationValidationException>(
            () => fixture.SetParameterHandler.HandleAsync(
                new SetJobParameterCommand(
                    fixture.Jobs.Job!.Id,
                    definition.Name,
                    value,
                    TestDomainFactory.User),
                CancellationToken.None));

        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task SecureReferenceParameterRejectsMissingAndDisabledReferences()
    {
        var definition = TestDomainFactory.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            required: true,
            sensitive: true);
        var fixture = HandlerFixture.WithDraftJob(definition);
        var credential = new CredentialReference(
            CredentialReferenceId.New(),
            "TestVault",
            "path/to/credential",
            "Test Credential",
            TestDomainFactory.Time,
            TestDomainFactory.User,
            isEnabled: false);

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => fixture.SetParameterHandler.HandleAsync(
                new SetJobParameterCommand(
                    fixture.Jobs.Job!.Id,
                    definition.Name,
                    CredentialReferenceId.New().ToString(),
                    TestDomainFactory.User),
                CancellationToken.None));

        fixture.Credentials.CredentialReference = credential;
        await Assert.ThrowsAsync<ApplicationValidationException>(
            () => fixture.SetParameterHandler.HandleAsync(
                new SetJobParameterCommand(
                    fixture.Jobs.Job!.Id,
                    definition.Name,
                    credential.Id.ToString(),
                    TestDomainFactory.User),
                CancellationToken.None));

        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task SecureReferenceLookupPropagatesCancellationToken()
    {
        var definition = TestDomainFactory.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            required: true,
            sensitive: true);
        var fixture = HandlerFixture.WithDraftJob(definition);
        var credential = new CredentialReference(
            CredentialReferenceId.New(),
            "TestVault",
            "path/to/credential",
            "Test Credential",
            TestDomainFactory.Time,
            TestDomainFactory.User);
        fixture.Credentials.CredentialReference = credential;
        using var source = new CancellationTokenSource();

        await fixture.SetParameterHandler.HandleAsync(
            new SetJobParameterCommand(
                fixture.Jobs.Job!.Id,
                definition.Name,
                credential.Id.ToString(),
                TestDomainFactory.User),
            source.Token);

        Assert.Contains(source.Token, fixture.Credentials.ObservedTokens);
    }

    [Fact]
    public async Task ParameterAuditUsesBoundedMetadataForLongAndMultilineValues()
    {
        var definition = TestDomainFactory.Parameter("Notes");
        var fixture = HandlerFixture.WithDraftJob(definition);
        var value = $"{new string('x', 2100)}\nsecond line";

        await fixture.SetParameterHandler.HandleAsync(
            new SetJobParameterCommand(
                fixture.Jobs.Job!.Id,
                definition.Name,
                value,
                TestDomainFactory.User),
            CancellationToken.None);

        var audit = Assert.Single(fixture.Audits.Events);
        Assert.Equal("String", audit.Properties["ParameterType"]);
        Assert.Equal("False", audit.Properties["IsSensitive"]);
        Assert.Equal(value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), audit.Properties["SerializedLength"]);
        Assert.DoesNotContain(value, string.Join(' ', audit.Properties.Values), StringComparison.Ordinal);
        Assert.Equal(1, fixture.Jobs.UpdateCount);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task ParameterAuditUsesBoundedMetadataForLargeStringArray()
    {
        var definition = TestDomainFactory.Parameter("Items", ScriptParameterType.StringArray);
        var fixture = HandlerFixture.WithDraftJob(definition);
        var value = System.Text.Json.JsonSerializer.Serialize(
            Enumerable.Range(1, 300).Select(number => $"item-{number:D3}").ToArray());

        await fixture.SetParameterHandler.HandleAsync(
            new SetJobParameterCommand(
                fixture.Jobs.Job!.Id,
                definition.Name,
                value,
                TestDomainFactory.User),
            CancellationToken.None);

        var audit = Assert.Single(fixture.Audits.Events);
        Assert.Equal("StringArray", audit.Properties["ParameterType"]);
        Assert.Equal(value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), audit.Properties["SerializedLength"]);
        Assert.DoesNotContain("item-001", string.Join(' ', audit.Properties.Values), StringComparison.Ordinal);
        Assert.Equal(value, Assert.Single(fixture.Jobs.Job.Parameters).SerializedValue);
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
        Assert.NotNull(fixture.Jobs.Job.PolicySnapshot);
        Assert.Equal(fixture.Scripts.Script!.RiskLevel, fixture.Jobs.Job.PolicySnapshot.RiskLevel);
        Assert.Equal(2, fixture.UnitOfWork.CommitCount);
        Assert.Equal(["JobSubmitted", "JobStatusChanged"], fixture.Audits.Events.Select(item => item.EventType));
    }

    [Theory]
    [InlineData(JobStatus.Draft)]
    [InlineData(JobStatus.Submitted)]
    [InlineData(JobStatus.Approved)]
    [InlineData(JobStatus.Rejected)]
    [InlineData(JobStatus.Executing)]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.CompletedWithWarnings)]
    public async Task GenericTransitionHandlerRejectsProtectedLifecycleOperations(JobStatus status)
    {
        var fixture = HandlerFixture.WithSubmittedJob();
        var originalStatus = fixture.Jobs.Job!.Status;

        await Assert.ThrowsAsync<ApplicationValidationException>(
            () => fixture.TransitionHandler.HandleAsync(
                new TransitionJobCommand(
                    fixture.Jobs.Job.Id,
                    status,
                    TestDomainFactory.OtherUser),
                CancellationToken.None));

        Assert.Equal(originalStatus, fixture.Jobs.Job.Status);
        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task ApprovalHandlerRecordsEvidenceBeforePersistence()
    {
        var fixture = HandlerFixture.WithAwaitingApprovalJob();

        await fixture.ApproveHandler.HandleAsync(
            new ApproveJobCommand(
                fixture.Jobs.Job!.Id,
                TestDomainFactory.Fingerprint,
                "Reviewed.",
                TestDomainFactory.OtherUser),
            CancellationToken.None);

        Assert.Equal(JobStatus.Approved, fixture.Jobs.Job.Status);
        Assert.Equal(ApprovalDecision.Approved, Assert.Single(fixture.Jobs.Job.Approvals).Decision);
        Assert.Equal("JobApproved", Assert.Single(fixture.Audits.Events).EventType);
        Assert.Equal(1, fixture.Jobs.UpdateCount);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task FailedApprovalDoesNotPersistAuditOrCommit()
    {
        var fixture = HandlerFixture.WithAwaitingApprovalJob(RiskLevel.High);

        await Assert.ThrowsAsync<Domain.Exceptions.DomainValidationException>(
            () => fixture.ApproveHandler.HandleAsync(
                new ApproveJobCommand(
                    fixture.Jobs.Job!.Id,
                    TestDomainFactory.Fingerprint,
                    null,
                    TestDomainFactory.User),
                CancellationToken.None));

        Assert.Equal(JobStatus.AwaitingApproval, fixture.Jobs.Job!.Status);
        Assert.Empty(fixture.Jobs.Job.Approvals);
        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task RejectionHandlerRecordsEvidenceBeforePersistence()
    {
        var fixture = HandlerFixture.WithAwaitingApprovalJob();

        await fixture.RejectHandler.HandleAsync(
            new RejectJobCommand(
                fixture.Jobs.Job!.Id,
                TestDomainFactory.Fingerprint,
                "Rejected after review.",
                TestDomainFactory.OtherUser),
            CancellationToken.None);

        Assert.Equal(JobStatus.Rejected, fixture.Jobs.Job.Status);
        Assert.Equal(ApprovalDecision.Rejected, Assert.Single(fixture.Jobs.Job.Approvals).Decision);
        Assert.Equal("JobRejected", Assert.Single(fixture.Audits.Events).EventType);
        Assert.Equal(1, fixture.Jobs.UpdateCount);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task ReadOnlyCompletionHandlerUsesCapturedPolicy()
    {
        var fixture = HandlerFixture.WithDryRunCompletedJob(
            RiskLevel.ReadOnly,
            [ExecutionPhase.DryRun]);

        await fixture.CompleteReadOnlyHandler.HandleAsync(
            new CompleteReadOnlyJobCommand(
                fixture.Jobs.Job!.Id,
                TestDomainFactory.OtherUser),
            CancellationToken.None);

        Assert.Equal(JobStatus.Completed, fixture.Jobs.Job.Status);
        Assert.Equal("ReadOnlyJobCompleted", Assert.Single(fixture.Audits.Events).EventType);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task GetJobQueryNeverReturnsSensitiveValue()
    {
        var definition = TestDomainFactory.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            sensitive: true);
        var fixture = HandlerFixture.WithDraftJob(definition);
        var credentialReferenceId = CredentialReferenceId.New().ToString();
        fixture.Jobs.Job!.SetParameter(
            definition,
            credentialReferenceId,
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));

        var response = await fixture.GetHandler.HandleAsync(
            new GetJobQuery(fixture.Jobs.Job.Id),
            CancellationToken.None);
        var parameter = Assert.Single(response.Parameters);

        Assert.True(parameter.IsRedacted);
        Assert.Equal("[REDACTED]", parameter.DisplayValue);
        Assert.DoesNotContain(credentialReferenceId, parameter.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(JobStatus.Failed, false)]
    [InlineData(JobStatus.Cancelled, false)]
    [InlineData(JobStatus.TimedOut, false)]
    [InlineData(JobStatus.Blocked, false)]
    [InlineData(JobStatus.NotRun, false)]
    [InlineData(JobStatus.Failed, true)]
    [InlineData(JobStatus.Cancelled, true)]
    [InlineData(JobStatus.TimedOut, true)]
    [InlineData(JobStatus.Blocked, true)]
    [InlineData(JobStatus.NotRun, true)]
    public async Task GenericTerminalTransitionRejectsActiveExecution(
        JobStatus terminalStatus,
        bool postValidation)
    {
        var fixture = HandlerFixture.WithExecutingJob(postValidation);
        var originalStatus = fixture.Jobs.Job!.Status;
        var execution = Assert.Single(fixture.Jobs.Job.Executions);

        await Assert.ThrowsAsync<ApplicationValidationException>(
            () => fixture.TransitionHandler.HandleAsync(
                new TransitionJobCommand(
                    fixture.Jobs.Job.Id,
                    terminalStatus,
                    TestDomainFactory.OtherUser),
                CancellationToken.None));

        Assert.Equal(originalStatus, fixture.Jobs.Job.Status);
        Assert.Null(execution.CompletedUtc);
        Assert.Null(execution.Outcome);
        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Theory]
    [InlineData(ExecutionOutcome.Failed, JobStatus.Failed, 1)]
    [InlineData(ExecutionOutcome.Cancelled, JobStatus.Cancelled, null)]
    [InlineData(ExecutionOutcome.TimedOut, JobStatus.TimedOut, null)]
    [InlineData(ExecutionOutcome.Blocked, JobStatus.Blocked, null)]
    [InlineData(ExecutionOutcome.NotRun, JobStatus.NotRun, null)]
    public async Task RecordExecutionOutcomeHandlerPersistsTerminalOutcome(
        ExecutionOutcome outcome,
        JobStatus expectedStatus,
        int? exitCode)
    {
        var fixture = HandlerFixture.WithExecutingJob();
        using var source = new CancellationTokenSource();

        await fixture.RecordExecutionOutcomeHandler.HandleAsync(
            new RecordExecutionOutcomeCommand(
                fixture.Jobs.Job!.Id,
                outcome,
                exitCode,
                "  diagnostic summary  ",
                TestDomainFactory.OtherUser),
            source.Token);

        var execution = Assert.Single(fixture.Jobs.Job.Executions);
        var audit = Assert.Single(fixture.Audits.Events);
        Assert.Equal(expectedStatus, fixture.Jobs.Job.Status);
        Assert.Equal(outcome, execution.Outcome);
        Assert.NotNull(execution.CompletedUtc);
        Assert.Equal("diagnostic summary", execution.Summary);
        Assert.Equal("ExecutionOutcomeRecorded", audit.EventType);
        Assert.Equal(outcome.ToString(), audit.Properties["Outcome"]);
        Assert.Equal((exitCode is not null).ToString(), audit.Properties["ExitCodePresent"]);
        Assert.Equal("True", audit.Properties["SummaryProvided"]);
        Assert.Equal("22", audit.Properties["SummaryLength"]);
        Assert.Equal("1", audit.Properties["AttemptNumber"]);
        Assert.Equal("False", audit.Properties["WorkerNodeIdPresent"]);
        Assert.DoesNotContain("diagnostic summary", string.Join(' ', audit.Properties.Values), StringComparison.Ordinal);
        Assert.Equal(1, fixture.Jobs.UpdateCount);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
        Assert.Contains(source.Token, fixture.ObservedTokens);
    }

    [Fact]
    public async Task InvalidExecutionOutcomeHandlerDoesNotPersistAuditOrCommit()
    {
        var fixture = HandlerFixture.WithExecutingJob();
        var execution = Assert.Single(fixture.Jobs.Job!.Executions);
        var updated = fixture.Jobs.Job.UpdatedUtc;

        await Assert.ThrowsAsync<Domain.Exceptions.DomainValidationException>(
            () => fixture.RecordExecutionOutcomeHandler.HandleAsync(
                new RecordExecutionOutcomeCommand(
                    fixture.Jobs.Job.Id,
                    (ExecutionOutcome)999,
                    0,
                    null,
                    TestDomainFactory.OtherUser),
                CancellationToken.None));

        Assert.Equal(JobStatus.Executing, fixture.Jobs.Job.Status);
        Assert.Equal(updated, fixture.Jobs.Job.UpdatedUtc);
        Assert.Null(execution.CompletedUtc);
        Assert.Null(execution.Outcome);
        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task UndefinedJobStatusTransitionDoesNotPersistAuditOrCommit()
    {
        var fixture = HandlerFixture.WithSubmittedJob();

        await Assert.ThrowsAsync<ApplicationValidationException>(
            () => fixture.TransitionHandler.HandleAsync(
                new TransitionJobCommand(
                    fixture.Jobs.Job!.Id,
                    (JobStatus)999,
                    TestDomainFactory.OtherUser),
                CancellationToken.None));

        Assert.Equal(JobStatus.Submitted, fixture.Jobs.Job!.Status);
        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task SubmitHandlerRejectsExecuteVersionWithoutDryRunSupport()
    {
        var fixture = new HandlerFixture();
        var version = TestDomainFactory.Version(
            publish: false,
            phases: [ExecutionPhase.Execute]);
        ForcePublished(version);
        var script = TestDomainFactory.Script(version);
        var job = TestDomainFactory.DraftJob(script, version, ExecutionPhase.Execute);
        job.AddTarget(new TargetName("server-01"), TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1));
        fixture.Scripts.Script = script;
        fixture.Jobs.Job = job;

        await Assert.ThrowsAsync<Domain.Exceptions.DomainValidationException>(
            () => fixture.SubmitHandler.HandleAsync(
                new SubmitJobCommand(job.Id, TestDomainFactory.User),
                CancellationToken.None));

        Assert.Equal(JobStatus.Draft, job.Status);
        Assert.Null(job.PolicySnapshot);
        Assert.Null(job.SubmittedUtc);
        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
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

    private static void ForcePublished(ScriptVersion version)
    {
        var field = typeof(ScriptVersion).GetField(
            $"<{nameof(ScriptVersion.IsPublished)}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);

        field?.SetValue(version, true);
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
                Credentials,
                Audits,
                UnitOfWork,
                Clock);
            SubmitHandler = new SubmitJobHandler(Jobs, Scripts, Audits, UnitOfWork, Clock);
            TransitionHandler = new TransitionJobHandler(Jobs, Audits, UnitOfWork, Clock);
            ApproveHandler = new ApproveJobHandler(Jobs, Audits, UnitOfWork, Clock);
            RejectHandler = new RejectJobHandler(Jobs, Audits, UnitOfWork, Clock);
            CompleteReadOnlyHandler = new CompleteReadOnlyJobHandler(Jobs, Audits, UnitOfWork, Clock);
            CompleteValidationHandler = new CompleteValidationJobHandler(Jobs, Audits, UnitOfWork, Clock);
            CompleteDryRunHandler = new CompleteDryRunJobHandler(Jobs, Audits, UnitOfWork, Clock);
            RecordExecutionOutcomeHandler = new RecordExecutionOutcomeHandler(Jobs, Audits, UnitOfWork, Clock);
            GetHandler = new GetJobHandler(Jobs);
        }

        public FakeJobRepository Jobs { get; } = new();
        public FakeScriptRepository Scripts { get; } = new();
        public FakeCredentialRepository Credentials { get; } = new();
        public FakeAuditWriter Audits { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public TestClock Clock { get; } = new(TestDomainFactory.Time.AddHours(1));
        public CreateDraftJobHandler CreateHandler { get; }
        public AddJobTargetHandler AddTargetHandler { get; }
        public SetJobParameterHandler SetParameterHandler { get; }
        public SubmitJobHandler SubmitHandler { get; }
        public TransitionJobHandler TransitionHandler { get; }
        public ApproveJobHandler ApproveHandler { get; }
        public RejectJobHandler RejectHandler { get; }
        public CompleteReadOnlyJobHandler CompleteReadOnlyHandler { get; }
        public CompleteValidationJobHandler CompleteValidationHandler { get; }
        public CompleteDryRunJobHandler CompleteDryRunHandler { get; }
        public RecordExecutionOutcomeHandler RecordExecutionOutcomeHandler { get; }
        public GetJobHandler GetHandler { get; }
        public IEnumerable<CancellationToken> ObservedTokens =>
            Jobs.ObservedTokens
                .Concat(Credentials.ObservedTokens)
                .Concat(Audits.ObservedTokens)
                .Concat(UnitOfWork.ObservedTokens);

        public static HandlerFixture WithDraftJob(ScriptParameterDefinition? parameter = null)
        {
            var fixture = new HandlerFixture();
            var version = TestDomainFactory.Version(parameter is null ? [] : [parameter]);
            var script = TestDomainFactory.Script(version);
            fixture.Scripts.Script = script;
            fixture.Jobs.Job = TestDomainFactory.DraftJob(script, version);
            return fixture;
        }

        public static HandlerFixture WithSubmittedJob(
            RiskLevel riskLevel = RiskLevel.Low,
            ExecutionPhase requestedPhase = ExecutionPhase.Execute,
            IEnumerable<ExecutionPhase>? supportedPhases = null)
        {
            var fixture = new HandlerFixture();
            var version = TestDomainFactory.Version(phases: supportedPhases);
            var script = TestDomainFactory.Script(version, riskLevel);
            fixture.Scripts.Script = script;
            fixture.Jobs.Job = TestDomainFactory.SubmittedJob(
                script,
                version,
                requestedPhase: requestedPhase);
            return fixture;
        }

        public static HandlerFixture WithAwaitingApprovalJob(RiskLevel riskLevel = RiskLevel.Low)
        {
            var fixture = WithSubmittedJob(riskLevel);
            TestDomainFactory.AdvanceToAwaitingApproval(fixture.Jobs.Job!);
            return fixture;
        }

        public static HandlerFixture WithDryRunCompletedJob(
            RiskLevel riskLevel,
            IEnumerable<ExecutionPhase> phases)
        {
            var fixture = new HandlerFixture();
            var version = TestDomainFactory.Version(phases: phases);
            var script = TestDomainFactory.Script(version, riskLevel);
            fixture.Scripts.Script = script;
            var job = TestDomainFactory.SubmittedJob(script, version);
            job.MarkValidated(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
            job.QueueDryRun(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
            job.StartDryRun(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
            job.CompleteDryRun(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
            fixture.Jobs.Job = job;
            return fixture;
        }

        public static HandlerFixture WithExecutingJob(bool postValidation = false)
        {
            var fixture = WithSubmittedJob();
            _ = TestDomainFactory.StartExecution(fixture.Jobs.Job!);
            if (postValidation)
            {
                fixture.Jobs.Job!.BeginPostValidation(
                    TestDomainFactory.OtherUser,
                    fixture.Jobs.Job.UpdatedUtc.AddMinutes(1));
            }

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
        public int UpdateCount { get; private set; }
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
            UpdateCount++;
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

    private sealed class FakeCredentialRepository : ICredentialReferenceRepository
    {
        public CredentialReference? CredentialReference { get; set; }
        public List<CancellationToken> ObservedTokens { get; } = [];

        public Task<CredentialReference?> GetByIdAsync(
            CredentialReferenceId id,
            CancellationToken cancellationToken)
        {
            ObservedTokens.Add(cancellationToken);
            return Task.FromResult(CredentialReference?.Id == id ? CredentialReference : null);
        }

        public Task AddAsync(
            CredentialReference credentialReference,
            CancellationToken cancellationToken)
        {
            ObservedTokens.Add(cancellationToken);
            CredentialReference = credentialReference;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(
            CredentialReference credentialReference,
            CancellationToken cancellationToken)
        {
            ObservedTokens.Add(cancellationToken);
            CredentialReference = credentialReference;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SubmitHandlerRejectsDisabledScriptWithoutPersistence()
    {
        var fixture = HandlerFixture.WithDraftJob();
        fixture.Scripts.Script!.Disable(TestDomainFactory.Time.AddMinutes(1));
        fixture.Jobs.Job!.AddTarget(
            new TargetName("server-01"),
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));

        await Assert.ThrowsAsync<Domain.Exceptions.DomainValidationException>(
            () => fixture.SubmitHandler.HandleAsync(
                new SubmitJobCommand(fixture.Jobs.Job.Id, TestDomainFactory.User),
                CancellationToken.None));

        Assert.Equal(JobStatus.Draft, fixture.Jobs.Job.Status);
        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task TransitionHandlerDoesNotPersistWhenRequestedPhaseRuleFails()
    {
        var fixture = HandlerFixture.WithSubmittedJob(requestedPhase: ExecutionPhase.DryRun);
        fixture.Jobs.Job!.MarkValidated(TestDomainFactory.OtherUser, fixture.Jobs.Job.UpdatedUtc.AddMinutes(1));
        fixture.Jobs.Job.QueueDryRun(TestDomainFactory.OtherUser, fixture.Jobs.Job.UpdatedUtc.AddMinutes(1));
        fixture.Jobs.Job.StartDryRun(TestDomainFactory.OtherUser, fixture.Jobs.Job.UpdatedUtc.AddMinutes(1));
        fixture.Jobs.Job.CompleteDryRun(TestDomainFactory.OtherUser, fixture.Jobs.Job.UpdatedUtc.AddMinutes(1));

        await Assert.ThrowsAsync<Domain.Exceptions.DomainValidationException>(
            () => fixture.TransitionHandler.HandleAsync(
                new TransitionJobCommand(
                    fixture.Jobs.Job.Id,
                    JobStatus.AwaitingApproval,
                    TestDomainFactory.OtherUser),
                CancellationToken.None));

        Assert.Equal(JobStatus.DryRunCompleted, fixture.Jobs.Job.Status);
        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task RequestedPhaseCompletionHandlersPersistOnlyOnValidPhase()
    {
        var validationFixture = HandlerFixture.WithSubmittedJob(
            requestedPhase: ExecutionPhase.Validation,
            supportedPhases: [ExecutionPhase.Validation]);
        validationFixture.Jobs.Job!.MarkValidated(
            TestDomainFactory.OtherUser,
            validationFixture.Jobs.Job.UpdatedUtc.AddMinutes(1));

        await validationFixture.CompleteValidationHandler.HandleAsync(
            new CompleteValidationJobCommand(
                validationFixture.Jobs.Job.Id,
                TestDomainFactory.OtherUser),
            CancellationToken.None);

        Assert.Equal(JobStatus.Completed, validationFixture.Jobs.Job.Status);
        Assert.Equal("ValidationJobCompleted", Assert.Single(validationFixture.Audits.Events).EventType);

        var dryRunFixture = HandlerFixture.WithSubmittedJob(
            requestedPhase: ExecutionPhase.DryRun,
            supportedPhases: [ExecutionPhase.DryRun]);
        dryRunFixture.Jobs.Job!.MarkValidated(
            TestDomainFactory.OtherUser,
            dryRunFixture.Jobs.Job.UpdatedUtc.AddMinutes(1));
        dryRunFixture.Jobs.Job.QueueDryRun(
            TestDomainFactory.OtherUser,
            dryRunFixture.Jobs.Job.UpdatedUtc.AddMinutes(1));
        dryRunFixture.Jobs.Job.StartDryRun(
            TestDomainFactory.OtherUser,
            dryRunFixture.Jobs.Job.UpdatedUtc.AddMinutes(1));
        dryRunFixture.Jobs.Job.CompleteDryRun(
            TestDomainFactory.OtherUser,
            dryRunFixture.Jobs.Job.UpdatedUtc.AddMinutes(1));

        await dryRunFixture.CompleteDryRunHandler.HandleAsync(
            new CompleteDryRunJobCommand(
                dryRunFixture.Jobs.Job.Id,
                TestDomainFactory.OtherUser),
            CancellationToken.None);

        Assert.Equal(JobStatus.Completed, dryRunFixture.Jobs.Job.Status);
        Assert.Equal("DryRunJobCompleted", Assert.Single(dryRunFixture.Audits.Events).EventType);
    }
}
