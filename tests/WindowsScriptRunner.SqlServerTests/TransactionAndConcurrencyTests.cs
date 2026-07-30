using Microsoft.EntityFrameworkCore;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Jobs;
using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Credentials;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Domain.Workers;

namespace WindowsScriptRunner.SqlServerTests;

public sealed class TransactionAndConcurrencyTests
{
    [Fact]
    public async Task JobAndAuditCommitAtomically()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var version = SqlServerTestData.Version();
        var script = SqlServerTestData.Script(version);
        var job = SqlServerTestData.DraftJob(script, version);
        await SeedJobAsync(database, script, job);

        var audit = Audit("JobDescriptionUpdated", job.Id);
        await using (var scope = new PersistenceTestScope(database))
        {
            var loaded = Assert.IsType<Job>(
                await scope.Jobs.GetByIdAsync(job.Id, CancellationToken.None));
            loaded.UpdateDescription(
                "Atomic update",
                SqlServerTestData.Requester,
                loaded.UpdatedUtc.AddMinutes(1));
            await scope.Jobs.UpdateAsync(loaded, CancellationToken.None);
            await scope.Audits.WriteAsync(audit, CancellationToken.None);
            await scope.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using var verification = new PersistenceTestScope(database);
        Assert.Equal(
            "Atomic update",
            Assert.IsType<Job>(
                await verification.Jobs.GetByIdAsync(job.Id, CancellationToken.None)).Description);
        Assert.True(await verification.Context.AuditEvents.AnyAsync(
            item => item.Id == audit.Id.Value));
    }

    [Fact]
    public async Task ConstraintFailureRollsBackAggregateAndAuditChanges()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var version = SqlServerTestData.Version();
        var script = SqlServerTestData.Script(version);
        var job = SqlServerTestData.DraftJob(script, version);
        var existingAudit = Audit("JobCreated", job.Id);
        await using (var seed = new PersistenceTestScope(database))
        {
            await seed.Scripts.AddAsync(script, CancellationToken.None);
            await seed.Jobs.AddAsync(job, CancellationToken.None);
            await seed.Audits.WriteAsync(existingAudit, CancellationToken.None);
            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using (var scope = new PersistenceTestScope(database))
        {
            var loaded = Assert.IsType<Job>(
                await scope.Jobs.GetByIdAsync(job.Id, CancellationToken.None));
            loaded.UpdateDescription(
                "Must roll back",
                SqlServerTestData.Requester,
                loaded.UpdatedUtc.AddMinutes(1));
            await scope.Jobs.UpdateAsync(loaded, CancellationToken.None);
            var duplicateAudit = new AuditEvent(
                existingAudit.Id,
                "Duplicate",
                "Job",
                job.Id.ToString(),
                SqlServerTestData.Requester,
                SqlServerTestData.Time.AddMinutes(2),
                "Forced duplicate primary key");
            await scope.Audits.WriteAsync(duplicateAudit, CancellationToken.None);

            await Assert.ThrowsAsync<ApplicationConflictException>(
                () => scope.UnitOfWork.CommitAsync(CancellationToken.None));
        }

        await using var verification = new PersistenceTestScope(database);
        Assert.Equal(
            job.Description,
            Assert.IsType<Job>(
                await verification.Jobs.GetByIdAsync(job.Id, CancellationToken.None)).Description);
        Assert.Equal(1, await verification.Context.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task StaleJobCommitConflictsAndDoesNotCommitItsAudit()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var version = SqlServerTestData.Version();
        var script = SqlServerTestData.Script(version);
        var job = SqlServerTestData.DraftJob(script, version);
        await SeedJobAsync(database, script, job);
        await using var first = new PersistenceTestScope(database);
        await using var second = new PersistenceTestScope(database);
        var stale = Assert.IsType<Job>(
            await first.Jobs.GetByIdAsync(job.Id, CancellationToken.None));
        var winner = Assert.IsType<Job>(
            await second.Jobs.GetByIdAsync(job.Id, CancellationToken.None));
        winner.UpdateDescription(
            "Winner",
            SqlServerTestData.Approver,
            winner.UpdatedUtc.AddMinutes(1));
        await second.Jobs.UpdateAsync(winner, CancellationToken.None);
        await second.UnitOfWork.CommitAsync(CancellationToken.None);

        stale.UpdateDescription(
            "Stale",
            SqlServerTestData.Requester,
            stale.UpdatedUtc.AddMinutes(2));
        await first.Jobs.UpdateAsync(stale, CancellationToken.None);
        var staleAudit = Audit("StaleUpdate", job.Id);
        await first.Audits.WriteAsync(staleAudit, CancellationToken.None);
        await Assert.ThrowsAsync<ApplicationConflictException>(
            () => first.UnitOfWork.CommitAsync(CancellationToken.None));

        await using var verification = new PersistenceTestScope(database);
        Assert.Equal(
            "Winner",
            Assert.IsType<Job>(
                await verification.Jobs.GetByIdAsync(job.Id, CancellationToken.None)).Description);
        Assert.False(await verification.Context.AuditEvents.AnyAsync(
            item => item.Id == staleAudit.Id.Value));
    }

    [Fact]
    public async Task StaleScriptChildOnlyCommitConflictsAndRollsBackItsAudit()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var version = SqlServerTestData.Version(publish: false);
        var script = SqlServerTestData.Script(version);
        await using (var seed = new PersistenceTestScope(database))
        {
            await seed.Scripts.AddAsync(script, CancellationToken.None);
            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using var first = new PersistenceTestScope(database);
        await using var second = new PersistenceTestScope(database);
        var stale = Assert.IsType<WindowsScriptRunner.Domain.Scripts.ScriptDefinition>(
            await first.Scripts.GetByIdAsync(script.Id, CancellationToken.None));
        var winner = Assert.IsType<WindowsScriptRunner.Domain.Scripts.ScriptDefinition>(
            await second.Scripts.GetByIdAsync(script.Id, CancellationToken.None));
        Assert.Single(winner.Versions).Publish();
        await second.Scripts.UpdateAsync(winner, CancellationToken.None);
        await second.UnitOfWork.CommitAsync(CancellationToken.None);
        Assert.Single(stale.Versions).Publish();
        await first.Scripts.UpdateAsync(stale, CancellationToken.None);
        var staleAudit = new AuditEvent(
            AuditEventId.New(),
            "ScriptVersionPublished",
            "ScriptDefinition",
            script.Id.ToString(),
            SqlServerTestData.Requester,
            SqlServerTestData.Time.AddMinutes(2),
            "Stale publication");
        await first.Audits.WriteAsync(staleAudit, CancellationToken.None);

        await Assert.ThrowsAsync<ApplicationConflictException>(
            () => first.UnitOfWork.CommitAsync(CancellationToken.None));
        await using var verification = new PersistenceTestScope(database);
        Assert.True(
            Assert.Single(
                Assert.IsType<WindowsScriptRunner.Domain.Scripts.ScriptDefinition>(
                    await verification.Scripts.GetByIdAsync(script.Id, CancellationToken.None))
                    .Versions)
                .IsPublished);
        Assert.False(await verification.Context.AuditEvents.AnyAsync(
            item => item.Id == staleAudit.Id.Value));
    }

    [Fact]
    public async Task SubmissionRevalidatesScriptAfterConcurrentDisable()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var version = SqlServerTestData.Version();
        var script = SqlServerTestData.Script(version);
        var job = SqlServerTestData.DraftJob(script, version);
        job.AddTarget(
            new TargetName("server-01"),
            SqlServerTestData.Requester,
            SqlServerTestData.Time.AddMinutes(1));
        await SeedJobAsync(database, script, job);

        await using (var submission = new PersistenceTestScope(database))
        {
            var handler = new SubmitJobHandler(
                submission.Jobs,
                submission.Scripts,
                submission.Audits,
                new ConcurrentScriptDisableUnitOfWork(
                    database,
                    submission.UnitOfWork,
                    script.Id),
                new FixedClock(SqlServerTestData.Time.AddMinutes(2)));

            await Assert.ThrowsAsync<ApplicationConflictException>(
                () => handler.HandleAsync(
                    new SubmitJobCommand(job.Id, SqlServerTestData.Requester),
                    CancellationToken.None));
        }

        await using var verification = new PersistenceTestScope(database);
        Assert.Equal(
            JobStatus.Draft,
            Assert.IsType<Job>(
                await verification.Jobs.GetByIdAsync(job.Id, CancellationToken.None)).Status);
        Assert.False(
            Assert.IsType<ScriptDefinition>(
                await verification.Scripts.GetByIdAsync(script.Id, CancellationToken.None))
                .IsEnabled);
        Assert.False(await verification.Context.AuditEvents.AnyAsync(
            item => item.EventType == "JobSubmitted" &&
                item.EntityId == job.Id.ToString()));
    }

    [Fact]
    public async Task LeaseAcquisitionRevalidatesWorkerAfterConcurrentDisable()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var version = SqlServerTestData.Version();
        var script = SqlServerTestData.Script(version);
        var job = CreateExecutionQueuedJob(script, version);
        var worker = SqlServerTestData.Worker();
        await using (var seed = new PersistenceTestScope(database))
        {
            await seed.Scripts.AddAsync(script, CancellationToken.None);
            await seed.Jobs.AddAsync(job, CancellationToken.None);
            await seed.Workers.AddAsync(worker, CancellationToken.None);
            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using (var acquisition = new PersistenceTestScope(database))
        {
            var handler = new AcquireJobLeaseHandler(
                acquisition.Jobs,
                acquisition.Workers,
                acquisition.FencingTokens,
                acquisition.Audits,
                new ConcurrentWorkerDisableUnitOfWork(
                    database,
                    acquisition.UnitOfWork,
                    worker.Id),
                new FixedClock(job.UpdatedUtc.AddMinutes(1)));

            await Assert.ThrowsAsync<ApplicationConflictException>(
                () => handler.HandleAsync(
                    new AcquireJobLeaseCommand(
                        job.Id,
                        JobWorkKind.Execute,
                        worker.Id,
                        TimeSpan.FromMinutes(2),
                        TimeSpan.FromHours(1)),
                    CancellationToken.None));
        }

        await using var verification = new PersistenceTestScope(database);
        var persistedJob = Assert.IsType<Job>(
            await verification.Jobs.GetByIdAsync(job.Id, CancellationToken.None));
        Assert.Equal(JobStatus.ExecutionQueued, persistedJob.Status);
        Assert.Null(persistedJob.Lease);
        Assert.Empty(persistedJob.Executions);
        Assert.False(
            Assert.IsType<WorkerNode>(
                await verification.Workers.GetByIdAsync(worker.Id, CancellationToken.None))
                .IsEnabled);
        Assert.False(await verification.Context.AuditEvents.AnyAsync(
            item => item.EventType == "JobLeaseAcquired" &&
                item.EntityId == job.Id.ToString()));
    }

    [Fact]
    public async Task SecureParameterBindingRevalidatesCredentialAfterConcurrentDisable()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var parameter = SqlServerTestData.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            required: true,
            sensitive: true);
        var version = SqlServerTestData.Version([parameter]);
        var script = SqlServerTestData.Script(version);
        var job = SqlServerTestData.DraftJob(script, version);
        var credential = SqlServerTestData.Credential();
        await using (var seed = new PersistenceTestScope(database))
        {
            await seed.Scripts.AddAsync(script, CancellationToken.None);
            await seed.Jobs.AddAsync(job, CancellationToken.None);
            await seed.Credentials.AddAsync(credential, CancellationToken.None);
            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using (var binding = new PersistenceTestScope(database))
        {
            var handler = new SetJobParameterHandler(
                binding.Jobs,
                binding.Scripts,
                binding.Credentials,
                binding.Audits,
                new ConcurrentCredentialDisableUnitOfWork(
                    database,
                    binding.UnitOfWork,
                    credential.Id),
                new FixedClock(job.UpdatedUtc.AddMinutes(1)));

            await Assert.ThrowsAsync<ApplicationConflictException>(
                () => handler.HandleAsync(
                    new SetJobParameterCommand(
                        job.Id,
                        parameter.Name,
                        credential.Id.ToString(),
                        SqlServerTestData.Requester),
                    CancellationToken.None));
        }

        await using var verification = new PersistenceTestScope(database);
        Assert.Empty(
            Assert.IsType<Job>(
                await verification.Jobs.GetByIdAsync(job.Id, CancellationToken.None))
                .Parameters);
        Assert.False(
            Assert.IsType<CredentialReference>(
                await verification.Credentials.GetByIdAsync(
                    credential.Id,
                    CancellationToken.None))
                .IsEnabled);
        Assert.False(await verification.Context.AuditEvents.AnyAsync(
            item => item.EventType == "JobParameterSet" &&
                item.EntityId == job.Id.ToString()));
    }

    [Fact]
    public async Task ConcurrentSubmissionsCanShareEnabledScript()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var version = SqlServerTestData.Version();
        var script = SqlServerTestData.Script(version);
        var firstJob = CreateDraftJobWithTarget(script, version);
        var secondJob = CreateDraftJobWithTarget(script, version);
        await using (var seed = new PersistenceTestScope(database))
        {
            await seed.Scripts.AddAsync(script, CancellationToken.None);
            await seed.Jobs.AddAsync(firstJob, CancellationToken.None);
            await seed.Jobs.AddAsync(secondJob, CancellationToken.None);
            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using var first = new PersistenceTestScope(database);
        await using var second = new PersistenceTestScope(database);
        var commitBarrier = new CommitBarrier(2);
        var firstHandler = new SubmitJobHandler(
            first.Jobs,
            first.Scripts,
            first.Audits,
            new CoordinatedUnitOfWork(first.UnitOfWork, commitBarrier),
            new FixedClock(firstJob.UpdatedUtc.AddMinutes(1)));
        var secondHandler = new SubmitJobHandler(
            second.Jobs,
            second.Scripts,
            second.Audits,
            new CoordinatedUnitOfWork(second.UnitOfWork, commitBarrier),
            new FixedClock(secondJob.UpdatedUtc.AddMinutes(1)));

        await Task.WhenAll(
            firstHandler.HandleAsync(
                new SubmitJobCommand(firstJob.Id, SqlServerTestData.Requester),
                CancellationToken.None),
            secondHandler.HandleAsync(
                new SubmitJobCommand(secondJob.Id, SqlServerTestData.Requester),
                CancellationToken.None));

        await using var verification = new PersistenceTestScope(database);
        Assert.Equal(
            JobStatus.Submitted,
            Assert.IsType<Job>(
                await verification.Jobs.GetByIdAsync(
                    firstJob.Id,
                    CancellationToken.None))
                .Status);
        Assert.Equal(
            JobStatus.Submitted,
            Assert.IsType<Job>(
                await verification.Jobs.GetByIdAsync(
                    secondJob.Id,
                    CancellationToken.None))
                .Status);
    }

    [Fact]
    public async Task ConcurrentLeaseAcquisitionsCanShareEnabledWorker()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var version = SqlServerTestData.Version();
        var script = SqlServerTestData.Script(version);
        var firstJob = CreateExecutionQueuedJob(script, version);
        var secondJob = CreateExecutionQueuedJob(script, version);
        var worker = SqlServerTestData.Worker();
        await using (var seed = new PersistenceTestScope(database))
        {
            await seed.Scripts.AddAsync(script, CancellationToken.None);
            await seed.Jobs.AddAsync(firstJob, CancellationToken.None);
            await seed.Jobs.AddAsync(secondJob, CancellationToken.None);
            await seed.Workers.AddAsync(worker, CancellationToken.None);
            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using var first = new PersistenceTestScope(database);
        await using var second = new PersistenceTestScope(database);
        var commitBarrier = new CommitBarrier(2);
        var firstHandler = new AcquireJobLeaseHandler(
            first.Jobs,
            first.Workers,
            first.FencingTokens,
            first.Audits,
            new CoordinatedUnitOfWork(first.UnitOfWork, commitBarrier),
            new FixedClock(firstJob.UpdatedUtc.AddMinutes(1)));
        var secondHandler = new AcquireJobLeaseHandler(
            second.Jobs,
            second.Workers,
            second.FencingTokens,
            second.Audits,
            new CoordinatedUnitOfWork(second.UnitOfWork, commitBarrier),
            new FixedClock(secondJob.UpdatedUtc.AddMinutes(1)));

        await Task.WhenAll(
            firstHandler.HandleAsync(
                new AcquireJobLeaseCommand(
                    firstJob.Id,
                    JobWorkKind.Execute,
                    worker.Id,
                    TimeSpan.FromMinutes(2),
                    TimeSpan.FromHours(1)),
                CancellationToken.None),
            secondHandler.HandleAsync(
                new AcquireJobLeaseCommand(
                    secondJob.Id,
                    JobWorkKind.Execute,
                    worker.Id,
                    TimeSpan.FromMinutes(2),
                    TimeSpan.FromHours(1)),
                CancellationToken.None));

        await using var verification = new PersistenceTestScope(database);
        Assert.Equal(
            JobStatus.Claimed,
            Assert.IsType<Job>(
                await verification.Jobs.GetByIdAsync(
                    firstJob.Id,
                    CancellationToken.None))
                .Status);
        Assert.Equal(
            JobStatus.Claimed,
            Assert.IsType<Job>(
                await verification.Jobs.GetByIdAsync(
                    secondJob.Id,
                    CancellationToken.None))
                .Status);
    }

    [Fact]
    public async Task ConcurrentSecureBindingsCanShareEnabledCredential()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var parameter = SqlServerTestData.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            required: true,
            sensitive: true);
        var version = SqlServerTestData.Version([parameter]);
        var script = SqlServerTestData.Script(version);
        var firstJob = SqlServerTestData.DraftJob(script, version);
        var secondJob = SqlServerTestData.DraftJob(script, version);
        var credential = SqlServerTestData.Credential();
        await using (var seed = new PersistenceTestScope(database))
        {
            await seed.Scripts.AddAsync(script, CancellationToken.None);
            await seed.Jobs.AddAsync(firstJob, CancellationToken.None);
            await seed.Jobs.AddAsync(secondJob, CancellationToken.None);
            await seed.Credentials.AddAsync(credential, CancellationToken.None);
            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using var first = new PersistenceTestScope(database);
        await using var second = new PersistenceTestScope(database);
        var commitBarrier = new CommitBarrier(2);
        var firstHandler = new SetJobParameterHandler(
            first.Jobs,
            first.Scripts,
            first.Credentials,
            first.Audits,
            new CoordinatedUnitOfWork(first.UnitOfWork, commitBarrier),
            new FixedClock(firstJob.UpdatedUtc.AddMinutes(1)));
        var secondHandler = new SetJobParameterHandler(
            second.Jobs,
            second.Scripts,
            second.Credentials,
            second.Audits,
            new CoordinatedUnitOfWork(second.UnitOfWork, commitBarrier),
            new FixedClock(secondJob.UpdatedUtc.AddMinutes(1)));

        await Task.WhenAll(
            firstHandler.HandleAsync(
                new SetJobParameterCommand(
                    firstJob.Id,
                    parameter.Name,
                    credential.Id.ToString(),
                    SqlServerTestData.Requester),
                CancellationToken.None),
            secondHandler.HandleAsync(
                new SetJobParameterCommand(
                    secondJob.Id,
                    parameter.Name,
                    credential.Id.ToString(),
                    SqlServerTestData.Requester),
                CancellationToken.None));

        await using var verification = new PersistenceTestScope(database);
        Assert.Equal(
            credential.Id.ToString(),
            Assert.Single(
                Assert.IsType<Job>(
                    await verification.Jobs.GetByIdAsync(
                        firstJob.Id,
                        CancellationToken.None))
                    .Parameters)
                .SerializedValue);
        Assert.Equal(
            credential.Id.ToString(),
            Assert.Single(
                Assert.IsType<Job>(
                    await verification.Jobs.GetByIdAsync(
                        secondJob.Id,
                        CancellationToken.None))
                    .Parameters)
                .SerializedValue);
    }

    [Fact]
    public async Task StaleWorkerChildOnlyCommitConflicts()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var worker = SqlServerTestData.Worker();
        await using (var seed = new PersistenceTestScope(database))
        {
            await seed.Workers.AddAsync(worker, CancellationToken.None);
            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using var first = new PersistenceTestScope(database);
        await using var second = new PersistenceTestScope(database);
        var stale = Assert.IsType<WindowsScriptRunner.Domain.Workers.WorkerNode>(
            await first.Workers.GetByIdAsync(worker.Id, CancellationToken.None));
        var winner = Assert.IsType<WindowsScriptRunner.Domain.Workers.WorkerNode>(
            await second.Workers.GetByIdAsync(worker.Id, CancellationToken.None));
        winner.RegisterCapability(new WorkerCapability("OperatingSystem", "Windows"));
        await second.Workers.UpdateAsync(winner, CancellationToken.None);
        await second.UnitOfWork.CommitAsync(CancellationToken.None);
        stale.RegisterCapability(new WorkerCapability("Role", "General"));
        await first.Workers.UpdateAsync(stale, CancellationToken.None);

        await Assert.ThrowsAsync<ApplicationConflictException>(
            () => first.UnitOfWork.CommitAsync(CancellationToken.None));
        await using var verification = new PersistenceTestScope(database);
        var persisted = Assert.IsType<WindowsScriptRunner.Domain.Workers.WorkerNode>(
            await verification.Workers.GetByIdAsync(worker.Id, CancellationToken.None));
        Assert.Contains(
            persisted.Capabilities,
            capability => capability.Name == "OperatingSystem");
        Assert.DoesNotContain(
            persisted.Capabilities,
            capability => capability.Name == "Role");
    }

    private static async Task SeedJobAsync(
        SqlServerDatabase database,
        WindowsScriptRunner.Domain.Scripts.ScriptDefinition script,
        Job job)
    {
        await using var seed = new PersistenceTestScope(database);
        await seed.Scripts.AddAsync(script, CancellationToken.None);
        await seed.Jobs.AddAsync(job, CancellationToken.None);
        await seed.UnitOfWork.CommitAsync(CancellationToken.None);
    }

    private static Job CreateExecutionQueuedJob(
        ScriptDefinition script,
        ScriptVersion version)
    {
        var job = SqlServerTestData.SubmittedJob(script, version);
        job.MarkValidated(SqlServerTestData.Approver, job.UpdatedUtc.AddMinutes(1));
        job.QueueDryRun(SqlServerTestData.Approver, job.UpdatedUtc.AddMinutes(1));
        job.StartDryRun(SqlServerTestData.Approver, job.UpdatedUtc.AddMinutes(1));
        job.CompleteDryRun(SqlServerTestData.Approver, job.UpdatedUtc.AddMinutes(1));
        job.RequireApproval(SqlServerTestData.Approver, job.UpdatedUtc.AddMinutes(1));
        job.RecordApproval(
            SqlServerTestData.Approver,
            SqlServerTestData.Fingerprint,
            null,
            job.UpdatedUtc.AddMinutes(1));
        job.QueueExecution(SqlServerTestData.Approver, job.UpdatedUtc.AddMinutes(1));
        return job;
    }

    private static Job CreateDraftJobWithTarget(
        ScriptDefinition script,
        ScriptVersion version)
    {
        var job = SqlServerTestData.DraftJob(script, version);
        job.AddTarget(
            new TargetName("server-01"),
            SqlServerTestData.Requester,
            job.UpdatedUtc.AddMinutes(1));
        return job;
    }

    private static AuditEvent Audit(string eventType, JobId jobId) =>
        new(
            AuditEventId.New(),
            eventType,
            "Job",
            jobId.ToString(),
            SqlServerTestData.Requester,
            SqlServerTestData.Time.AddMinutes(10),
            eventType);

    private sealed class FixedClock(DateTimeOffset utcNow) :
        IClock,
        IWorkerCoordinationClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;

        public Task<DateTimeOffset> GetUtcNowAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(UtcNow);
        }
    }

    private sealed class ConcurrentScriptDisableUnitOfWork(
        SqlServerDatabase database,
        IUnitOfWork inner,
        ScriptDefinitionId scriptDefinitionId) : IUnitOfWork
    {
        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await using (var competing = new PersistenceTestScope(database))
            {
                var script = Assert.IsType<ScriptDefinition>(
                    await competing.Scripts.GetByIdAsync(
                        scriptDefinitionId,
                        cancellationToken));
                script.Disable(script.UpdatedUtc.AddMinutes(1));
                await competing.Scripts.UpdateAsync(script, cancellationToken);
                await competing.UnitOfWork.CommitAsync(cancellationToken);
            }

            await inner.CommitAsync(cancellationToken);
        }
    }

    private sealed class ConcurrentWorkerDisableUnitOfWork(
        SqlServerDatabase database,
        IUnitOfWork inner,
        WorkerNodeId workerNodeId) : IUnitOfWork
    {
        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await using (var competing = new PersistenceTestScope(database))
            {
                var worker = Assert.IsType<WorkerNode>(
                    await competing.Workers.GetByIdAsync(
                        workerNodeId,
                        cancellationToken));
                worker.Disable();
                await competing.Workers.UpdateAsync(worker, cancellationToken);
                await competing.UnitOfWork.CommitAsync(cancellationToken);
            }

            await inner.CommitAsync(cancellationToken);
        }
    }

    private sealed class ConcurrentCredentialDisableUnitOfWork(
        SqlServerDatabase database,
        IUnitOfWork inner,
        CredentialReferenceId credentialReferenceId) : IUnitOfWork
    {
        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await using (var competing = new PersistenceTestScope(database))
            {
                var credential = Assert.IsType<CredentialReference>(
                    await competing.Credentials.GetByIdAsync(
                        credentialReferenceId,
                        cancellationToken));
                credential.Disable();
                await competing.Credentials.UpdateAsync(credential, cancellationToken);
                await competing.UnitOfWork.CommitAsync(cancellationToken);
            }

            await inner.CommitAsync(cancellationToken);
        }
    }

    private sealed class CoordinatedUnitOfWork(
        IUnitOfWork inner,
        CommitBarrier commitBarrier) : IUnitOfWork
    {
        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await commitBarrier.SignalAndWaitAsync(cancellationToken);
            await inner.CommitAsync(cancellationToken);
        }
    }

    private sealed class CommitBarrier(int participantCount)
    {
        private readonly TaskCompletionSource<bool> allParticipantsReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public async Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrivals) == participantCount)
            {
                allParticipantsReady.TrySetResult(true);
            }

            await allParticipantsReady.Task.WaitAsync(cancellationToken);
        }
    }
}
