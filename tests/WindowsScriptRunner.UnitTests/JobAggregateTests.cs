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
    public void SubmissionRequiresTarget()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.DraftJob(TestDomainFactory.Script(version), version);

        Assert.Throws<DomainValidationException>(
            () => job.Submit(version, TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1)));
    }

    [Fact]
    public void SubmissionRequiresRequiredParameter()
    {
        var required = TestDomainFactory.Parameter("Mode", required: true);
        var version = TestDomainFactory.Version([required]);
        var job = TestDomainFactory.DraftJob(TestDomainFactory.Script(version), version);
        job.AddTarget(new TargetName("server-01"), TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1));

        Assert.Throws<InvalidJobParameterException>(
            () => job.Submit(version, TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(2)));
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
    public void ValidJobSubmitsAndRecordsTimestamp()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);

        Assert.Equal(JobStatus.Submitted, job.Status);
        Assert.Equal(TestDomainFactory.Time.AddMinutes(3), job.SubmittedUtc);
    }

    [Fact]
    public void EveryNormalTransitionIsSupported()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);
        JobStatus[] statuses =
        [
            JobStatus.Validated,
            JobStatus.DryRunQueued,
            JobStatus.DryRunRunning,
            JobStatus.DryRunCompleted,
            JobStatus.AwaitingApproval,
        ];
        foreach (var status in statuses)
        {
            job.TransitionTo(status, TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        }

        job.RecordApproval(
            RiskLevel.Low,
            TestDomainFactory.OtherUser,
            TestDomainFactory.Fingerprint,
            null,
            job.UpdatedUtc.AddMinutes(1));
        JobStatus[] remaining =
        [
            JobStatus.ExecutionQueued,
            JobStatus.Claimed,
            JobStatus.Executing,
            JobStatus.PostValidation,
            JobStatus.Completed,
        ];
        foreach (var status in remaining)
        {
            job.TransitionTo(status, TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        }

        Assert.Equal(JobStatus.Completed, job.Status);
    }

    [Theory]
    [InlineData(JobStatus.Executing)]
    [InlineData(JobStatus.Approved)]
    [InlineData(JobStatus.Completed)]
    public void DraftCannotSkipToLaterState(JobStatus status)
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.DraftJob(TestDomainFactory.Script(version), version);

        Assert.Throws<InvalidJobStateTransitionException>(
            () => job.TransitionTo(status, TestDomainFactory.User, TestDomainFactory.Time.AddMinutes(1)));
    }

    [Fact]
    public void SubmittedCannotSkipToApproved()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);

        Assert.Throws<InvalidJobStateTransitionException>(
            () => job.TransitionTo(
                JobStatus.Approved,
                TestDomainFactory.OtherUser,
                job.UpdatedUtc.AddMinutes(1)));
    }

    [Fact]
    public void ValidatedCannotSkipToCompleted()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);
        job.TransitionTo(
            JobStatus.Validated,
            TestDomainFactory.OtherUser,
            job.UpdatedUtc.AddMinutes(1));

        Assert.Throws<InvalidJobStateTransitionException>(
            () => job.TransitionTo(
                JobStatus.Completed,
                TestDomainFactory.OtherUser,
                job.UpdatedUtc.AddMinutes(1)));
    }

    [Fact]
    public void CompletedJobCannotReturnToExecuting()
    {
        var version = TestDomainFactory.Version(
            phases: [ExecutionPhase.DryRun]);
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);
        job.TransitionTo(JobStatus.Validated, TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.TransitionTo(JobStatus.DryRunQueued, TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.TransitionTo(JobStatus.DryRunRunning, TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.TransitionTo(JobStatus.DryRunCompleted, TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.CompleteReadOnlyAfterDryRun(
            RiskLevel.ReadOnly,
            supportsExecutePhase: false,
            TestDomainFactory.OtherUser,
            job.UpdatedUtc.AddMinutes(1));

        Assert.Throws<InvalidJobStateTransitionException>(
            () => job.TransitionTo(
                JobStatus.Executing,
                TestDomainFactory.OtherUser,
                job.UpdatedUtc.AddMinutes(1)));
    }

    [Theory]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.TimedOut)]
    [InlineData(JobStatus.Blocked)]
    [InlineData(JobStatus.NotRun)]
    public void NonTerminalJobSupportsControlledTerminalState(JobStatus terminalStatus)
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);

        job.TransitionTo(terminalStatus, TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));

        Assert.True(JobStatusPolicy.IsTerminal(job.Status));
        Assert.Throws<InvalidJobStateTransitionException>(
            () => job.TransitionTo(
                JobStatus.Submitted,
                TestDomainFactory.OtherUser,
                job.UpdatedUtc.AddMinutes(1)));
    }

    [Fact]
    public void StateCannotTransitionToItself()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);

        Assert.Throws<InvalidJobStateTransitionException>(
            () => job.TransitionTo(
                JobStatus.Submitted,
                TestDomainFactory.OtherUser,
                job.UpdatedUtc.AddMinutes(1)));
    }

    [Fact]
    public void RejectionRecordsApprovalAndTerminatesJob()
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

    [Fact]
    public void ExecutionAttemptMustStartFromClaimedAndCompletesOnce()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);
        TestDomainFactory.AdvanceToAwaitingApproval(job);
        job.RecordApproval(
            RiskLevel.Low,
            TestDomainFactory.OtherUser,
            TestDomainFactory.Fingerprint,
            null,
            job.UpdatedUtc.AddMinutes(1));
        job.TransitionTo(
            JobStatus.ExecutionQueued,
            TestDomainFactory.OtherUser,
            job.UpdatedUtc.AddMinutes(1));
        job.TransitionTo(
            JobStatus.Claimed,
            TestDomainFactory.OtherUser,
            job.UpdatedUtc.AddMinutes(1));

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
}
