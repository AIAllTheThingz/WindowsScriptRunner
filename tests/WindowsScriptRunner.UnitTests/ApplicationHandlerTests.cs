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
    public void StartExecutionAttemptRequestCarriesOptionalWorkerAssignment()
    {
        var jobId = Guid.NewGuid();
        var workerNodeId = Guid.NewGuid();

        var assigned = new Contracts.Jobs.StartExecutionAttemptRequest(jobId, workerNodeId);
        var unassigned = new Contracts.Jobs.StartExecutionAttemptRequest(jobId, null);

        Assert.Equal(jobId, assigned.JobId);
        Assert.Equal(workerNodeId, assigned.WorkerNodeId);
        Assert.Null(unassigned.WorkerNodeId);
    }

    [Fact]
    public async Task CreateDraftPersistsAuditsCommitsAndPropagatesCancellation()
    {
        var fixture = new HandlerFixture();
        var version = TestDomainFactory.Version();
        var script = TestDomainFactory.Script(version);
        fixture.Scripts.Script = script;
        using var source = new CancellationTokenSource();
        var command = new CreateDraftJobCommand(
            script.Id,
            version.Id,
            ExecutionPhase.DryRun,
            TestDomainFactory.User);

        var id = await fixture.CreateHandler.HandleAsync(command, source.Token);

        Assert.Equal(id, fixture.Jobs.Job?.Id);
        Assert.Equal("JobDraftCreated", Assert.Single(fixture.Audits.Events).EventType);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
        Assert.All(fixture.ObservedTokens, token => Assert.Equal(source.Token, token));
    }

    [Fact]
    public async Task CreateDraftRejectsMissingScriptWithoutSideEffects()
    {
        var fixture = new HandlerFixture();

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => fixture.CreateHandler.HandleAsync(
                new CreateDraftJobCommand(
                    ScriptDefinitionId.New(),
                    ScriptVersionId.New(),
                    ExecutionPhase.DryRun,
                    TestDomainFactory.User),
                CancellationToken.None));

        Assert.Null(fixture.Jobs.Job);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task CreateDraftRejectsVersionFromAnotherScriptWithoutSideEffects()
    {
        var fixture = new HandlerFixture();
        var script = TestDomainFactory.Script(TestDomainFactory.Version());
        fixture.Scripts.Script = script;

        await Assert.ThrowsAsync<Domain.Exceptions.InvalidScriptVersionException>(
            () => fixture.CreateHandler.HandleAsync(
                new CreateDraftJobCommand(
                    script.Id,
                    ScriptVersionId.New(),
                    ExecutionPhase.DryRun,
                    TestDomainFactory.User),
                CancellationToken.None));

        Assert.Null(fixture.Jobs.Job);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
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
        Assert.Equal("0", audit.Properties["SerializedLength"]);
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

        await Assert.ThrowsAsync<Domain.Exceptions.InvalidJobParameterException>(
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task OptionalSecureReferenceCanBeClearedWithoutCredentialLookup(string? absentValue)
    {
        var definition = TestDomainFactory.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            sensitive: true);
        var fixture = HandlerFixture.WithDraftJob(definition);
        var previousId = CredentialReferenceId.New().ToString();
        fixture.Jobs.Job!.SetParameterValue(
            definition.Name,
            previousId,
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));
        using var source = new CancellationTokenSource();

        await fixture.SetParameterHandler.HandleAsync(
            new SetJobParameterCommand(
                fixture.Jobs.Job.Id,
                definition.Name,
                absentValue,
                TestDomainFactory.OtherUser),
            source.Token);
        var response = await fixture.GetHandler.HandleAsync(
            new GetJobQuery(fixture.Jobs.Job.Id),
            source.Token);

        Assert.Empty(fixture.Jobs.Job.Parameters);
        Assert.Empty(response.Parameters);
        Assert.DoesNotContain(previousId, response.ToString(), StringComparison.Ordinal);
        Assert.Empty(fixture.Credentials.ObservedTokens);
        Assert.Equal(fixture.Clock.UtcNow, fixture.Jobs.Job.UpdatedUtc);
        Assert.Equal(TestDomainFactory.OtherUser, fixture.Jobs.Job.LastActingUser);
        Assert.Equal(1, fixture.Jobs.UpdateCount);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
        Assert.Contains(source.Token, fixture.Jobs.ObservedTokens);
        Assert.Contains(source.Token, fixture.Scripts.ObservedTokens);

        var audit = Assert.Single(fixture.Audits.Events);
        Assert.Equal("JobParameterCleared", audit.EventType);
        Assert.Equal("Credential", audit.Properties["Parameter"]);
        Assert.Equal("SecureReference", audit.Properties["ParameterType"]);
        Assert.Equal("True", audit.Properties["IsSensitive"]);
        Assert.Equal("True", audit.Properties["BindingExisted"]);
        Assert.Equal("False", audit.Properties["ValueProvided"]);
        Assert.Equal("False", audit.Properties["ReferenceSupplied"]);
        Assert.DoesNotContain(previousId, string.Join(' ', audit.Properties.Values), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ScriptParameterType.String, "stale-string", null)]
    [InlineData(ScriptParameterType.String, "stale-string", "")]
    [InlineData(ScriptParameterType.String, "stale-string", " \t ")]
    [InlineData(ScriptParameterType.StringArray, "[\"stale\"]", null)]
    [InlineData(ScriptParameterType.StringArray, "[\"stale\"]", "")]
    [InlineData(ScriptParameterType.StringArray, "[\"stale\"]", " \t ")]
    [InlineData(ScriptParameterType.Integer, "42", null)]
    [InlineData(ScriptParameterType.Integer, "42", "")]
    [InlineData(ScriptParameterType.Integer, "42", " \t ")]
    [InlineData(ScriptParameterType.Boolean, "true", null)]
    [InlineData(ScriptParameterType.Boolean, "true", "")]
    [InlineData(ScriptParameterType.Boolean, "true", " \t ")]
    [InlineData(ScriptParameterType.DateTime, "2026-07-28T12:00:00+00:00", null)]
    [InlineData(ScriptParameterType.DateTime, "2026-07-28T12:00:00+00:00", "")]
    [InlineData(ScriptParameterType.DateTime, "2026-07-28T12:00:00+00:00", " \t ")]
    [InlineData(ScriptParameterType.Enum, "Safe", null)]
    [InlineData(ScriptParameterType.Enum, "Safe", "")]
    [InlineData(ScriptParameterType.Enum, "Safe", " \t ")]
    public async Task OptionalParameterAbsentValueClearsExplicitBinding(
        ScriptParameterType parameterType,
        string previousValue,
        string? absentValue)
    {
        var allowedValues = parameterType == ScriptParameterType.Enum
            ? new[] { "Safe", "Fast" }
            : null;
        var definition = TestDomainFactory.Parameter(
            "OptionalValue",
            parameterType,
            allowedValues: allowedValues);
        var fixture = HandlerFixture.WithDraftJob(definition);
        fixture.Jobs.Job!.SetParameterValue(
            definition.Name,
            previousValue,
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));

        await fixture.SetParameterHandler.HandleAsync(
            new SetJobParameterCommand(
                fixture.Jobs.Job.Id,
                definition.Name,
                absentValue,
                TestDomainFactory.OtherUser),
            CancellationToken.None);

        Assert.Empty(fixture.Jobs.Job.Parameters);
        var audit = Assert.Single(fixture.Audits.Events);
        Assert.Equal("JobParameterCleared", audit.EventType);
        Assert.Equal(parameterType.ToString(), audit.Properties["ParameterType"]);
        Assert.Equal("True", audit.Properties["BindingExisted"]);
        Assert.DoesNotContain(previousValue, string.Join(' ', audit.Properties.Values), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClearingAlreadyAbsentOptionalParameterIsSafeAndAudited()
    {
        var definition = TestDomainFactory.Parameter("OptionalValue");
        var fixture = HandlerFixture.WithDraftJob(definition);

        await fixture.SetParameterHandler.HandleAsync(
            new SetJobParameterCommand(
                fixture.Jobs.Job!.Id,
                definition.Name,
                null,
                TestDomainFactory.OtherUser),
            CancellationToken.None);

        Assert.Empty(fixture.Jobs.Job.Parameters);
        Assert.Equal(fixture.Clock.UtcNow, fixture.Jobs.Job.UpdatedUtc);
        Assert.Equal(TestDomainFactory.OtherUser, fixture.Jobs.Job.LastActingUser);
        Assert.Equal(1, fixture.Jobs.UpdateCount);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
        var audit = Assert.Single(fixture.Audits.Events);
        Assert.Equal("JobParameterCleared", audit.EventType);
        Assert.Equal("False", audit.Properties["BindingExisted"]);
    }

    [Fact]
    public async Task ClearingRequiredParameterWithDefaultRestoresDefinitionOwnedDefault()
    {
        var definition = TestDomainFactory.Parameter(
            "RetryCount",
            ScriptParameterType.Integer,
            required: true,
            defaultValue: "3");
        var fixture = HandlerFixture.WithDraftJob(definition);
        fixture.Jobs.Job!.SetParameterValue(
            definition.Name,
            "7",
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));

        await fixture.SetParameterHandler.HandleAsync(
            new SetJobParameterCommand(
                fixture.Jobs.Job.Id,
                definition.Name,
                null,
                TestDomainFactory.OtherUser),
            CancellationToken.None);

        Assert.Empty(fixture.Jobs.Job.Parameters);
        Assert.Equal("3", definition.DefaultValue);
        var audit = Assert.Single(fixture.Audits.Events);
        Assert.Equal("JobParameterCleared", audit.EventType);
        Assert.Equal("True", audit.Properties["BindingExisted"]);
        var joined = string.Join(' ', audit.Properties.Values);
        Assert.DoesNotContain("3", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("7", joined, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task RequiredSecureReferenceAbsenceIsRejectedBeforeLookupWithoutMutation(
        string? absentValue)
    {
        var definition = TestDomainFactory.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            required: true,
            sensitive: true);
        var fixture = HandlerFixture.WithDraftJob(definition);
        var previousId = CredentialReferenceId.New().ToString();
        fixture.Jobs.Job!.SetParameterValue(
            definition.Name,
            previousId,
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));
        var updatedUtc = fixture.Jobs.Job.UpdatedUtc;
        var lastActingUser = fixture.Jobs.Job.LastActingUser;

        await Assert.ThrowsAsync<Domain.Exceptions.InvalidJobParameterException>(
            () => fixture.SetParameterHandler.HandleAsync(
                new SetJobParameterCommand(
                    fixture.Jobs.Job.Id,
                    definition.Name,
                    absentValue,
                    TestDomainFactory.OtherUser),
                CancellationToken.None));

        var parameter = Assert.Single(fixture.Jobs.Job.Parameters);
        Assert.Equal(previousId, parameter.SerializedValue);
        Assert.Equal(updatedUtc, fixture.Jobs.Job.UpdatedUtc);
        Assert.Equal(lastActingUser, fixture.Jobs.Job.LastActingUser);
        Assert.Empty(fixture.Credentials.ObservedTokens);
        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task OptionalParameterClearRemainsDraftOnly()
    {
        var definition = TestDomainFactory.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            sensitive: true);
        var version = TestDomainFactory.Version([definition]);
        var script = TestDomainFactory.Script(version);
        var previousId = CredentialReferenceId.New().ToString();
        var fixture = new HandlerFixture
        {
            Scripts = { Script = script },
            Jobs =
            {
                Job = TestDomainFactory.SubmittedJob(
                    script,
                    version,
                    [(definition, previousId)]),
            },
        };
        var updatedUtc = fixture.Jobs.Job!.UpdatedUtc;

        await Assert.ThrowsAsync<Domain.Exceptions.DomainValidationException>(
            () => fixture.SetParameterHandler.HandleAsync(
                new SetJobParameterCommand(
                    fixture.Jobs.Job.Id,
                    definition.Name,
                    null,
                    TestDomainFactory.OtherUser),
                CancellationToken.None));

        Assert.Equal(previousId, Assert.Single(fixture.Jobs.Job.Parameters).SerializedValue);
        Assert.Equal(updatedUtc, fixture.Jobs.Job.UpdatedUtc);
        Assert.Empty(fixture.Credentials.ObservedTokens);
        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task PresentSecureReferenceMustBeCanonicalBeforeCredentialLookup()
    {
        var definition = TestDomainFactory.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            required: true,
            sensitive: true);
        var fixture = HandlerFixture.WithDraftJob(definition);
        var nonCanonical = new CredentialReferenceId(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"))
            .ToString()
            .ToUpperInvariant();

        await Assert.ThrowsAsync<Domain.Exceptions.InvalidJobParameterException>(
            () => fixture.SetParameterHandler.HandleAsync(
                new SetJobParameterCommand(
                    fixture.Jobs.Job!.Id,
                    definition.Name,
                    nonCanonical,
                    TestDomainFactory.User),
                CancellationToken.None));

        Assert.Empty(fixture.Credentials.ObservedTokens);
        Assert.Empty(fixture.Jobs.Job!.Parameters);
        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
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
    public async Task SetParameterHandlerUsesPinnedDefinitionForAuditAndStorage()
    {
        var pinned = TestDomainFactory.Parameter("Token", sensitive: true);
        var spoofed = TestDomainFactory.Parameter("Token", ScriptParameterType.Integer, sensitive: false);
        var fixture = HandlerFixture.WithDraftJob(pinned);

        await fixture.SetParameterHandler.HandleAsync(
            new SetJobParameterCommand(
                fixture.Jobs.Job!.Id,
                spoofed.Name,
                "secret-marker",
                TestDomainFactory.User),
            CancellationToken.None);

        var audit = Assert.Single(fixture.Audits.Events);
        var parameter = Assert.Single(fixture.Jobs.Job.Parameters);
        Assert.Equal("Token", parameter.Name);
        Assert.Equal("secret-marker", parameter.SerializedValue);
        Assert.Equal("String", audit.Properties["ParameterType"]);
        Assert.Equal("True", audit.Properties["IsSensitive"]);
        Assert.Equal("0", audit.Properties["SerializedLength"]);
        Assert.DoesNotContain("secret-marker", string.Join(' ', audit.Properties.Values), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetParameterHandlerRejectsInvalidPinnedValueWithoutPersistence()
    {
        var definition = TestDomainFactory.Parameter("Count", ScriptParameterType.Integer);
        var fixture = HandlerFixture.WithDraftJob(definition);
        var updated = fixture.Jobs.Job!.UpdatedUtc;

        await Assert.ThrowsAsync<Domain.Exceptions.InvalidJobParameterException>(
            () => fixture.SetParameterHandler.HandleAsync(
                new SetJobParameterCommand(
                    fixture.Jobs.Job.Id,
                    definition.Name,
                    "not-an-integer",
                    TestDomainFactory.User),
                CancellationToken.None));

        Assert.Empty(fixture.Jobs.Job.Parameters);
        Assert.Equal(updated, fixture.Jobs.Job.UpdatedUtc);
        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task SetParameterHandlerPropagatesCancellationToJobAndScriptRepositories()
    {
        var definition = TestDomainFactory.Parameter("Mode");
        var fixture = HandlerFixture.WithDraftJob(definition);
        using var source = new CancellationTokenSource();

        await fixture.SetParameterHandler.HandleAsync(
            new SetJobParameterCommand(
                fixture.Jobs.Job!.Id,
                definition.Name,
                "Safe",
                TestDomainFactory.User),
            source.Token);

        Assert.Contains(source.Token, fixture.Jobs.ObservedTokens);
        Assert.Contains(source.Token, fixture.Scripts.ObservedTokens);
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
        fixture.Jobs.Job!.SetParameterValue(
            definition.Name,
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

    [Fact]
    public async Task GetJobQueryRedactsSpoofedDraftSensitiveParameter()
    {
        var pinned = TestDomainFactory.Parameter("Token", sensitive: true);
        var spoofed = TestDomainFactory.Parameter("Token", sensitive: false);
        var fixture = HandlerFixture.WithDraftJob(pinned);
        fixture.Jobs.Job!.SetParameterValue(
            spoofed.Name,
            "secret-marker",
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));

        var response = await fixture.GetHandler.HandleAsync(
            new GetJobQuery(fixture.Jobs.Job.Id),
            CancellationToken.None);
        var parameter = Assert.Single(response.Parameters);

        Assert.Equal("Token", parameter.Name);
        Assert.Equal("String", parameter.ParameterType);
        Assert.True(parameter.IsSensitive);
        Assert.True(parameter.IsRedacted);
        Assert.Equal("[REDACTED]", parameter.DisplayValue);
        Assert.DoesNotContain("secret-marker", parameter.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-marker", response.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetJobQueryFailsClosedForUnknownDraftParameter()
    {
        var definition = TestDomainFactory.Parameter("Mode");
        var fixture = HandlerFixture.WithDraftJob(definition);
        fixture.Jobs.Job!.SetParameterValue(
            "Unknown",
            "secret-marker",
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));

        var exception = await Assert.ThrowsAsync<ApplicationConflictException>(
            () => fixture.GetHandler.HandleAsync(
                new GetJobQuery(fixture.Jobs.Job.Id),
                CancellationToken.None));

        Assert.DoesNotContain("secret-marker", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetJobQueryFailsClosedForInvalidPinnedValue()
    {
        var definition = TestDomainFactory.Parameter("Count", ScriptParameterType.Integer);
        var fixture = HandlerFixture.WithDraftJob(definition);
        fixture.Jobs.Job!.SetParameterValue(
            definition.Name,
            "secret-marker",
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));

        var exception = await Assert.ThrowsAsync<ApplicationConflictException>(
            () => fixture.GetHandler.HandleAsync(
                new GetJobQuery(fixture.Jobs.Job.Id),
                CancellationToken.None));

        Assert.DoesNotContain("secret-marker", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Submitted")]
    [InlineData("Terminal")]
    public async Task GetJobQueryRedactsSensitiveValueAcrossLifecycle(string state)
    {
        var definition = TestDomainFactory.Parameter("Token", sensitive: true);
        var fixture = HandlerFixture.WithDraftJob(definition);
        fixture.Jobs.Job!.SetParameterValue(
            definition.Name,
            "secret-marker",
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));
        fixture.Jobs.Job.AddTarget(
            new TargetName("server-01"),
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(2));

        if (state is "Submitted" or "Terminal")
        {
            fixture.Jobs.Job.Submit(
                fixture.Scripts.Script!,
                TestDomainFactory.User,
                TestDomainFactory.Time.AddMinutes(3));
        }

        if (state == "Terminal")
        {
            fixture.Jobs.Job.MarkValidated(
                TestDomainFactory.OtherUser,
                fixture.Jobs.Job.UpdatedUtc.AddMinutes(1));
            fixture.Jobs.Job.QueueDryRun(
                TestDomainFactory.OtherUser,
                fixture.Jobs.Job.UpdatedUtc.AddMinutes(1));
            fixture.Jobs.Job.StartDryRun(
                TestDomainFactory.OtherUser,
                fixture.Jobs.Job.UpdatedUtc.AddMinutes(1));
            fixture.Jobs.Job.CompleteDryRun(
                TestDomainFactory.OtherUser,
                fixture.Jobs.Job.UpdatedUtc.AddMinutes(1));
            fixture.Jobs.Job.CompleteRequestedDryRun(
                TestDomainFactory.OtherUser,
                fixture.Jobs.Job.UpdatedUtc.AddMinutes(1));
        }

        var response = await fixture.GetHandler.HandleAsync(
            new GetJobQuery(fixture.Jobs.Job.Id),
            CancellationToken.None);
        var parameter = Assert.Single(response.Parameters);

        Assert.Equal("[REDACTED]", parameter.DisplayValue);
        Assert.True(parameter.IsSensitive);
        Assert.True(parameter.IsRedacted);
        Assert.DoesNotContain("secret-marker", response.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ScriptParameterType.String, "hello", "hello")]
    [InlineData(ScriptParameterType.Integer, "42", "42")]
    [InlineData(ScriptParameterType.StringArray, "[\"one\",\"two\"]", "[\"one\",\"two\"]")]
    public async Task GetJobQueryReturnsTrustedNonSensitiveValues(
        ScriptParameterType parameterType,
        string serializedValue,
        string expectedDisplay)
    {
        var definition = TestDomainFactory.Parameter("Value", parameterType);
        var fixture = HandlerFixture.WithDraftJob(definition);
        fixture.Jobs.Job!.SetParameterValue(
            definition.Name,
            serializedValue,
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));

        var response = await fixture.GetHandler.HandleAsync(
            new GetJobQuery(fixture.Jobs.Job.Id),
            CancellationToken.None);
        var parameter = Assert.Single(response.Parameters);

        Assert.Equal(parameterType.ToString(), parameter.ParameterType);
        Assert.Equal(expectedDisplay, parameter.DisplayValue);
        Assert.False(parameter.IsSensitive);
        Assert.False(parameter.IsRedacted);
    }

    [Fact]
    public async Task GetJobQueryRedactsSecureReferenceEvenWhenSpoofedAsString()
    {
        var definition = TestDomainFactory.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            sensitive: true);
        var spoofed = TestDomainFactory.Parameter("Credential", ScriptParameterType.String);
        var fixture = HandlerFixture.WithDraftJob(definition);
        var credentialReferenceId = CredentialReferenceId.New().ToString();
        fixture.Jobs.Job!.SetParameterValue(
            spoofed.Name,
            credentialReferenceId,
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));

        var response = await fixture.GetHandler.HandleAsync(
            new GetJobQuery(fixture.Jobs.Job.Id),
            CancellationToken.None);
        var parameter = Assert.Single(response.Parameters);

        Assert.Equal("SecureReference", parameter.ParameterType);
        Assert.True(parameter.IsSensitive);
        Assert.True(parameter.IsRedacted);
        Assert.Equal("[REDACTED]", parameter.DisplayValue);
        Assert.DoesNotContain(credentialReferenceId, response.ToString(), StringComparison.Ordinal);
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

    [Fact]
    public async Task StartExecutionAttemptHandlerCreatesAuditsPersistsAndCommitsAttempt()
    {
        var fixture = HandlerFixture.WithClaimedJob();
        var workerNodeId = WorkerNodeId.New();
        fixture.Workers.WorkerNode = new WorkerNode(
            workerNodeId,
            "worker-01",
            TestDomainFactory.Time);
        using var source = new CancellationTokenSource();

        await fixture.StartExecutionAttemptHandler.HandleAsync(
            new StartExecutionAttemptCommand(
                fixture.Jobs.Job!.Id,
                workerNodeId,
                TestDomainFactory.OtherUser),
            source.Token);

        var execution = Assert.Single(fixture.Jobs.Job.Executions);
        var audit = Assert.Single(fixture.Audits.Events);
        Assert.Equal(JobStatus.Executing, fixture.Jobs.Job.Status);
        Assert.Equal(1, execution.AttemptNumber);
        Assert.Equal(workerNodeId, execution.WorkerNodeId);
        Assert.Equal(fixture.Clock.UtcNow, execution.StartedUtc);
        Assert.Null(execution.CompletedUtc);
        Assert.Equal("ExecutionAttemptStarted", audit.EventType);
        Assert.Equal("1", audit.Properties["AttemptNumber"]);
        Assert.Equal("True", audit.Properties["WorkerNodeIdPresent"]);
        Assert.Equal(1, fixture.Jobs.UpdateCount);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
        Assert.All(fixture.ObservedTokens, token => Assert.Equal(source.Token, token));
    }

    [Fact]
    public async Task StartExecutionAttemptHandlerRejectsMissingWorkerWithoutMutation()
    {
        var fixture = HandlerFixture.WithClaimedJob();

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => fixture.StartExecutionAttemptHandler.HandleAsync(
                new StartExecutionAttemptCommand(
                    fixture.Jobs.Job!.Id,
                    WorkerNodeId.New(),
                    TestDomainFactory.OtherUser),
                CancellationToken.None));

        Assert.Equal(JobStatus.Claimed, fixture.Jobs.Job!.Status);
        Assert.Empty(fixture.Jobs.Job.Executions);
        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task StartExecutionAttemptHandlerRejectsDisabledWorkerWithoutMutation()
    {
        var fixture = HandlerFixture.WithClaimedJob();
        var workerNodeId = WorkerNodeId.New();
        fixture.Workers.WorkerNode = new WorkerNode(
            workerNodeId,
            "worker-01",
            TestDomainFactory.Time,
            isEnabled: false);

        await Assert.ThrowsAsync<ApplicationValidationException>(
            () => fixture.StartExecutionAttemptHandler.HandleAsync(
                new StartExecutionAttemptCommand(
                    fixture.Jobs.Job!.Id,
                    workerNodeId,
                    TestDomainFactory.OtherUser),
                CancellationToken.None));

        Assert.Equal(JobStatus.Claimed, fixture.Jobs.Job!.Status);
        Assert.Empty(fixture.Jobs.Job.Executions);
        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task StartExecutionAttemptHandlerDoesNotPersistWhenJobIsNotClaimed()
    {
        var fixture = HandlerFixture.WithSubmittedJob();

        await Assert.ThrowsAsync<Domain.Exceptions.InvalidJobStateTransitionException>(
            () => fixture.StartExecutionAttemptHandler.HandleAsync(
                new StartExecutionAttemptCommand(
                    fixture.Jobs.Job!.Id,
                    null,
                    TestDomainFactory.OtherUser),
                CancellationToken.None));

        Assert.Equal(JobStatus.Submitted, fixture.Jobs.Job!.Status);
        Assert.Empty(fixture.Jobs.Job.Executions);
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

    [Fact]
    public async Task HandlerEntryPointsRejectNullRequestsBeforeRepositoryAccess()
    {
        var fixture = new HandlerFixture();
        Func<Task>[] calls =
        [
            () => fixture.CreateHandler.HandleAsync(null!, CancellationToken.None),
            () => fixture.AddTargetHandler.HandleAsync(null!, CancellationToken.None),
            () => fixture.SetParameterHandler.HandleAsync(null!, CancellationToken.None),
            () => fixture.SubmitHandler.HandleAsync(null!, CancellationToken.None),
            () => fixture.TransitionHandler.HandleAsync(null!, CancellationToken.None),
            () => fixture.ApproveHandler.HandleAsync(null!, CancellationToken.None),
            () => fixture.RejectHandler.HandleAsync(null!, CancellationToken.None),
            () => fixture.CompleteReadOnlyHandler.HandleAsync(null!, CancellationToken.None),
            () => fixture.CompleteValidationHandler.HandleAsync(null!, CancellationToken.None),
            () => fixture.CompleteDryRunHandler.HandleAsync(null!, CancellationToken.None),
            () => fixture.StartExecutionAttemptHandler.HandleAsync(null!, CancellationToken.None),
            () => fixture.RecordExecutionOutcomeHandler.HandleAsync(null!, CancellationToken.None),
            () => fixture.GetHandler.HandleAsync(null!, CancellationToken.None),
        ];

        foreach (var call in calls)
        {
            await Assert.ThrowsAsync<ArgumentNullException>(call);
        }

        Assert.Empty(fixture.ObservedTokens);
    }

    private static void ForcePublished(ScriptVersion version)
    {
        var field = typeof(ScriptVersion).GetField(
            $"<{nameof(ScriptVersion.IsPublished)}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        field.SetValue(version, true);
    }

    private sealed class HandlerFixture
    {
        public HandlerFixture()
        {
            CreateHandler = new CreateDraftJobHandler(Scripts, Jobs, Audits, UnitOfWork, Clock);
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
            StartExecutionAttemptHandler = new StartExecutionAttemptHandler(
                Jobs,
                Workers,
                Audits,
                UnitOfWork,
                Clock);
            RecordExecutionOutcomeHandler = new RecordExecutionOutcomeHandler(Jobs, Audits, UnitOfWork, Clock);
            GetHandler = new GetJobHandler(Jobs, Scripts);
        }

        public FakeJobRepository Jobs { get; } = new();
        public FakeScriptRepository Scripts { get; } = new();
        public FakeCredentialRepository Credentials { get; } = new();
        public FakeWorkerRepository Workers { get; } = new();
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
        public StartExecutionAttemptHandler StartExecutionAttemptHandler { get; }
        public RecordExecutionOutcomeHandler RecordExecutionOutcomeHandler { get; }
        public GetJobHandler GetHandler { get; }
        public IEnumerable<CancellationToken> ObservedTokens =>
            Jobs.ObservedTokens
                .Concat(Scripts.ObservedTokens)
                .Concat(Credentials.ObservedTokens)
                .Concat(Workers.ObservedTokens)
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

        public static HandlerFixture WithClaimedJob()
        {
            var fixture = WithAwaitingApprovalJob();
            fixture.Jobs.Job!.RecordApproval(
                TestDomainFactory.OtherUser,
                TestDomainFactory.Fingerprint,
                null,
                fixture.Jobs.Job.UpdatedUtc.AddMinutes(1));
            fixture.Jobs.Job.QueueExecution(
                TestDomainFactory.OtherUser,
                fixture.Jobs.Job.UpdatedUtc.AddMinutes(1));
            fixture.Jobs.Job.Claim(
                TestDomainFactory.OtherUser,
                fixture.Jobs.Job.UpdatedUtc.AddMinutes(1));
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
        public List<CancellationToken> ObservedTokens { get; } = [];

        public Task<ScriptDefinition?> GetByIdAsync(
            ScriptDefinitionId id,
            CancellationToken cancellationToken)
        {
            ObservedTokens.Add(cancellationToken);
            return Task.FromResult(Script?.Id == id ? Script : null);
        }

        public Task AddAsync(ScriptDefinition definition, CancellationToken cancellationToken)
        {
            ObservedTokens.Add(cancellationToken);
            Script = definition;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ScriptDefinition definition, CancellationToken cancellationToken)
        {
            ObservedTokens.Add(cancellationToken);
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

    private sealed class FakeWorkerRepository : IWorkerNodeRepository
    {
        public WorkerNode? WorkerNode { get; set; }
        public List<CancellationToken> ObservedTokens { get; } = [];

        public Task<WorkerNode?> GetByIdAsync(
            WorkerNodeId id,
            CancellationToken cancellationToken)
        {
            ObservedTokens.Add(cancellationToken);
            return Task.FromResult(WorkerNode?.Id == id ? WorkerNode : null);
        }

        public Task AddAsync(WorkerNode workerNode, CancellationToken cancellationToken)
        {
            ObservedTokens.Add(cancellationToken);
            WorkerNode = workerNode;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(WorkerNode workerNode, CancellationToken cancellationToken)
        {
            ObservedTokens.Add(cancellationToken);
            WorkerNode = workerNode;
            return Task.CompletedTask;
        }
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
