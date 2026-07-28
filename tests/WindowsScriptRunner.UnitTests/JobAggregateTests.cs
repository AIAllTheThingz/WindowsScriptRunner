using System.Reflection;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.ValueObjects;

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
            () => job.SetParameter(
                definition,
                "value",
                TestDomainFactory.User,
                job.UpdatedUtc.AddMinutes(1)));
    }

    [Fact]
    public void DraftParameterCanBeAddedUpdatedAndRedacted()
    {
        var definition = TestDomainFactory.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            required: true,
            sensitive: true);
        var version = TestDomainFactory.Version([definition]);
        var job = TestDomainFactory.DraftJob(TestDomainFactory.Script(version), version);

        job.SetParameter(
            definition,
            "credential-reference-1",
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(1));
        job.SetParameter(
            definition,
            "credential-reference-2",
            TestDomainFactory.User,
            TestDomainFactory.Time.AddMinutes(2));
        var parameter = Assert.Single(job.Parameters);

        Assert.Equal("[REDACTED]", parameter.GetSafeDisplayValue());
        Assert.DoesNotContain("credential-reference-2", parameter.ToString(), StringComparison.Ordinal);
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

    [Fact]
    public void SubmissionRequiresRequiredParameter()
    {
        var required = TestDomainFactory.Parameter("Mode", required: true);
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
    public void InvalidParameterValueIsRejected()
    {
        var count = TestDomainFactory.Parameter("Count", ScriptParameterType.Integer);
        var version = TestDomainFactory.Version([count]);
        var job = TestDomainFactory.DraftJob(TestDomainFactory.Script(version), version);

        Assert.Throws<InvalidJobParameterException>(
            () => job.SetParameter(
                count,
                "not-an-integer",
                TestDomainFactory.User,
                TestDomainFactory.Time.AddMinutes(1)));
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
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);
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
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);
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
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);
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
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);

        ApplyTerminalOperation(job, terminalStatus, job.UpdatedUtc.AddMinutes(1));

        Assert.Equal(terminalStatus, job.Status);
        Assert.Throws<InvalidJobStateTransitionException>(
            () => job.MarkValidated(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1)));
    }

    [Fact]
    public void RejectionRecordsDecisionAndTerminatesJob()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);
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
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);
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
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);
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
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);
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
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);
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
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);
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
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);
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
            () => execution.Complete(
                ExecutionOutcome.Succeeded,
                0,
                null,
                job.UpdatedUtc.AddMinutes(1)));
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
}
