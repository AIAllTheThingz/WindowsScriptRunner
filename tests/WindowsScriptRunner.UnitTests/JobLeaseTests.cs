using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Workers;

namespace WindowsScriptRunner.UnitTests;

public sealed class JobLeaseTests
{
    private static readonly WorkerNodeId WorkerId = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly WorkerNodeId OtherWorkerId = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    [Fact]
    public void JobLeaseIdentifierAndConstructorEnforceInvariants()
    {
        Assert.Throws<DomainValidationException>(() => new JobLeaseId(Guid.Empty));
        Assert.Throws<DomainValidationException>(
            () => new JobLease(
                JobLeaseId.New(),
                WorkerId,
                (JobWorkKind)99,
                1,
                TestDomainFactory.Time,
                TestDomainFactory.Time,
                TestDomainFactory.Time.AddMinutes(1)));
        Assert.Throws<DomainValidationException>(
            () => new JobLease(
                JobLeaseId.New(),
                WorkerId,
                JobWorkKind.Execute,
                0,
                TestDomainFactory.Time,
                TestDomainFactory.Time,
                TestDomainFactory.Time.AddMinutes(1)));
        Assert.Throws<DomainValidationException>(
            () => new JobLease(
                JobLeaseId.New(),
                WorkerId,
                JobWorkKind.Execute,
                1,
                TestDomainFactory.Time,
                TestDomainFactory.Time.AddMinutes(1),
                TestDomainFactory.Time.AddMinutes(1)));
    }

    [Fact]
    public void DryRunAcquisitionRetainsQueuedStatusAndExecuteAcquisitionClaims()
    {
        var dryRun = CreateDryRunQueuedJob();
        var execute = CreateExecutionQueuedJob();
        var acquired = dryRun.UpdatedUtc.AddMinutes(1);

        var dryRunLease = dryRun.AcquireWorkLease(
            JobLeaseId.New(),
            WorkerId,
            JobWorkKind.DryRun,
            10,
            TestDomainFactory.OtherUser,
            acquired,
            acquired.AddMinutes(2));
        var executeLease = execute.AcquireWorkLease(
            JobLeaseId.New(),
            WorkerId,
            JobWorkKind.Execute,
            11,
            TestDomainFactory.OtherUser,
            execute.UpdatedUtc.AddMinutes(1),
            execute.UpdatedUtc.AddMinutes(3));

        Assert.Equal(JobStatus.DryRunQueued, dryRun.Status);
        Assert.Same(dryRunLease, dryRun.Lease);
        Assert.Equal(JobStatus.Claimed, execute.Status);
        Assert.Same(executeLease, execute.Lease);
        Assert.Equal(11, executeLease.FencingToken);
    }

    [Theory]
    [InlineData(JobWorkKind.DryRun)]
    [InlineData(JobWorkKind.Execute)]
    public void AcquisitionRejectsIneligibleOrAlreadyLeasedJobWithoutMutation(JobWorkKind workKind)
    {
        var job = CreateDryRunQueuedJob();
        var acquired = job.UpdatedUtc.AddMinutes(1);
        var first = job.AcquireWorkLease(
            JobLeaseId.New(),
            WorkerId,
            JobWorkKind.DryRun,
            1,
            TestDomainFactory.OtherUser,
            acquired,
            acquired.AddMinutes(2));
        var status = job.Status;
        var updated = job.UpdatedUtc;

        Assert.Throws<DomainValidationException>(
            () => job.AcquireWorkLease(
                JobLeaseId.New(),
                OtherWorkerId,
                workKind,
                2,
                TestDomainFactory.User,
                acquired.AddSeconds(1),
                acquired.AddMinutes(3)));

        Assert.Same(first, job.Lease);
        Assert.Equal(status, job.Status);
        Assert.Equal(updated, job.UpdatedUtc);
    }

    [Fact]
    public void RenewalExtendsOnlyCurrentUnexpiredLease()
    {
        var job = CreateExecutionQueuedJob();
        var credentials = Acquire(job, JobWorkKind.Execute, 100);
        var initialStatus = job.Status;
        var initialUpdated = job.UpdatedUtc;
        var initialExpiration = job.Lease!.ExpiresUtc;
        var renewed = initialExpiration.AddMinutes(-1);

        job.RenewWorkLease(credentials, renewed, initialExpiration.AddMinutes(2));

        Assert.Equal(initialExpiration.AddMinutes(2), job.Lease.ExpiresUtc);
        Assert.Equal(renewed, job.Lease.LastRenewedUtc);
        Assert.Equal(100, job.Lease.FencingToken);
        Assert.Equal(initialStatus, job.Status);
        Assert.Equal(initialUpdated, job.UpdatedUtc);

        var expiration = job.Lease.ExpiresUtc;
        Assert.Throws<DomainValidationException>(
            () => job.RenewWorkLease(
                new JobLeaseCredentials(credentials.LeaseId, OtherWorkerId, 100),
                renewed.AddSeconds(1),
                expiration.AddMinutes(1)));
        Assert.Throws<DomainValidationException>(
            () => job.RenewWorkLease(
                new JobLeaseCredentials(credentials.LeaseId, WorkerId, 99),
                renewed.AddSeconds(1),
                expiration.AddMinutes(1)));
        Assert.Throws<DomainValidationException>(
            () => job.RenewWorkLease(
                credentials,
                expiration,
                expiration.AddMinutes(1)));
        Assert.Equal(expiration, job.Lease.ExpiresUtc);
    }

    [Fact]
    public void UnstartedReleaseKeepsDryRunQueuedAndRequeuesExecute()
    {
        var dryRun = CreateDryRunQueuedJob();
        var execute = CreateExecutionQueuedJob();
        var dryCredentials = Acquire(dryRun, JobWorkKind.DryRun, 1);
        var executeCredentials = Acquire(execute, JobWorkKind.Execute, 2);

        dryRun.ReleaseUnstartedWorkLease(
            dryCredentials,
            TestDomainFactory.OtherUser,
            dryRun.UpdatedUtc.AddSeconds(1));
        execute.ReleaseUnstartedWorkLease(
            executeCredentials,
            TestDomainFactory.OtherUser,
            execute.UpdatedUtc.AddSeconds(1));

        Assert.Equal(JobStatus.DryRunQueued, dryRun.Status);
        Assert.Null(dryRun.Lease);
        Assert.Equal(JobStatus.ExecutionQueued, execute.Status);
        Assert.Null(execute.Lease);
    }

    [Fact]
    public void StartedWorkCannotUseUnstartedRelease()
    {
        var dryRun = CreateDryRunQueuedJob();
        var dryCredentials = Acquire(dryRun, JobWorkKind.DryRun, 1);
        dryRun.StartDryRun(
            dryCredentials,
            TestDomainFactory.OtherUser,
            dryRun.UpdatedUtc.AddSeconds(1));

        Assert.Throws<DomainValidationException>(
            () => dryRun.ReleaseUnstartedWorkLease(
                dryCredentials,
                TestDomainFactory.OtherUser,
                dryRun.UpdatedUtc.AddSeconds(1)));
        Assert.Equal(JobStatus.DryRunRunning, dryRun.Status);
        Assert.NotNull(dryRun.Lease);

        var execute = CreateExecutionQueuedJob();
        var executeCredentials = Acquire(execute, JobWorkKind.Execute, 2);
        _ = execute.StartLeasedExecutionAttempt(
            executeCredentials,
            TestDomainFactory.OtherUser,
            execute.UpdatedUtc.AddSeconds(1));
        Assert.Throws<DomainValidationException>(
            () => execute.ReleaseUnstartedWorkLease(
                executeCredentials,
                TestDomainFactory.OtherUser,
                execute.UpdatedUtc.AddSeconds(1)));
        Assert.Equal(JobStatus.Executing, execute.Status);
        Assert.True(execute.HasActiveExecutionAttempt);
    }

    [Fact]
    public void WorkerControlledTransitionsRequireCurrentFencingCredentials()
    {
        var job = CreateExecutionQueuedJob();
        var current = Acquire(job, JobWorkKind.Execute, 101);
        var stale = new JobLeaseCredentials(current.LeaseId, WorkerId, 100);

        Assert.Throws<DomainValidationException>(
            () => job.StartLeasedExecutionAttempt(
                stale,
                TestDomainFactory.OtherUser,
                job.UpdatedUtc.AddSeconds(1)));
        Assert.Equal(JobStatus.Claimed, job.Status);
        Assert.Empty(job.Executions);

        var execution = job.StartLeasedExecutionAttempt(
            current,
            TestDomainFactory.OtherUser,
            job.UpdatedUtc.AddSeconds(1));
        Assert.Equal(WorkerId, execution.WorkerNodeId);
        Assert.Throws<DomainValidationException>(
            () => job.RecordTerminalExecutionOutcome(
                stale,
                ExecutionOutcome.Succeeded,
                0,
                null,
                TestDomainFactory.OtherUser,
                job.UpdatedUtc.AddSeconds(1)));
        Assert.True(execution.IsActive);
        Assert.Equal(JobStatus.Executing, job.Status);
    }

    [Fact]
    public void DryRunCompletionAndExecutionOutcomeResolveLease()
    {
        var dryRun = CreateDryRunQueuedJob();
        var dryCredentials = Acquire(dryRun, JobWorkKind.DryRun, 1);
        dryRun.StartDryRun(
            dryCredentials,
            TestDomainFactory.OtherUser,
            dryRun.UpdatedUtc.AddSeconds(1));
        dryRun.CompleteDryRun(
            dryCredentials,
            TestDomainFactory.OtherUser,
            dryRun.UpdatedUtc.AddSeconds(1));

        Assert.Equal(JobStatus.DryRunCompleted, dryRun.Status);
        Assert.Null(dryRun.Lease);

        var execute = CreateExecutionQueuedJob();
        var executeCredentials = Acquire(execute, JobWorkKind.Execute, 2);
        _ = execute.StartLeasedExecutionAttempt(
            executeCredentials,
            TestDomainFactory.OtherUser,
            execute.UpdatedUtc.AddSeconds(1));
        _ = execute.RecordTerminalExecutionOutcome(
            executeCredentials,
            ExecutionOutcome.Succeeded,
            0,
            "done",
            TestDomainFactory.OtherUser,
            execute.UpdatedUtc.AddSeconds(1));

        Assert.Equal(JobStatus.Completed, execute.Status);
        Assert.Null(execute.Lease);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpiredQueuedLeasesAreReleasedOrRequeued(bool execute)
    {
        var job = execute ? CreateExecutionQueuedJob() : CreateDryRunQueuedJob();
        var kind = execute ? JobWorkKind.Execute : JobWorkKind.DryRun;
        var credentials = Acquire(job, kind, 1);
        var disposition = job.RecoverExpiredWorkLease(
            credentials,
            TestDomainFactory.OtherUser,
            job.Lease!.ExpiresUtc);

        Assert.Null(job.Lease);
        Assert.Equal(
            execute ? JobStatus.ExecutionQueued : JobStatus.DryRunQueued,
            job.Status);
        Assert.Equal(
            execute
                ? JobLeaseRecoveryDisposition.RequeuedUnstartedExecution
                : JobLeaseRecoveryDisposition.ReleasedQueuedDryRun,
            disposition);
    }

    [Fact]
    public void ExpiredRunningDryRunTimesOutWithoutExecution()
    {
        var job = CreateDryRunQueuedJob();
        var credentials = Acquire(job, JobWorkKind.DryRun, 1);
        job.StartDryRun(
            credentials,
            TestDomainFactory.OtherUser,
            job.UpdatedUtc.AddSeconds(1));

        var disposition = job.RecoverExpiredWorkLease(
            credentials,
            TestDomainFactory.OtherUser,
            job.Lease!.ExpiresUtc);

        Assert.Equal(JobLeaseRecoveryDisposition.TimedOutDryRun, disposition);
        Assert.Equal(JobStatus.TimedOut, job.Status);
        Assert.Null(job.Lease);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpiredActiveExecutionRecordsTimedOutOutcome(bool postValidation)
    {
        var job = CreateExecutionQueuedJob();
        var credentials = Acquire(job, JobWorkKind.Execute, 1);
        var execution = job.StartLeasedExecutionAttempt(
            credentials,
            TestDomainFactory.OtherUser,
            job.UpdatedUtc.AddSeconds(1));
        if (postValidation)
        {
            job.BeginPostValidation(
                credentials,
                TestDomainFactory.OtherUser,
                job.UpdatedUtc.AddSeconds(1));
        }

        var disposition = job.RecoverExpiredWorkLease(
            credentials,
            TestDomainFactory.OtherUser,
            job.Lease!.ExpiresUtc);

        Assert.Equal(JobLeaseRecoveryDisposition.TimedOutExecution, disposition);
        Assert.Equal(JobStatus.TimedOut, job.Status);
        Assert.Equal(ExecutionOutcome.TimedOut, execution.Outcome);
        Assert.Null(execution.ExitCode);
        Assert.Null(job.Lease);
    }

    [Fact]
    public void RecoveryBeforeExpirationAndWithStaleCredentialsLeavesStateUnchanged()
    {
        var job = CreateExecutionQueuedJob();
        var credentials = Acquire(job, JobWorkKind.Execute, 5);
        var lease = job.Lease;
        var status = job.Status;
        var updated = job.UpdatedUtc;

        Assert.Throws<DomainValidationException>(
            () => job.RecoverExpiredWorkLease(
                credentials,
                TestDomainFactory.OtherUser,
                lease!.ExpiresUtc.AddTicks(-1)));
        Assert.Throws<DomainValidationException>(
            () => job.RecoverExpiredWorkLease(
                new JobLeaseCredentials(credentials.LeaseId, WorkerId, 4),
                TestDomainFactory.OtherUser,
                lease!.ExpiresUtc));

        Assert.Same(lease, job.Lease);
        Assert.Equal(status, job.Status);
        Assert.Equal(updated, job.UpdatedUtc);
    }

    [Fact]
    public void WorkerCapabilitySynchronizationIsAtomicAndIdempotent()
    {
        var worker = new WorkerNode(WorkerId, "worker-01", TestDomainFactory.Time);
        worker.RegisterCapability(new WorkerCapability("OS", "Windows"));

        Assert.False(worker.SynchronizeCapabilities([new WorkerCapability("OS", "Windows")]));
        Assert.True(
            worker.SynchronizeCapabilities(
                [
                    new WorkerCapability("OS", "Windows Server"),
                    new WorkerCapability("Role", "General"),
                ]));
        Assert.Equal(2, worker.Capabilities.Count);
        Assert.Contains(worker.Capabilities, capability => capability.Value == "Windows Server");

        Assert.Throws<DomainValidationException>(
            () => worker.SynchronizeCapabilities(
                [
                    new WorkerCapability("PowerShell", "7.4"),
                    new WorkerCapability("powershell", "7.5"),
                ]));
        Assert.Equal(2, worker.Capabilities.Count);
        Assert.DoesNotContain(
            worker.Capabilities,
            capability => capability.Name.Equals("PowerShell", StringComparison.OrdinalIgnoreCase));
    }

    private static Job CreateDryRunQueuedJob()
    {
        var version = TestDomainFactory.Version();
        var script = TestDomainFactory.Script(version);
        var job = TestDomainFactory.SubmittedJob(
            script,
            version,
            requestedPhase: ExecutionPhase.DryRun);
        job.MarkValidated(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.QueueDryRun(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        return job;
    }

    private static Job CreateExecutionQueuedJob()
    {
        var version = TestDomainFactory.Version();
        var script = TestDomainFactory.Script(version);
        var job = TestDomainFactory.SubmittedJob(
            script,
            version,
            requestedPhase: ExecutionPhase.Execute);
        TestDomainFactory.AdvanceToAwaitingApproval(job);
        job.RecordApproval(
            TestDomainFactory.OtherUser,
            TestDomainFactory.Fingerprint,
            null,
            job.UpdatedUtc.AddMinutes(1));
        job.QueueExecution(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        return job;
    }

    private static JobLeaseCredentials Acquire(Job job, JobWorkKind kind, long token)
    {
        var acquired = job.UpdatedUtc.AddSeconds(1);
        return job.AcquireWorkLease(
            JobLeaseId.New(),
            WorkerId,
            kind,
            token,
            TestDomainFactory.OtherUser,
            acquired,
            acquired.AddMinutes(2)).Credentials;
    }
}
