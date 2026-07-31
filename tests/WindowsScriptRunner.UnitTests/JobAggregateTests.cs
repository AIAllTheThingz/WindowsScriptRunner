using System.Reflection;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Credentials;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Domain.Workers;

namespace WindowsScriptRunner.UnitTests;

public sealed class JobAggregateTests
{
    [Fact]
    public void DraftTargetCanBeAddedAndRemoved()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.DraftJob(TestDomainFactory.Script(version), version);

        job.AddTarget(new TargetName("server-01"), TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1));
        Assert.Single(job.Targets);
        job.RemoveTarget(new TargetName("SERVER-01"), TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(2));

        Assert.Empty(job.Targets);
    }

    [Fact]
    public void DuplicateTargetsAreRejectedCaseInsensitively()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.DraftJob(TestDomainFactory.Script(version), version);
        job.AddTarget(new TargetName("server-01"), TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1));

        Assert.Throws<DuplicateJobTargetException>(
            () => job.AddTarget(
                new TargetName("SERVER-01"),
                TestDomainFactory.User,
                TestDomainFactory.Time.AddMinutes(2)));
    }

    [Fact]
    public void TargetAndParameterMutationAreRejectedAfterSubmission()
    {
        var definition = TestDomainFactory.Parameter();
        var version = TestDomainFactory.Version([definition]);
        var script = TestDomainFactory.Script(version);
        var job = TestDomainFactory.SubmittedJob(script, version);

        Assert.Throws<DomainValidationException>(
            () => job.AddTarget(
                new TargetName("server-02"),
                TestDomainFactory.User,
                job.UpdatedUtc.AddMinutes(1)));
        Assert.Throws<DomainValidationException>(
            () => job.SetParameterValue(
                definition.Name,
                "value",
                TestDomainFactory.User,
                job.UpdatedUtc.AddMinutes(1)));
    }

    [Fact]
    public void DraftParameterCanBeAddedAndUpdatedAsBindingOnly()
    {
        var definition = TestDomainFactory.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            required: true,
            sensitive: true);
        var version = TestDomainFactory.Version([definition]);
        var job = TestDomainFactory.DraftJob(TestDomainFactory.Script(version), version);

        job.SetParameterValue(
            definition.Name,
            CredentialReferenceId.New().ToString(),
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));
        var replacementId = CredentialReferenceId.New().ToString();
        job.SetParameterValue(
            definition.Name,
            replacementId,
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(2));
        var parameter = Assert.Single(job.Parameters);

        Assert.Equal(definition.Name, parameter.Name);
        Assert.Equal(replacementId, parameter.SerializedValue);
        Assert.DoesNotContain(replacementId, parameter.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void SetParameterValueTreatsAbsentInputAsAnIdempotentClear(string? absentValue)
    {
        var version = TestDomainFactory.Version([TestDomainFactory.Parameter("Mode")]);
        var job = TestDomainFactory.DraftJob(TestDomainFactory.Script(version), version);
        job.SetParameterValue(
            "Mode",
            "Safe",
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));
        var clearedUtc = TestDomainFactory.Time.AddMinutes(2);

        job.SetParameterValue(
            "mode",
            absentValue,
            TestDomainFactory.OtherUser,
            clearedUtc);

        Assert.Empty(job.Parameters);
        Assert.Equal(clearedUtc, job.UpdatedUtc);
        Assert.Equal(TestDomainFactory.OtherUser, job.LastActingUser);
    }

    [Fact]
    public void ClearParameterValueIsCaseInsensitiveAndIntentionallyTouchesIdempotentCommands()
    {
        var version = TestDomainFactory.Version([TestDomainFactory.Parameter("Mode")]);
        var job = TestDomainFactory.DraftJob(TestDomainFactory.Script(version), version);
        job.SetParameterValue(
            "Mode",
            "Safe",
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));

        var bindingExisted = job.ClearParameterValue(
            "mode",
            TestDomainFactory.OtherUser,
            TestDomainFactory.Time.AddMinutes(2));
        var absentBindingExisted = job.ClearParameterValue(
            "MODE",
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(3));

        Assert.True(bindingExisted);
        Assert.False(absentBindingExisted);
        Assert.Empty(job.Parameters);
        Assert.Equal(TestDomainFactory.Time.AddMinutes(3), job.UpdatedUtc);
        Assert.Equal(TestDomainFactory.User, job.LastActingUser);
    }

    [Fact]
    public void RemoveParameterNormalizesNameBeforeLookup()
    {
        var version = TestDomainFactory.Version([TestDomainFactory.Parameter("Mode")]);
        var job = TestDomainFactory.DraftJob(TestDomainFactory.Script(version), version);
        job.SetParameterValue(
            "Mode",
            "Safe",
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));
        var removedUtc = TestDomainFactory.Time.AddMinutes(2);

        job.RemoveParameter(" mode ", TestDomainFactory.OtherUser, removedUtc);

        Assert.Empty(job.Parameters);
        Assert.Equal(removedUtc, job.UpdatedUtc);
        Assert.Equal(TestDomainFactory.OtherUser, job.LastActingUser);
    }

    [Fact]
    public void FailedClearParameterValueLeavesBindingActorAndTimestampUnchanged()
    {
        var version = TestDomainFactory.Version([TestDomainFactory.Parameter("Mode")]);
        var job = TestDomainFactory.DraftJob(TestDomainFactory.Script(version), version);
        job.SetParameterValue(
            "Mode",
            "Safe",
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));
        var updatedUtc = job.UpdatedUtc;
        var lastActingUser = job.LastActingUser;
        var binding = Assert.Single(job.Parameters);

        Assert.Throws<InvalidJobParameterException>(
            () => job.ClearParameterValue(
                "invalid-name",
                TestDomainFactory.OtherUser,
                updatedUtc.AddMinutes(1)));
        Assert.Throws<DomainValidationException>(
            () => job.ClearParameterValue(
                "Mode",
                null!,
                updatedUtc.AddMinutes(1)));
        Assert.Throws<DomainValidationException>(
            () => job.ClearParameterValue(
                "Mode",
                TestDomainFactory.OtherUser,
                updatedUtc.AddTicks(-1)));

        Assert.Same(binding, Assert.Single(job.Parameters));
        Assert.Equal(updatedUtc, job.UpdatedUtc);
        Assert.Equal(lastActingUser, job.LastActingUser);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void JobParameterRejectsAbsentExplicitBindings(string? absentValue)
    {
        Assert.Throws<InvalidJobParameterException>(
            () => new JobParameter("Mode", absentValue));
    }

    [Fact]
    public void SubmissionRequiresTargetWithoutMutatingPolicyOrStatus()
    {
        var version = TestDomainFactory.Version();
        var script = TestDomainFactory.Script(version);
        var job = TestDomainFactory.DraftJob(script, version);

        Assert.Throws<DomainValidationException>(
            () => job.Submit(script, TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1)));

        Assert.Equal(JobStatus.Draft, job.Status);
        Assert.Null(job.PolicySnapshot);
        Assert.Null(job.SubmittedUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void SubmissionRequiresRequiredParameter(string? defaultValue)
    {
        var required = TestDomainFactory.Parameter(
            "Mode",
            required: true,
            defaultValue: defaultValue);
        var version = TestDomainFactory.Version([required]);
        var script = TestDomainFactory.Script(version);
        var job = TestDomainFactory.DraftJob(script, version);
        job.AddTarget(new TargetName("server-01"), TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1));

        Assert.Throws<InvalidJobParameterException>(
            () => job.Submit(script, TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(2)));
    }

    [Fact]
    public void SubmissionRejectsMismatchedScriptDefinition()
    {
        var version = TestDomainFactory.Version();
        var script = TestDomainFactory.Script(version);
        var job = TestDomainFactory.DraftJob(script, version);
        job.AddTarget(new TargetName("server-01"), TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1));

        var otherVersion = TestDomainFactory.Version();
        var otherScript = TestDomainFactory.Script(otherVersion, RiskLevel.Critical);
        Assert.Throws<DomainValidationException>(
            () => job.Submit(otherScript, TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(2)));

        Assert.Equal(JobStatus.Draft, job.Status);
        Assert.Null(job.PolicySnapshot);
    }

    [Fact]
    public void InvalidParameterValueIsRejectedAtSubmissionWithoutMutation()
    {
        var count = TestDomainFactory.Parameter("Count", ScriptParameterType.Integer);
        var version = TestDomainFactory.Version([count]);
        var script = TestDomainFactory.Script(version);
        var job = TestDomainFactory.DraftJob(script, version);
        job.AddTarget(
            new TargetName("server-01"),
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));
        job.SetParameterValue(
            count.Name,
            "not-an-integer",
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(2));
        var updated = job.UpdatedUtc;

        Assert.Throws<InvalidJobParameterException>(
            () => job.Submit(script, TestDomainFactory.User, updated.AddMinutes(1)));

        Assert.Equal(JobStatus.Draft, job.Status);
        Assert.Null(job.SubmittedUtc);
        Assert.Null(job.PolicySnapshot);
        Assert.Equal(updated, job.UpdatedUtc);
    }

    [Fact]
    public void SubmissionCapturesTrustedPolicySnapshot()
    {
        var version = TestDomainFactory.Version(phases: [ExecutionPhase.DryRun]);
        var script = TestDomainFactory.Script(version, RiskLevel.High);
        var job = TestDomainFactory.SubmittedJob(script, version);

        Assert.Equal(JobStatus.Submitted, job.Status);
        Assert.Equal(TestDomainFactory.Time.AddMinutes(3), job.SubmittedUtc);
        Assert.NotNull(job.PolicySnapshot);
        Assert.Equal(script.Id, job.PolicySnapshot.ScriptDefinitionId);
        Assert.Equal(version.Id, job.PolicySnapshot.ScriptVersionId);
        Assert.Equal(RiskLevel.High, job.PolicySnapshot.RiskLevel);
        Assert.False(job.PolicySnapshot.SupportsExecutePhase);
    }

    [Fact]
    public void ExplicitLifecycleOperationsSupportNormalFlow()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version),
            version,
            requestedPhase: ExecutionPhase.Execute);
        TestDomainFactory.AdvanceToAwaitingApproval(job);
        job.RecordApproval(
            TestDomainFactory.OtherUser,
            TestDomainFactory.Fingerprint,
            null,
            job.UpdatedUtc.AddMinutes(1));
        job.QueueExecution(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.Claim(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.StartExecutionAttempt(null, TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.BeginPostValidation(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.RecordTerminalExecutionOutcome(
            ExecutionOutcome.Succeeded,
            0,
            "Completed.",
            TestDomainFactory.OtherUser,
            job.UpdatedUtc.AddMinutes(1));

        Assert.Equal(JobStatus.Completed, job.Status);
    }

    [Fact]
    public void PublicApiHasNoGenericStatusTransitionBypass()
    {
        var publicMethods = typeof(Job).GetMethods(BindingFlags.Instance | BindingFlags.Public);

        Assert.DoesNotContain(publicMethods, method => method.Name == "TransitionTo");
        Assert.DoesNotContain(
            publicMethods,
            method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(JobStatus)));
    }

    [Fact]
    public void OutOfOrderExplicitOperationIsRejectedWithoutMutation()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version),
            version,
            requestedPhase: ExecutionPhase.Execute);
        var status = job.Status;
        var updated = job.UpdatedUtc;
        var actor = job.LastActingUser;

        Assert.Throws<InvalidJobStateTransitionException>(
            () => job.QueueExecution(TestDomainFactory.OtherUser, updated.AddMinutes(1)));

        Assert.Equal(status, job.Status);
        Assert.Equal(updated, job.UpdatedUtc);
        Assert.Equal(actor, job.LastActingUser);
    }

    [Fact]
    public void BackwardTimestampAndNullActorCannotPartiallyMutateTransition()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version),
            version,
            requestedPhase: ExecutionPhase.Execute);
        var status = job.Status;
        var updated = job.UpdatedUtc;
        var actor = job.LastActingUser;

        Assert.Throws<DomainValidationException>(
            () => job.MarkValidated(TestDomainFactory.OtherUser, updated.AddTicks(-1)));
        Assert.Throws<DomainValidationException>(
            () => job.MarkValidated(null!, updated.AddMinutes(1)));

        Assert.Equal(status, job.Status);
        Assert.Equal(updated, job.UpdatedUtc);
        Assert.Equal(actor, job.LastActingUser);
    }

    [Theory]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.TimedOut)]
    [InlineData(JobStatus.Blocked)]
    [InlineData(JobStatus.NotRun)]
    public void NonTerminalJobSupportsControlledTerminalOperation(JobStatus terminalStatus)
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version),
            version,
            requestedPhase: ExecutionPhase.Execute);

        ApplyTerminalOperation(job, terminalStatus, job.UpdatedUtc.AddMinutes(1));

        Assert.Equal(terminalStatus, job.Status);
        Assert.Throws<InvalidJobStateTransitionException>(
            () => job.MarkValidated(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1)));
    }

    [Fact]
    public void RejectionRecordsDecisionAndTerminatesJob()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version),
            version,
            requestedPhase: ExecutionPhase.Execute);
        TestDomainFactory.AdvanceToAwaitingApproval(job);

        job.RecordRejection(
            TestDomainFactory.OtherUser,
            TestDomainFactory.Fingerprint,
            "Rejected for test.",
            job.UpdatedUtc.AddMinutes(1));

        Assert.Equal(JobStatus.Rejected, job.Status);
        Assert.Equal(ApprovalDecision.Rejected, Assert.Single(job.Approvals).Decision);
    }

    [Theory]
    [InlineData(RiskLevel.Low, false)]
    [InlineData(RiskLevel.Medium, false)]
    [InlineData(RiskLevel.High, false)]
    [InlineData(RiskLevel.Critical, false)]
    [InlineData(RiskLevel.ReadOnly, true)]
    public void ReadOnlyCompletionRequiresTrustedReadOnlyPolicyWithoutExecute(
        RiskLevel riskLevel,
        bool supportsExecute)
    {
        var phases = supportsExecute
            ? new[] { ExecutionPhase.DryRun, ExecutionPhase.Execute }
            : new[] { ExecutionPhase.DryRun };
        var version = TestDomainFactory.Version(phases: phases);
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version, riskLevel), version);
        job.MarkValidated(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.QueueDryRun(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.StartDryRun(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.CompleteDryRun(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));

        Assert.Throws<InvalidJobStateTransitionException>(
            () => job.CompleteReadOnlyAfterDryRun(
                TestDomainFactory.OtherUser,
                job.UpdatedUtc.AddMinutes(1)));
        Assert.Equal(JobStatus.DryRunCompleted, job.Status);
    }

    [Fact]
    public void TrustedReadOnlyPolicyWithoutExecuteCompletesAfterDryRun()
    {
        var version = TestDomainFactory.Version(phases: [ExecutionPhase.DryRun]);
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version, RiskLevel.ReadOnly),
            version);
        job.MarkValidated(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.QueueDryRun(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.StartDryRun(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.CompleteDryRun(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));

        job.CompleteReadOnlyAfterDryRun(
            TestDomainFactory.OtherUser,
            job.UpdatedUtc.AddMinutes(1));

        Assert.Equal(JobStatus.Completed, job.Status);
    }

    [Fact]
    public void InvalidApprovalLeavesJobAndDecisionCollectionUnchanged()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version),
            version,
            requestedPhase: ExecutionPhase.Execute);
        TestDomainFactory.AdvanceToAwaitingApproval(job);
        var updated = job.UpdatedUtc;
        var actor = job.LastActingUser;

        Assert.Throws<DomainValidationException>(
            () => job.RecordApproval(
                TestDomainFactory.OtherUser,
                "invalid",
                null,
                updated.AddMinutes(1)));

        Assert.Equal(JobStatus.AwaitingApproval, job.Status);
        Assert.Empty(job.Approvals);
        Assert.Equal(updated, job.UpdatedUtc);
        Assert.Equal(actor, job.LastActingUser);
    }

    [Fact]
    public void InvalidApprovalTimestampLeavesJobAndDecisionCollectionUnchanged()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version),
            version,
            requestedPhase: ExecutionPhase.Execute);
        TestDomainFactory.AdvanceToAwaitingApproval(job);
        var updated = job.UpdatedUtc;
        var actor = job.LastActingUser;

        Assert.Throws<DomainValidationException>(
            () => job.RecordApproval(
                TestDomainFactory.OtherUser,
                TestDomainFactory.Fingerprint,
                null,
                updated.AddTicks(-1)));

        Assert.Equal(JobStatus.AwaitingApproval, job.Status);
        Assert.Empty(job.Approvals);
        Assert.Equal(updated, job.UpdatedUtc);
        Assert.Equal(actor, job.LastActingUser);
    }

    [Fact]
    public void InvalidRejectionTimestampLeavesJobAndDecisionCollectionUnchanged()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version),
            version,
            requestedPhase: ExecutionPhase.Execute);
        TestDomainFactory.AdvanceToAwaitingApproval(job);
        var updated = job.UpdatedUtc;
        var actor = job.LastActingUser;

        Assert.Throws<DomainValidationException>(
            () => job.RecordRejection(
                TestDomainFactory.OtherUser,
                TestDomainFactory.Fingerprint,
                null,
                updated.AddTicks(-1)));

        Assert.Equal(JobStatus.AwaitingApproval, job.Status);
        Assert.Empty(job.Approvals);
        Assert.Equal(updated, job.UpdatedUtc);
        Assert.Equal(actor, job.LastActingUser);
    }

    [Fact]
    public void InvalidExecutionStartLeavesJobAndAttemptCollectionUnchanged()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version),
            version,
            requestedPhase: ExecutionPhase.Execute);
        TestDomainFactory.AdvanceToAwaitingApproval(job);
        job.RecordApproval(
            TestDomainFactory.OtherUser,
            TestDomainFactory.Fingerprint,
            null,
            job.UpdatedUtc.AddMinutes(1));
        job.QueueExecution(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.Claim(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        var updated = job.UpdatedUtc;

        Assert.Throws<DomainValidationException>(
            () => job.StartExecutionAttempt(
                null,
                TestDomainFactory.OtherUser,
                updated.AddTicks(-1)));

        Assert.Equal(JobStatus.Claimed, job.Status);
        Assert.Empty(job.Executions);
        Assert.Equal(updated, job.UpdatedUtc);
    }

    [Fact]
    public void InvalidTerminalOutcomeLeavesJobAndAttemptUnchanged()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version),
            version,
            requestedPhase: ExecutionPhase.Execute);
        TestDomainFactory.AdvanceToAwaitingApproval(job);
        job.RecordApproval(
            TestDomainFactory.OtherUser,
            TestDomainFactory.Fingerprint,
            null,
            job.UpdatedUtc.AddMinutes(1));
        job.QueueExecution(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.Claim(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        var execution = job.StartExecutionAttempt(
            null,
            TestDomainFactory.OtherUser,
            job.UpdatedUtc.AddMinutes(1));
        var updated = job.UpdatedUtc;

        Assert.Throws<DomainValidationException>(
            () => job.RecordTerminalExecutionOutcome(
                ExecutionOutcome.Succeeded,
                null,
                null,
                TestDomainFactory.OtherUser,
                updated.AddMinutes(1)));

        Assert.Equal(JobStatus.Executing, job.Status);
        Assert.Null(execution.Outcome);
        Assert.Null(execution.CompletedUtc);
        Assert.Equal(updated, job.UpdatedUtc);
    }

    [Fact]
    public void ExecutionAttemptCompletesOnce()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version),
            version,
            requestedPhase: ExecutionPhase.Execute);
        TestDomainFactory.AdvanceToAwaitingApproval(job);
        job.RecordApproval(
            TestDomainFactory.OtherUser,
            TestDomainFactory.Fingerprint,
            null,
            job.UpdatedUtc.AddMinutes(1));
        job.QueueExecution(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.Claim(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));

        var execution = job.StartExecutionAttempt(
            null,
            TestDomainFactory.OtherUser,
            job.UpdatedUtc.AddMinutes(1));
        job.RecordTerminalExecutionOutcome(
            ExecutionOutcome.Succeeded,
            0,
            "Completed.",
            TestDomainFactory.OtherUser,
            job.UpdatedUtc.AddMinutes(1));

        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Equal(ExecutionOutcome.Succeeded, execution.Outcome);
        Assert.Throws<DomainValidationException>(
            () => job.RecordTerminalExecutionOutcome(
                ExecutionOutcome.Succeeded,
                0,
                null,
                TestDomainFactory.OtherUser,
                job.UpdatedUtc.AddMinutes(1)));
    }

    [Theory]
    [InlineData(ExecutionOutcome.Succeeded, JobStatus.Completed, 0)]
    [InlineData(ExecutionOutcome.SucceededWithWarnings, JobStatus.CompletedWithWarnings, 1)]
    [InlineData(ExecutionOutcome.Failed, JobStatus.Failed, 1)]
    [InlineData(ExecutionOutcome.Cancelled, JobStatus.Cancelled, null)]
    [InlineData(ExecutionOutcome.TimedOut, JobStatus.TimedOut, null)]
    [InlineData(ExecutionOutcome.Blocked, JobStatus.Blocked, null)]
    [InlineData(ExecutionOutcome.NotRun, JobStatus.NotRun, null)]
    public void TerminalExecutionOutcomeCompletesJobAndAttemptTogether(
        ExecutionOutcome outcome,
        JobStatus expectedStatus,
        int? exitCode)
    {
        var job = CreateExecutingJob();
        var execution = Assert.Single(job.Executions);
        var completedUtc = job.UpdatedUtc.AddMinutes(1);

        var returned = job.RecordTerminalExecutionOutcome(
            outcome,
            exitCode,
            "  Completed with summary.  ",
            TestDomainFactory.OtherUser,
            completedUtc);

        Assert.Same(execution, returned);
        Assert.Equal(expectedStatus, job.Status);
        Assert.Equal(completedUtc, execution.CompletedUtc);
        Assert.Equal(outcome, execution.Outcome);
        Assert.Equal(exitCode, execution.ExitCode);
        Assert.Equal("Completed with summary.", execution.Summary);
        Assert.False(job.HasActiveExecutionAttempt);
    }

    [Theory]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.TimedOut)]
    [InlineData(JobStatus.Blocked)]
    [InlineData(JobStatus.NotRun)]
    public void DirectTerminalOperationRejectsActiveExecutionAttempt(JobStatus terminalStatus)
    {
        var job = CreateExecutingJob();
        var execution = Assert.Single(job.Executions);
        var updated = job.UpdatedUtc;

        Assert.Throws<DomainValidationException>(
            () => ApplyTerminalOperation(job, terminalStatus, updated.AddMinutes(1)));

        Assert.Equal(JobStatus.Executing, job.Status);
        Assert.Equal(updated, job.UpdatedUtc);
        Assert.Null(execution.CompletedUtc);
        Assert.Null(execution.Outcome);
        Assert.True(job.HasActiveExecutionAttempt);
    }

    [Theory]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.TimedOut)]
    [InlineData(JobStatus.Blocked)]
    [InlineData(JobStatus.NotRun)]
    public void DirectTerminalOperationRejectsPostValidationWithActiveAttempt(JobStatus terminalStatus)
    {
        var job = CreateExecutingJob();
        job.BeginPostValidation(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        var execution = Assert.Single(job.Executions);
        var updated = job.UpdatedUtc;

        Assert.Throws<DomainValidationException>(
            () => ApplyTerminalOperation(job, terminalStatus, updated.AddMinutes(1)));

        Assert.Equal(JobStatus.PostValidation, job.Status);
        Assert.Equal(updated, job.UpdatedUtc);
        Assert.Null(execution.CompletedUtc);
        Assert.Null(execution.Outcome);
        Assert.True(job.HasActiveExecutionAttempt);
    }

    [Fact]
    public void SecondExecutionAttemptCannotStartWhileAttemptIsActive()
    {
        var job = CreateExecutingJob();
        var updated = job.UpdatedUtc;

        Assert.Throws<DomainValidationException>(
            () => job.StartExecutionAttempt(
                WorkerNodeId.New(),
                TestDomainFactory.OtherUser,
                updated.AddMinutes(1)));

        Assert.Single(job.Executions);
        Assert.Equal(JobStatus.Executing, job.Status);
        Assert.Equal(updated, job.UpdatedUtc);
    }

    [Fact]
    public void JobExecutionCannotBeCompletedThroughPublicApi()
    {
        var publicMethods = typeof(JobExecution).GetMethods(BindingFlags.Instance | BindingFlags.Public);

        Assert.DoesNotContain(publicMethods, method => method.Name == "Start");
        Assert.DoesNotContain(publicMethods, method => method.Name == "Complete");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public void UndefinedTerminalOutcomeLeavesJobAndAttemptUnchanged(int undefinedOutcome)
    {
        var job = CreateExecutingJob();
        var execution = Assert.Single(job.Executions);
        var updated = job.UpdatedUtc;

        Assert.Throws<DomainValidationException>(
            () => job.RecordTerminalExecutionOutcome(
                (ExecutionOutcome)undefinedOutcome,
                0,
                null,
                TestDomainFactory.OtherUser,
                updated.AddMinutes(1)));

        Assert.Equal(JobStatus.Executing, job.Status);
        Assert.Equal(updated, job.UpdatedUtc);
        Assert.Null(execution.CompletedUtc);
        Assert.Null(execution.Outcome);
        Assert.Null(execution.ExitCode);
        Assert.Null(execution.Summary);
    }

    [Fact]
    public void BackwardTerminalOutcomeTimestampLeavesJobAndAttemptUnchanged()
    {
        var job = CreateExecutingJob();
        var execution = Assert.Single(job.Executions);
        var updated = job.UpdatedUtc;

        Assert.Throws<DomainValidationException>(
            () => job.RecordTerminalExecutionOutcome(
                ExecutionOutcome.Failed,
                1,
                null,
                TestDomainFactory.OtherUser,
                updated.AddTicks(-1)));

        Assert.Equal(JobStatus.Executing, job.Status);
        Assert.Equal(updated, job.UpdatedUtc);
        Assert.Null(execution.CompletedUtc);
        Assert.Null(execution.Outcome);
    }

    [Fact]
    public void OversizedTerminalOutcomeSummaryLeavesJobAndAttemptUnchanged()
    {
        var job = CreateExecutingJob();
        var execution = Assert.Single(job.Executions);
        var updated = job.UpdatedUtc;

        Assert.Throws<DomainValidationException>(
            () => job.RecordTerminalExecutionOutcome(
                ExecutionOutcome.Failed,
                1,
                new string('s', 2001),
                TestDomainFactory.OtherUser,
                updated.AddMinutes(1)));

        Assert.Equal(JobStatus.Executing, job.Status);
        Assert.Equal(updated, job.UpdatedUtc);
        Assert.Null(execution.CompletedUtc);
        Assert.Null(execution.Outcome);
        Assert.Null(execution.Summary);
    }

    [Fact]
    public void NullActorTerminalOutcomeLeavesJobAndAttemptUnchanged()
    {
        var job = CreateExecutingJob();
        var execution = Assert.Single(job.Executions);
        var updated = job.UpdatedUtc;

        Assert.Throws<DomainValidationException>(
            () => job.RecordTerminalExecutionOutcome(
                ExecutionOutcome.Failed,
                1,
                null,
                null!,
                updated.AddMinutes(1)));

        Assert.Equal(JobStatus.Executing, job.Status);
        Assert.Equal(updated, job.UpdatedUtc);
        Assert.Null(execution.CompletedUtc);
        Assert.Null(execution.Outcome);
    }

    [Fact]
    public void MissingActiveExecutionAttemptIsRejected()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version),
            version,
            requestedPhase: ExecutionPhase.Execute);
        ForceStatus(job, JobStatus.Executing);
        var updated = job.UpdatedUtc;

        Assert.Throws<DomainValidationException>(
            () => job.RecordTerminalExecutionOutcome(
                ExecutionOutcome.Failed,
                1,
                null,
                TestDomainFactory.OtherUser,
                updated.AddMinutes(1)));

        Assert.Equal(JobStatus.Executing, job.Status);
        Assert.Equal(updated, job.UpdatedUtc);
        Assert.Empty(job.Executions);
    }

    [Fact]
    public void ValidationRequestCompletesAfterValidationAndCannotProceed()
    {
        var job = SubmittedForPhase(ExecutionPhase.Validation, [ExecutionPhase.Validation]);
        job.MarkValidated(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));

        Assert.Throws<DomainValidationException>(
            () => job.QueueDryRun(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1)));
        Assert.Throws<DomainValidationException>(
            () => job.RequireApproval(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1)));
        Assert.Throws<DomainValidationException>(
            () => job.QueueExecution(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1)));
        Assert.Throws<DomainValidationException>(
            () => job.StartExecutionAttempt(null, TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1)));

        job.CompleteRequestedValidation(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));

        Assert.Equal(JobStatus.Completed, job.Status);
    }

    [Fact]
    public void DryRunRequestCompletesAfterDryRunAndCannotExecute()
    {
        var job = SubmittedForPhase(ExecutionPhase.DryRun, [ExecutionPhase.DryRun, ExecutionPhase.Execute]);
        job.MarkValidated(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.QueueDryRun(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.StartDryRun(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.CompleteDryRun(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));

        Assert.Throws<DomainValidationException>(
            () => job.RequireApproval(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1)));
        Assert.Throws<DomainValidationException>(
            () => job.QueueExecution(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1)));
        Assert.Throws<DomainValidationException>(
            () => job.StartExecutionAttempt(null, TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1)));

        job.CompleteRequestedDryRun(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));

        Assert.Equal(JobStatus.Completed, job.Status);
    }

    [Fact]
    public void UnsupportedRequestedPhaseIsRejectedDuringSubmissionWithoutMutation()
    {
        var version = TestDomainFactory.Version(phases: [ExecutionPhase.Discovery]);
        var script = TestDomainFactory.Script(version);
        var job = TestDomainFactory.DraftJob(script, version, ExecutionPhase.Discovery);
        job.AddTarget(new TargetName("server-01"), TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1));
        var updated = job.UpdatedUtc;
        var actor = job.LastActingUser;

        Assert.Throws<DomainValidationException>(
            () => job.Submit(script, TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(2)));

        Assert.Equal(JobStatus.Draft, job.Status);
        Assert.Null(job.PolicySnapshot);
        Assert.Null(job.SubmittedUtc);
        Assert.Equal(updated, job.UpdatedUtc);
        Assert.Equal(actor, job.LastActingUser);
    }

    [Fact]
    public void DisabledScriptDefinitionCannotBeSubmittedWithoutMutation()
    {
        var version = TestDomainFactory.Version();
        var script = TestDomainFactory.Script(version);
        script.Disable(TestDomainFactory.Time.AddMinutes(1));
        var job = TestDomainFactory.DraftJob(script, version);
        job.AddTarget(new TargetName("server-01"), TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1));
        var updated = job.UpdatedUtc;
        var actor = job.LastActingUser;

        Assert.Throws<DomainValidationException>(
            () => job.Submit(script, TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(2)));

        Assert.Equal(JobStatus.Draft, job.Status);
        Assert.Null(job.PolicySnapshot);
        Assert.Null(job.SubmittedUtc);
        Assert.Equal(updated, job.UpdatedUtc);
        Assert.Equal(actor, job.LastActingUser);
    }

    [Fact]
    public void EnabledPublishedScriptDefinitionCanBeSubmitted()
    {
        var job = SubmittedForPhase(ExecutionPhase.DryRun, [ExecutionPhase.DryRun]);

        Assert.Equal(JobStatus.Submitted, job.Status);
        Assert.NotNull(job.PolicySnapshot);
    }

    [Fact]
    public void DisabledUnpublishedScriptDefinitionIsRejectedBeforePolicyCapture()
    {
        var version = TestDomainFactory.Version(publish: false);
        var script = TestDomainFactory.Script(version);
        script.Disable(TestDomainFactory.Time.AddMinutes(1));
        var job = TestDomainFactory.DraftJob(script, version);
        job.AddTarget(new TargetName("server-01"), TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1));

        Assert.Throws<DomainValidationException>(
            () => job.Submit(script, TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(2)));

        Assert.Equal(JobStatus.Draft, job.Status);
        Assert.Null(job.PolicySnapshot);
    }

    [Theory]
    [InlineData(RiskLevel.Medium)]
    [InlineData(RiskLevel.High)]
    [InlineData(RiskLevel.Critical)]
    public void ElevatedRiskSelfApprovalFailsForTheStableRequesterIdentity(RiskLevel riskLevel)
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version, riskLevel),
            version,
            requestedPhase: ExecutionPhase.Execute);
        TestDomainFactory.AdvanceToAwaitingApproval(job);

        Assert.Throws<DomainValidationException>(
            () => job.RecordApproval(
                new UserIdentity("sid:S-1-5-21-1001-1002-1003-1004"),
                TestDomainFactory.Fingerprint,
                null,
                job.UpdatedUtc.AddMinutes(1)));

        Assert.Equal(JobStatus.AwaitingApproval, job.Status);
        Assert.Empty(job.Approvals);
    }

    [Fact]
    public void ExecuteApprovalRequiresAStableSidRequester()
    {
        var version = TestDomainFactory.Version();
        var script = TestDomainFactory.Script(version, RiskLevel.Medium);
        var requester = new UserIdentity("DOMAIN\\legacy-requester");
        var job = Job.CreateDraft(
            JobId.New(),
            script.Id,
            version.Id,
            ExecutionPhase.Execute,
            requester,
            TestDomainFactory.Time);
        job.AddTarget(new TargetName("server-01"), requester, TestDomainFactory.Time.AddMinutes(1));
        job.Submit(script, requester, TestDomainFactory.Time.AddMinutes(2));
        job.MarkValidated(TestDomainFactory.OtherUser, TestDomainFactory.Time.AddMinutes(3));
        job.QueueDryRun(TestDomainFactory.OtherUser, TestDomainFactory.Time.AddMinutes(4));
        job.StartDryRun(TestDomainFactory.OtherUser, TestDomainFactory.Time.AddMinutes(5));
        job.CompleteDryRun(TestDomainFactory.OtherUser, TestDomainFactory.Time.AddMinutes(6));

        Assert.Throws<DomainValidationException>(
            () => job.RequireApproval(TestDomainFactory.OtherUser, TestDomainFactory.Time.AddMinutes(7)));

        Assert.Equal(JobStatus.DryRunCompleted, job.Status);
    }

    [Fact]
    public void LegacyExecuteStateCannotQueueLeaseOrStartWithoutAcceptedDryRunEvidence()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version, RiskLevel.Medium),
            version,
            requestedPhase: ExecutionPhase.Execute);
        var updated = job.UpdatedUtc;

        ForceStatus(job, JobStatus.Approved);
        Assert.Throws<DomainValidationException>(
            () => job.QueueExecution(TestDomainFactory.OtherUser, updated.AddMinutes(1)));

        ForceStatus(job, JobStatus.ExecutionQueued);
        Assert.Throws<DomainValidationException>(
            () => job.AcquireWorkLease(
                JobLeaseId.New(),
                WorkerNodeId.New(),
                JobWorkKind.Execute,
                1,
                TestDomainFactory.OtherUser,
                updated.AddMinutes(1),
                updated.AddMinutes(2)));

        ForceStatus(job, JobStatus.Claimed);
        Assert.Throws<DomainValidationException>(
            () => job.StartExecutionAttempt(
                WorkerNodeId.New(),
                TestDomainFactory.OtherUser,
                updated.AddMinutes(1)));
    }

    [Fact]
    public void AggregateBoundariesRejectNullIdentifiers()
    {
        var version = TestDomainFactory.Version();
        var script = TestDomainFactory.Script(version);

        Assert.Throws<DomainValidationException>(
            () => Job.CreateDraft(null!, script.Id, version.Id, ExecutionPhase.DryRun, TestDomainFactory.User, TestDomainFactory.Time));
        Assert.Throws<DomainValidationException>(
            () => Job.CreateDraft(JobId.New(), null!, version.Id, ExecutionPhase.DryRun, TestDomainFactory.User, TestDomainFactory.Time));
        Assert.Throws<DomainValidationException>(
            () => Job.CreateDraft(JobId.New(), script.Id, null!, ExecutionPhase.DryRun, TestDomainFactory.User, TestDomainFactory.Time));
        Assert.Throws<DomainValidationException>(
            () => ScriptDefinition.Create(null!, new ScriptName("valid.name"), "Valid", string.Empty, RiskLevel.Low, TestDomainFactory.User, TestDomainFactory.Time));
        Assert.Throws<DomainValidationException>(
            () => new ScriptVersion(null!, ScriptVersionNumber.Parse("1.0.0"), "scripts/Test.ps1", new string('a', 64), null, "7.4", 30, [ExecutionPhase.DryRun], [], TestDomainFactory.Time, TestDomainFactory.User));
        Assert.Throws<DomainValidationException>(
            () => new ScriptParameterDefinition(null!, "Mode", "Mode", null, ScriptParameterType.String, false, null, [], false));
        Assert.Throws<DomainValidationException>(
            () => new CredentialReference(null!, "Vault", "path", "Name", TestDomainFactory.Time, TestDomainFactory.User));
        Assert.Throws<DomainValidationException>(
            () => new WorkerNode(null!, "worker-01", TestDomainFactory.Time));
        Assert.Throws<DomainValidationException>(
            () => new AuditEvent(null!, "Event", "Entity", "id", TestDomainFactory.User, TestDomainFactory.Time, "Summary"));
        Assert.Throws<DomainValidationException>(
            () => new JobExecution(null!, 1, null, TestDomainFactory.Time));
        Assert.Throws<DomainValidationException>(
            () => new JobApproval(null!, ApprovalDecision.Approved, TestDomainFactory.OtherUser, TestDomainFactory.Time, null, TestDomainFactory.Fingerprint));
    }

    [Fact]
    public void AuditPropertiesRejectCaseInsensitiveDuplicateKeysWithDomainException()
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["JobId"] = "first",
            ["jobid"] = "second",
        };

        Assert.Throws<DomainValidationException>(
            () => new AuditEvent(
                AuditEventId.New(),
                "Event",
                "Entity",
                "id",
                TestDomainFactory.User,
                TestDomainFactory.Time,
                "Summary",
                properties));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public void UndefinedRequestedPhaseIsRejectedAtJobCreation(int phase)
    {
        var version = TestDomainFactory.Version();
        var script = TestDomainFactory.Script(version);

        Assert.Throws<DomainValidationException>(
            () => Job.CreateDraft(
                JobId.New(),
                script.Id,
                version.Id,
                (ExecutionPhase)phase,
                TestDomainFactory.User,
                TestDomainFactory.Time));
    }

    [Fact]
    public void JobParameterCannotBeConstructedWithSecurityMetadata()
    {
        var publicConstructors = typeof(JobParameter).GetConstructors();

        Assert.DoesNotContain(
            publicConstructors,
            constructor => constructor.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(ScriptParameterType) ||
                parameter.ParameterType == typeof(bool)));
    }

    [Fact]
    public void JobParameterStoresOnlyBindingData()
    {
        var parameter = new JobParameter("Mode", "  Safe  ");
        var publicProperties = typeof(JobParameter).GetProperties(BindingFlags.Instance | BindingFlags.Public);

        Assert.Equal("Mode", parameter.Name);
        Assert.Equal("  Safe  ", parameter.SerializedValue);
        Assert.DoesNotContain(
            publicProperties,
            property => property.Name is "ParameterType" or "IsSensitive");
        Assert.DoesNotContain("Safe", parameter.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidParameterNameIsSanitizedAndBoundedInException()
    {
        var invalidName = new string('x', 200) + "\r\nsecret";

        var exception = Assert.Throws<InvalidJobParameterException>(
            () => new JobParameter(invalidName, "value"));

        Assert.DoesNotContain('\r', exception.Message);
        Assert.DoesNotContain('\n', exception.Message);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
        Assert.True(exception.Message.Length < invalidName.Length);
    }

    [Fact]
    public void ParameterNamesRemainCaseInsensitivelyUnique()
    {
        var version = TestDomainFactory.Version([
            TestDomainFactory.Parameter("Mode"),
        ]);
        var job = TestDomainFactory.DraftJob(TestDomainFactory.Script(version), version);

        job.SetParameterValue("Mode", "Safe", TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1));
        job.SetParameterValue("mode", "Fast", TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(2));

        var parameter = Assert.Single(job.Parameters);
        Assert.Equal("mode", parameter.Name);
        Assert.Equal("Fast", parameter.SerializedValue);
    }

    [Fact]
    public void SubmissionRejectsUnknownParameterWithoutMutation()
    {
        var version = TestDomainFactory.Version([
            TestDomainFactory.Parameter("Mode"),
        ]);
        var script = TestDomainFactory.Script(version);
        var job = TestDomainFactory.DraftJob(script, version);
        job.AddTarget(new TargetName("server-01"), TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1));
        job.SetParameterValue("Unknown", "secret-marker", TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(2));
        var updated = job.UpdatedUtc;
        var parameters = job.Parameters.ToArray();
        var targets = job.Targets.ToArray();
        var actor = job.LastActingUser;

        var exception = Assert.Throws<InvalidJobParameterException>(
            () => job.Submit(script, TestDomainFactory.User, updated.AddMinutes(1)));

        Assert.DoesNotContain("secret-marker", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(JobStatus.Draft, job.Status);
        Assert.Null(job.SubmittedUtc);
        Assert.Null(job.PolicySnapshot);
        Assert.Equal(updated, job.UpdatedUtc);
        Assert.Equal(actor, job.LastActingUser);
        Assert.Equal(parameters, job.Parameters);
        Assert.Equal(targets, job.Targets);
    }

    [Fact]
    public void SubmissionUsesPinnedDefinitionForValueValidation()
    {
        var pinned = TestDomainFactory.Parameter("Count", ScriptParameterType.Integer);
        var spoofed = TestDomainFactory.Parameter("Count", ScriptParameterType.String);
        var version = TestDomainFactory.Version([pinned]);
        var script = TestDomainFactory.Script(version);
        var job = TestDomainFactory.DraftJob(script, version);
        job.AddTarget(new TargetName("server-01"), TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1));
        job.SetParameterValue(spoofed.Name, "not-an-integer", TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(2));
        var updated = job.UpdatedUtc;

        Assert.Throws<InvalidJobParameterException>(
            () => job.Submit(script, TestDomainFactory.User, updated.AddMinutes(1)));

        Assert.Equal(JobStatus.Draft, job.Status);
        Assert.Null(job.SubmittedUtc);
        Assert.Null(job.PolicySnapshot);
        Assert.Equal(updated, job.UpdatedUtc);
    }

    [Fact]
    public void SubmissionCannotTrustIndependentlySuppliedSensitivityMetadata()
    {
        var pinned = TestDomainFactory.Parameter("Token", sensitive: true);
        var spoofed = TestDomainFactory.Parameter("Token", sensitive: false);
        var version = TestDomainFactory.Version([pinned]);
        var script = TestDomainFactory.Script(version);
        var job = TestDomainFactory.DraftJob(script, version);
        job.AddTarget(new TargetName("server-01"), TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1));
        job.SetParameterValue(spoofed.Name, "secret-marker", TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(2));

        job.Submit(script, TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(3));

        Assert.Equal(JobStatus.Submitted, job.Status);
        Assert.Single(job.Parameters);
        Assert.DoesNotContain("secret-marker", Assert.Single(job.Parameters).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SubmissionRejectsDuplicateReconstructedBindingsWithoutMutation()
    {
        var definition = TestDomainFactory.Parameter("Mode");
        var version = TestDomainFactory.Version([definition]);
        var script = TestDomainFactory.Script(version);
        var job = TestDomainFactory.DraftJob(script, version);
        job.AddTarget(new TargetName("server-01"), TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1));
        AddReconstructedParameter(job, new JobParameter("Mode", "Safe"));
        AddReconstructedParameter(job, new JobParameter("mode", "Fast"));
        var updated = job.UpdatedUtc;

        Assert.Throws<InvalidJobParameterException>(
            () => job.Submit(script, TestDomainFactory.User, updated.AddMinutes(1)));

        Assert.Equal(JobStatus.Draft, job.Status);
        Assert.Null(job.SubmittedUtc);
        Assert.Null(job.PolicySnapshot);
        Assert.Equal(updated, job.UpdatedUtc);
    }

    [Fact]
    public void ExecuteSubmissionWithoutDryRunSupportFailsWithoutMutation()
    {
        var version = TestDomainFactory.Version(
            publish: false,
            phases: [ExecutionPhase.Execute]);
        ForcePublished(version);
        var script = TestDomainFactory.Script(version);
        var job = TestDomainFactory.DraftJob(script, version, ExecutionPhase.Execute);
        job.AddTarget(new TargetName("server-01"), TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1));
        var updated = job.UpdatedUtc;
        var actor = job.LastActingUser;
        var targets = job.Targets.ToArray();

        Assert.Throws<DomainValidationException>(
            () => job.Submit(script, TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(2)));

        Assert.Equal(JobStatus.Draft, job.Status);
        Assert.Null(job.PolicySnapshot);
        Assert.Null(job.SubmittedUtc);
        Assert.Equal(updated, job.UpdatedUtc);
        Assert.Equal(actor, job.LastActingUser);
        Assert.Equal(targets, job.Targets);
        Assert.Empty(job.Parameters);
    }

    private static void ApplyTerminalOperation(
        Job job,
        JobStatus terminalStatus,
        DateTimeOffset updatedUtc)
    {
        switch (terminalStatus)
        {
            case JobStatus.Failed:
                job.Fail(TestDomainFactory.OtherUser, updatedUtc);
                break;
            case JobStatus.Cancelled:
                job.Cancel(TestDomainFactory.OtherUser, updatedUtc);
                break;
            case JobStatus.TimedOut:
                job.MarkTimedOut(TestDomainFactory.OtherUser, updatedUtc);
                break;
            case JobStatus.Blocked:
                job.Block(TestDomainFactory.OtherUser, updatedUtc);
                break;
            case JobStatus.NotRun:
                job.MarkNotRun(TestDomainFactory.OtherUser, updatedUtc);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(terminalStatus));
        }
    }

    private static Job SubmittedForPhase(
        ExecutionPhase requestedPhase,
        IEnumerable<ExecutionPhase> supportedPhases)
    {
        var version = TestDomainFactory.Version(phases: supportedPhases);
        return TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version),
            version,
            requestedPhase: requestedPhase);
    }

    private static Job CreateExecutingJob()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version),
            version,
            requestedPhase: ExecutionPhase.Execute);
        _ = TestDomainFactory.StartExecution(job);
        return job;
    }

    private static void ForceStatus(Job job, JobStatus status)
    {
        var field = typeof(Job).GetField(
            $"<{nameof(Job.Status)}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(job, status);
    }

    private static void ForcePublished(ScriptVersion version)
    {
        var field = typeof(ScriptVersion).GetField(
            $"<{nameof(ScriptVersion.IsPublished)}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(version, true);
    }

    private static void AddReconstructedParameter(Job job, JobParameter parameter)
    {
        var field = typeof(Job).GetField(
            "_parameters",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var parameters = Assert.IsType<List<JobParameter>>(field?.GetValue(job));
        parameters.Add(parameter);
    }
}
