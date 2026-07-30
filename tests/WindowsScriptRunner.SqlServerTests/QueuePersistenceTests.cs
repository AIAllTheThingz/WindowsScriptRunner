using Microsoft.EntityFrameworkCore;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Jobs;
using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Application.Workers;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Domain.Workers;

namespace WindowsScriptRunner.SqlServerTests;

public sealed class QueuePersistenceTests
{
    [Fact]
    public async Task SqlCoordinationClockDrivesExpiredLeaseDiscovery()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        DateTimeOffset databaseUtc;
        await using (var timing = new PersistenceTestScope(database))
        {
            var before = await timing.Context.Database.SqlQueryRaw<DateTimeOffset>(
                "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') AS [Value]")
                .SingleAsync();
            databaseUtc = await timing.CoordinationClock.GetUtcNowAsync(
                CancellationToken.None);
            var after = await timing.Context.Database.SqlQueryRaw<DateTimeOffset>(
                "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') AS [Value]")
                .SingleAsync();
            Assert.InRange(databaseUtc, before, after);
        }

        var version = SqlServerTestData.Version();
        var script = SqlServerTestData.Script(version);
        var job = SqlServerTestData.DryRunQueuedJob(script, version);
        var worker = Worker("worker-sql-clock", databaseUtc);
        var acquiredUtc = databaseUtc.AddMinutes(-2);
        var lease = job.AcquireWorkLease(
            JobLeaseId.New(),
            worker.Id,
            JobWorkKind.DryRun,
            1,
            new UserIdentity("worker:sql-clock"),
            acquiredUtc,
            acquiredUtc.AddMinutes(1));
        await SeedAsync(database, script, [job], [worker]);

        await using var discovery = new PersistenceTestScope(database);
        var candidate = Assert.Single(await discovery.ExpiredLeases.FindExpiredAsync(
            10,
            CancellationToken.None));

        Assert.Equal(job.Id, candidate.JobId);
        Assert.Equal(lease.Credentials, candidate.Credentials);
    }

    [Fact]
    public async Task FencingSequenceIncreasesAndCandidateDiscoveryIsBoundedFilteredAndDeterministic()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var version = SqlServerTestData.Version();
        var script = SqlServerTestData.Script(version);
        var dryRunJobs = Enumerable.Range(0, 3)
            .Select(_ => SqlServerTestData.DryRunQueuedJob(script, version))
            .ToArray();
        var executeJob = SqlServerTestData.ExecutionQueuedJob(script, version);
        var submittedJob = SqlServerTestData.SubmittedJob(script, version);
        await using (var seed = new PersistenceTestScope(database))
        {
            await seed.Scripts.AddAsync(script, CancellationToken.None);
            foreach (var job in dryRunJobs.Append(executeJob).Append(submittedJob))
            {
                await seed.Jobs.AddAsync(job, CancellationToken.None);
            }

            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using var scope = new PersistenceTestScope(database);
        var firstToken = await scope.FencingTokens.GetNextAsync(CancellationToken.None);
        var secondToken = await scope.FencingTokens.GetNextAsync(CancellationToken.None);
        var dryRunCandidates = await scope.Candidates.FindCandidatesAsync(
            new HashSet<JobWorkKind> { JobWorkKind.DryRun },
            2,
            SqlServerTestData.Time.AddDays(1),
            CancellationToken.None);
        var allDryRunCandidates = await scope.Candidates.FindCandidatesAsync(
            new HashSet<JobWorkKind> { JobWorkKind.DryRun },
            10,
            SqlServerTestData.Time.AddDays(1),
            CancellationToken.None);
        var repeatedDryRunCandidates = await scope.Candidates.FindCandidatesAsync(
            new HashSet<JobWorkKind> { JobWorkKind.DryRun },
            10,
            SqlServerTestData.Time.AddDays(1),
            CancellationToken.None);
        var allCandidates = await scope.Candidates.FindCandidatesAsync(
            new HashSet<JobWorkKind> { JobWorkKind.DryRun, JobWorkKind.Execute },
            10,
            SqlServerTestData.Time.AddDays(1),
            CancellationToken.None);

        Assert.True(secondToken > firstToken);
        Assert.Equal(2, dryRunCandidates.Count);
        Assert.All(dryRunCandidates, candidate => Assert.Equal(JobWorkKind.DryRun, candidate.WorkKind));
        Assert.Equal(
            allDryRunCandidates.Take(2).Select(candidate => candidate.JobId),
            dryRunCandidates.Select(candidate => candidate.JobId));
        Assert.Equal(
            allDryRunCandidates.Select(candidate => candidate.JobId),
            repeatedDryRunCandidates.Select(candidate => candidate.JobId));
        Assert.Equal(4, allCandidates.Count);
        Assert.DoesNotContain(allCandidates, candidate => candidate.JobId == submittedJob.Id);
        Assert.All(
            typeof(JobQueueCandidate).GetProperties(),
            property => Assert.DoesNotContain(
                ["Parameter", "Credential", "ScriptPath", "ChangeReference", "Approval"],
                prohibited => property.Name.Contains(
                    prohibited,
                    StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task LeaseRoundTripsRenewsAndReleasesWithRestrictiveWorkerRelationship()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var version = SqlServerTestData.Version();
        var script = SqlServerTestData.Script(version);
        var job = SqlServerTestData.ExecutionQueuedJob(script, version);
        var worker = Worker("worker-01", job.UpdatedUtc);
        var claimTime = job.UpdatedUtc.AddMinutes(1);
        ClaimedJobWork claimed;
        await using (var seed = new PersistenceTestScope(database))
        {
            await seed.Scripts.AddAsync(script, CancellationToken.None);
            await seed.Jobs.AddAsync(job, CancellationToken.None);
            await seed.Workers.AddAsync(worker, CancellationToken.None);
            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using (var acquisition = new PersistenceTestScope(database))
        {
            claimed = await CreateAcquireHandler(acquisition, claimTime).HandleAsync(
                new AcquireJobLeaseCommand(
                    job.Id,
                    JobWorkKind.Execute,
                    worker.Id,
                    TimeSpan.FromMinutes(2),
                    TimeSpan.FromHours(1)),
                CancellationToken.None);
        }

        await using (var roundTrip = new PersistenceTestScope(database))
        {
            var loaded = Assert.IsType<Job>(
                await roundTrip.Jobs.GetByIdAsync(job.Id, CancellationToken.None));
            var lease = Assert.IsType<JobLease>(loaded.Lease);
            Assert.Equal(claimed.LeaseId, lease.Id);
            Assert.Equal(job.Id, loaded.Id);
            Assert.Equal(worker.Id, lease.WorkerNodeId);
            Assert.Equal(JobWorkKind.Execute, lease.WorkKind);
            Assert.Equal(claimed.FencingToken, lease.FencingToken);
            Assert.Equal(claimTime, lease.AcquiredUtc);
            Assert.Equal(claimTime, lease.LastRenewedUtc);
            Assert.Equal(claimed.LeaseExpiresUtc, lease.ExpiresUtc);
            Assert.Equal(
                1,
                await roundTrip.Context.JobLeases.CountAsync(
                    leaseRow => leaseRow.JobId == job.Id.Value));
        }

        var renewalTime = claimed.LeaseExpiresUtc.AddMinutes(-1);
        await using (var renewal = new PersistenceTestScope(database))
        {
            var auditCount = await renewal.Context.AuditEvents.CountAsync();
            var expiration = await new RenewJobLeaseHandler(
                renewal.Jobs,
                renewal.UnitOfWork,
                new FixedClock(renewalTime))
                .HandleAsync(
                    new RenewJobLeaseCommand(
                        job.Id,
                        claimed.Credentials,
                        TimeSpan.FromMinutes(2)),
                    CancellationToken.None);
            Assert.Equal(renewalTime.AddMinutes(2), expiration);
            Assert.Equal(auditCount, await renewal.Context.AuditEvents.CountAsync());
        }

        await using (var restrictedDelete = database.CreateContext())
        {
            var entity = await restrictedDelete.WorkerNodes.SingleAsync(
                workerRow => workerRow.Id == worker.Id.Value);
            restrictedDelete.WorkerNodes.Remove(entity);
            await Assert.ThrowsAsync<DbUpdateException>(
                () => restrictedDelete.SaveChangesAsync());
        }

        await using (var release = new PersistenceTestScope(database))
        {
            await new ReleaseUnstartedJobLeaseHandler(
                release.Jobs,
                release.Audits,
                release.UnitOfWork,
                new FixedClock(renewalTime.AddSeconds(1)))
                .HandleAsync(
                    new ReleaseUnstartedJobLeaseCommand(job.Id, claimed.Credentials),
                    CancellationToken.None);
        }

        await using var verification = new PersistenceTestScope(database);
        var released = Assert.IsType<Job>(
            await verification.Jobs.GetByIdAsync(job.Id, CancellationToken.None));
        Assert.Equal(JobStatus.ExecutionQueued, released.Status);
        Assert.Null(released.Lease);
        Assert.False(await verification.Context.JobLeases.AnyAsync(
            lease => lease.JobId == job.Id.Value));
        Assert.True(await verification.Context.AuditEvents.AnyAsync(
            audit => audit.EventType == "JobLeaseReleased" &&
                audit.EntityId == job.Id.ToString()));
    }

    [Fact]
    public async Task TwoWorkersRacingForOneJobProduceOneLeaseAndOneAudit()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var version = SqlServerTestData.Version();
        var script = SqlServerTestData.Script(version);
        var job = SqlServerTestData.ExecutionQueuedJob(script, version);
        var claimTime = job.UpdatedUtc.AddMinutes(1);
        var firstWorker = Worker("worker-01", claimTime);
        var secondWorker = Worker("worker-02", claimTime);
        await SeedAsync(database, script, [job], [firstWorker, secondWorker]);

        await using var discoveryOne = new PersistenceTestScope(database);
        await using var discoveryTwo = new PersistenceTestScope(database);
        Assert.Equal(
            job.Id,
            Assert.Single(await discoveryOne.Candidates.FindCandidatesAsync(
                new HashSet<JobWorkKind> { JobWorkKind.Execute },
                10,
                claimTime,
                CancellationToken.None)).JobId);
        Assert.Equal(
            job.Id,
            Assert.Single(await discoveryTwo.Candidates.FindCandidatesAsync(
                new HashSet<JobWorkKind> { JobWorkKind.Execute },
                10,
                claimTime,
                CancellationToken.None)).JobId);

        await using var first = new PersistenceTestScope(database);
        await using var second = new PersistenceTestScope(database);
        var barrier = new CommitBarrier(2);
        var firstHandler = CreateAcquireHandler(
            first,
            claimTime,
            new CoordinatedUnitOfWork(first.UnitOfWork, barrier));
        var secondHandler = CreateAcquireHandler(
            second,
            claimTime,
            new CoordinatedUnitOfWork(second.UnitOfWork, barrier));
        var results = await Task.WhenAll(
            CaptureAsync(
                () => firstHandler.HandleAsync(
                    Command(job.Id, firstWorker.Id),
                    CancellationToken.None)),
            CaptureAsync(
                () => secondHandler.HandleAsync(
                    Command(job.Id, secondWorker.Id),
                    CancellationToken.None)));

        Assert.Single(results, result => result.Work is not null);
        var failure = Assert.Single(results, result => result.Exception is not null).Exception;
        Assert.IsType<ApplicationConflictException>(failure);
        await using var verification = new PersistenceTestScope(database);
        var persisted = Assert.IsType<Job>(
            await verification.Jobs.GetByIdAsync(job.Id, CancellationToken.None));
        Assert.Equal(JobStatus.Claimed, persisted.Status);
        Assert.NotNull(persisted.Lease);
        Assert.Equal(1, await verification.Context.JobLeases.CountAsync());
        Assert.Equal(
            1,
            await verification.Context.AuditEvents.CountAsync(
                audit => audit.EventType == "JobLeaseAcquired" &&
                    audit.EntityId == job.Id.ToString()));
    }

    [Fact]
    public async Task FourWorkersAcquireManyJobsWithoutDuplicateOwnershipOrFencingTokens()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var version = SqlServerTestData.Version();
        var script = SqlServerTestData.Script(version);
        var jobs = Enumerable.Range(0, 30)
            .Select(_ => SqlServerTestData.ExecutionQueuedJob(script, version))
            .ToArray();
        var claimTime = jobs.Max(job => job.UpdatedUtc).AddMinutes(1);
        var workers = Enumerable.Range(1, 4)
            .Select(index => Worker($"worker-{index:00}", claimTime))
            .ToArray();
        await SeedAsync(database, script, jobs, workers);

        await Task.WhenAll(workers.Select(worker => ClaimDiscoveredAsync(
            database,
            worker.Id,
            claimTime)));

        await using var verification = new PersistenceTestScope(database);
        var leases = await verification.Context.JobLeases
            .AsNoTracking()
            .ToListAsync();
        Assert.Equal(jobs.Length, leases.Count);
        Assert.Equal(jobs.Length, leases.Select(lease => lease.JobId).Distinct().Count());
        Assert.Equal(jobs.Length, leases.Select(lease => lease.FencingToken).Distinct().Count());
        Assert.All(leases, lease => Assert.True(lease.FencingToken > 0));
        Assert.Equal(
            jobs.Length,
            await verification.Context.AuditEvents.CountAsync(
                audit => audit.EventType == "JobLeaseAcquired"));
    }

    [Fact]
    public async Task WorkerRegistrationCapabilitySynchronizationAndHeartbeatPersistAtomically()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var workerId = WorkerNodeId.New();
        var firstHeartbeat = SqlServerTestData.Time.AddMinutes(1);
        await using (var registration = new PersistenceTestScope(database))
        {
            var result = await new RegisterWorkerHandler(
                registration.Workers,
                registration.Audits,
                registration.UnitOfWork,
                new FixedClock(firstHeartbeat))
                .HandleAsync(
                    new RegisterWorkerCommand(
                        workerId,
                        "worker-registration",
                        [
                            new WorkerCapability("OS", "Windows"),
                            new WorkerCapability("Role", "General"),
                        ]),
                    CancellationToken.None);
            Assert.True(result.Created);
        }

        var secondHeartbeat = firstHeartbeat.AddMinutes(1);
        await using (var restart = new PersistenceTestScope(database))
        {
            var result = await new RegisterWorkerHandler(
                restart.Workers,
                restart.Audits,
                restart.UnitOfWork,
                new FixedClock(secondHeartbeat))
                .HandleAsync(
                    new RegisterWorkerCommand(
                        workerId,
                        "worker-registration",
                        [
                            new WorkerCapability("OS", "Windows Server"),
                            new WorkerCapability("Queue", "Enabled"),
                        ]),
                    CancellationToken.None);
            Assert.False(result.Created);
            Assert.True(result.CapabilitiesChanged);
        }

        var thirdHeartbeat = secondHeartbeat.AddMinutes(1);
        await using (var heartbeat = new PersistenceTestScope(database))
        {
            var auditCount = await heartbeat.Context.AuditEvents.CountAsync();
            await new RecordWorkerHeartbeatHandler(
                heartbeat.Workers,
                heartbeat.UnitOfWork,
                new FixedClock(thirdHeartbeat))
                .HandleAsync(
                    new RecordWorkerHeartbeatCommand(workerId),
                    CancellationToken.None);
            Assert.Equal(auditCount, await heartbeat.Context.AuditEvents.CountAsync());
        }

        await using var verification = new PersistenceTestScope(database);
        var worker = Assert.IsType<WorkerNode>(
            await verification.Workers.GetByIdAsync(workerId, CancellationToken.None));
        Assert.Equal(thirdHeartbeat, worker.LastHeartbeatUtc);
        Assert.Equal(
            ["OS|Windows Server", "Queue|Enabled"],
            worker.Capabilities
                .OrderBy(capability => capability.Name)
                .Select(capability => $"{capability.Name}|{capability.Value}"));
        Assert.Equal(
            ["WorkerCapabilitiesSynchronized", "WorkerRegistered"],
            await verification.Context.AuditEvents
                .Where(audit => audit.EntityId == workerId.ToString())
                .OrderBy(audit => audit.EventType)
                .Select(audit => audit.EventType)
                .ToListAsync());
    }

    [Fact]
    public async Task ConcurrentRenewalAndRecoveryProduceOneValidWinner()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var seeded = await SeedClaimedJobAsync(database);
        var candidate = new ExpiredJobLeaseCandidate(
            seeded.JobId,
            seeded.Work.Credentials,
            seeded.Work.LeaseExpiresUtc);
        await using var renewal = new PersistenceTestScope(database);
        await using var recovery = new PersistenceTestScope(database);
        var barrier = new CommitBarrier(2);
        var renewalHandler = new RenewJobLeaseHandler(
            renewal.Jobs,
            new CoordinatedUnitOfWork(renewal.UnitOfWork, barrier),
            new FixedClock(seeded.Work.LeaseExpiresUtc.AddSeconds(-30)));
        var recoveryHandler = new RecoverExpiredJobLeaseHandler(
            recovery.Jobs,
            recovery.Audits,
            new CoordinatedUnitOfWork(recovery.UnitOfWork, barrier),
            new FixedClock(seeded.Work.LeaseExpiresUtc));

        var results = await Task.WhenAll(
            CaptureOperationAsync(
                () => renewalHandler.HandleAsync(
                    new RenewJobLeaseCommand(
                        seeded.JobId,
                        seeded.Work.Credentials,
                        TimeSpan.FromMinutes(2)),
                    CancellationToken.None)),
            CaptureOperationAsync(
                async () =>
                {
                    _ = await recoveryHandler.HandleAsync(
                        new RecoverExpiredJobLeaseCommand(candidate),
                        CancellationToken.None);
                    return seeded.Work.LeaseExpiresUtc;
                }));

        Assert.Single(results, result => result.Exception is null);
        Assert.IsType<ApplicationConflictException>(
            Assert.Single(results, result => result.Exception is not null).Exception);
        await using var verification = new PersistenceTestScope(database);
        var job = Assert.IsType<Job>(
            await verification.Jobs.GetByIdAsync(seeded.JobId, CancellationToken.None));
        Assert.True(
            (job.Status == JobStatus.Claimed &&
                job.Lease is not null &&
                job.Lease.ExpiresUtc > seeded.Work.LeaseExpiresUtc) ||
            (job.Status == JobStatus.ExecutionQueued && job.Lease is null));
    }

    [Fact]
    public async Task ConcurrentRenewalAndExecutionStartBothCommit()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var seeded = await SeedClaimedJobAsync(database);
        var operationTime = seeded.Work.LeaseExpiresUtc.AddSeconds(-30);
        var renewedExpiration = operationTime.AddMinutes(2);
        await using var renewal = new PersistenceTestScope(database);
        await using var execution = new PersistenceTestScope(database);
        var barrier = new CommitBarrier(2);
        var renewalHandler = new RenewJobLeaseHandler(
            renewal.Jobs,
            new CoordinatedUnitOfWork(renewal.UnitOfWork, barrier),
            new FixedClock(operationTime));
        var executionHandler = new StartExecutionAttemptHandler(
            execution.Jobs,
            execution.Audits,
            new CoordinatedUnitOfWork(execution.UnitOfWork, barrier),
            new FixedClock(operationTime));

        var results = await Task.WhenAll(
            CaptureOperationAsync(
                () => renewalHandler.HandleAsync(
                    new RenewJobLeaseCommand(
                        seeded.JobId,
                        seeded.Work.Credentials,
                        TimeSpan.FromMinutes(2)),
                    CancellationToken.None)),
            CaptureOperationAsync(
                async () =>
                {
                    await executionHandler.HandleAsync(
                        new StartExecutionAttemptCommand(
                            seeded.JobId,
                            seeded.Work.Credentials,
                            new UserIdentity("worker:concurrency")),
                        CancellationToken.None);
                    return operationTime;
                }));

        Assert.All(results, result => Assert.Null(result.Exception));
        await using var verification = new PersistenceTestScope(database);
        var job = Assert.IsType<Job>(
            await verification.Jobs.GetByIdAsync(seeded.JobId, CancellationToken.None));
        var lease = Assert.IsType<JobLease>(job.Lease);
        var attempt = Assert.Single(job.Executions);
        Assert.Equal(JobStatus.Executing, job.Status);
        Assert.Equal(renewedExpiration, lease.ExpiresUtc);
        Assert.Equal(operationTime, attempt.StartedUtc);
    }

    [Fact]
    public async Task TerminalOutcomeRetriesAfterConcurrentRenewalCommits()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var seeded = await SeedClaimedJobAsync(database);
        var actor = new UserIdentity("worker:terminal-retry");
        var executionStartedUtc = seeded.Work.LeaseExpiresUtc.AddMinutes(-1);
        await using (var start = new PersistenceTestScope(database))
        {
            _ = await new StartLeasedExecutionHandler(
                start.Jobs,
                start.Audits,
                start.UnitOfWork,
                new FixedClock(executionStartedUtc))
                .HandleAsync(
                    new StartLeasedExecutionCommand(
                        seeded.JobId,
                        seeded.Work.Credentials,
                        actor),
                    CancellationToken.None);
        }

        var operationTime = executionStartedUtc.AddSeconds(1);
        await using var renewal = new PersistenceTestScope(database);
        await using var terminal = new PersistenceTestScope(database);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var commitOrder = new RenewalBeforeTerminalCommit();
        var renewalHandler = new RenewJobLeaseHandler(
            renewal.Jobs,
            new DelegatingUnitOfWork(
                cancellationToken => commitOrder.CommitRenewalAsync(
                    renewal.UnitOfWork,
                    cancellationToken)),
            new FixedClock(operationTime));
        var terminalHandler = new RecordLeasedExecutionOutcomeHandler(
            terminal.Jobs,
            terminal.Audits,
            new DelegatingUnitOfWork(
                cancellationToken => commitOrder.CommitTerminalAsync(
                    terminal.UnitOfWork,
                    cancellationToken)),
            new FixedClock(operationTime));

        var results = await Task.WhenAll(
            CaptureExceptionAsync(
                async () =>
                {
                    _ = await renewalHandler.HandleAsync(
                        new RenewJobLeaseCommand(
                            seeded.JobId,
                            seeded.Work.Credentials,
                            TimeSpan.FromMinutes(2)),
                        timeout.Token);
                }),
            CaptureExceptionAsync(
                async () =>
                {
                    _ = await terminalHandler.HandleAsync(
                        new RecordLeasedExecutionOutcomeCommand(
                            seeded.JobId,
                            seeded.Work.Credentials,
                            ExecutionOutcome.Succeeded,
                            0,
                            null,
                            actor),
                        timeout.Token);
                }));

        Assert.All(results, Assert.Null);
        await using var verification = new PersistenceTestScope(database);
        var job = Assert.IsType<Job>(
            await verification.Jobs.GetByIdAsync(seeded.JobId, CancellationToken.None));
        var attempt = Assert.Single(job.Executions);
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Equal(ExecutionOutcome.Succeeded, attempt.Outcome);
        Assert.Null(job.Lease);
    }

    [Fact]
    public async Task ConcurrentRecoveryAttemptsProduceOneWinner()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var seeded = await SeedClaimedJobAsync(database);
        var candidate = new ExpiredJobLeaseCandidate(
            seeded.JobId,
            seeded.Work.Credentials,
            seeded.Work.LeaseExpiresUtc);
        await using var first = new PersistenceTestScope(database);
        await using var second = new PersistenceTestScope(database);
        var barrier = new CommitBarrier(2);
        var firstHandler = new RecoverExpiredJobLeaseHandler(
            first.Jobs,
            first.Audits,
            new CoordinatedUnitOfWork(first.UnitOfWork, barrier),
            new FixedClock(seeded.Work.LeaseExpiresUtc));
        var secondHandler = new RecoverExpiredJobLeaseHandler(
            second.Jobs,
            second.Audits,
            new CoordinatedUnitOfWork(second.UnitOfWork, barrier),
            new FixedClock(seeded.Work.LeaseExpiresUtc));

        var results = await Task.WhenAll(
            CaptureOperationAsync(
                () => firstHandler.HandleAsync(
                    new RecoverExpiredJobLeaseCommand(candidate),
                    CancellationToken.None)),
            CaptureOperationAsync(
                () => secondHandler.HandleAsync(
                    new RecoverExpiredJobLeaseCommand(candidate),
                    CancellationToken.None)));

        Assert.Single(results, result => result.Exception is null);
        Assert.IsType<ApplicationConflictException>(
            Assert.Single(results, result => result.Exception is not null).Exception);
        await using var verification = new PersistenceTestScope(database);
        var job = Assert.IsType<Job>(
            await verification.Jobs.GetByIdAsync(seeded.JobId, CancellationToken.None));
        Assert.Equal(JobStatus.ExecutionQueued, job.Status);
        Assert.Null(job.Lease);
        Assert.Equal(
            1,
            await verification.Context.AuditEvents.CountAsync(
                audit => audit.EventType == "JobLeaseRecovered" &&
                    audit.EntityId == seeded.JobId.ToString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredActiveExecutionRecoveryPersistsTimedOutOutcome(bool postValidation)
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var version = SqlServerTestData.Version();
        var script = SqlServerTestData.Script(version);
        var job = SqlServerTestData.ExecutionQueuedJob(script, version);
        var claimTime = job.UpdatedUtc.AddMinutes(1);
        var worker = Worker("worker-01", claimTime);
        await SeedAsync(database, script, [job], [worker]);
        ClaimedJobWork claimed;
        await using (var acquisition = new PersistenceTestScope(database))
        {
            claimed = await CreateAcquireHandler(acquisition, claimTime).HandleAsync(
                Command(job.Id, worker.Id),
                CancellationToken.None);
        }

        await using (var start = new PersistenceTestScope(database))
        {
            var actor = new UserIdentity("worker:test");
            _ = await new StartLeasedExecutionHandler(
                start.Jobs,
                start.Audits,
                start.UnitOfWork,
                new FixedClock(claimTime.AddSeconds(1)))
                .HandleAsync(
                    new StartLeasedExecutionCommand(job.Id, claimed.Credentials, actor),
                    CancellationToken.None);
        }

        if (postValidation)
        {
            await using var post = new PersistenceTestScope(database);
            await new BeginLeasedPostValidationHandler(
                post.Jobs,
                post.Audits,
                post.UnitOfWork,
                new FixedClock(claimTime.AddSeconds(2)))
                .HandleAsync(
                    new BeginLeasedPostValidationCommand(
                        job.Id,
                        claimed.Credentials,
                        new UserIdentity("worker:test")),
                    CancellationToken.None);
        }

        await using (var recovery = new PersistenceTestScope(database))
        {
            var candidate = Assert.Single(await recovery.ExpiredLeases.FindExpiredAsync(
                10,
                CancellationToken.None));
            var disposition = await new RecoverExpiredJobLeaseHandler(
                recovery.Jobs,
                recovery.Audits,
                recovery.UnitOfWork,
                new FixedClock(claimed.LeaseExpiresUtc))
                .HandleAsync(
                    new RecoverExpiredJobLeaseCommand(candidate),
                    CancellationToken.None);
            Assert.Equal(JobLeaseRecoveryDisposition.TimedOutExecution, disposition);
        }

        await using var verification = new PersistenceTestScope(database);
        var recovered = Assert.IsType<Job>(
            await verification.Jobs.GetByIdAsync(job.Id, CancellationToken.None));
        var execution = Assert.Single(recovered.Executions);
        Assert.Equal(JobStatus.TimedOut, recovered.Status);
        Assert.Equal(ExecutionOutcome.TimedOut, execution.Outcome);
        Assert.Null(recovered.Lease);
        Assert.False(await verification.Context.JobLeases.AnyAsync());
        Assert.True(await verification.Context.AuditEvents.AnyAsync(
            audit => audit.EventType == "JobLeaseRecovered" &&
                audit.EntityId == job.Id.ToString()));
        Assert.Throws<Domain.Exceptions.DomainValidationException>(
            () => recovered.RecordTerminalExecutionOutcome(
                claimed.Credentials,
                ExecutionOutcome.Succeeded,
                0,
                null,
                new UserIdentity("worker:stale"),
                claimed.LeaseExpiresUtc.AddSeconds(1)));
    }

    private static async Task ClaimDiscoveredAsync(
        SqlServerDatabase database,
        WorkerNodeId workerId,
        DateTimeOffset claimTime)
    {
        IReadOnlyList<JobQueueCandidate> candidates;
        await using (var discovery = new PersistenceTestScope(database))
        {
            candidates = await discovery.Candidates.FindCandidatesAsync(
                new HashSet<JobWorkKind> { JobWorkKind.Execute },
                100,
                claimTime,
                CancellationToken.None);
        }

        foreach (var candidate in candidates)
        {
            await using var attempt = new PersistenceTestScope(database);
            try
            {
                _ = await CreateAcquireHandler(attempt, claimTime).HandleAsync(
                    Command(candidate.JobId, workerId),
                    CancellationToken.None);
            }
            catch (ApplicationConflictException)
            {
            }
            catch (WindowsScriptRunner.Infrastructure.Persistence.PersistenceUnavailableException)
            {
            }
        }
    }

    private static async Task<(JobId JobId, ClaimedJobWork Work)> SeedClaimedJobAsync(
        SqlServerDatabase database)
    {
        var version = SqlServerTestData.Version();
        var script = SqlServerTestData.Script(version);
        var job = SqlServerTestData.ExecutionQueuedJob(script, version);
        var claimTime = job.UpdatedUtc.AddMinutes(1);
        var worker = Worker("worker-concurrency", claimTime);
        await SeedAsync(database, script, [job], [worker]);
        await using var acquisition = new PersistenceTestScope(database);
        var work = await CreateAcquireHandler(acquisition, claimTime).HandleAsync(
            Command(job.Id, worker.Id),
            CancellationToken.None);
        return (job.Id, work);
    }

    private static async Task SeedAsync(
        SqlServerDatabase database,
        WindowsScriptRunner.Domain.Scripts.ScriptDefinition script,
        IReadOnlyCollection<Job> jobs,
        IReadOnlyCollection<WorkerNode> workers)
    {
        await using var seed = new PersistenceTestScope(database);
        await seed.Scripts.AddAsync(script, CancellationToken.None);
        foreach (var job in jobs)
        {
            await seed.Jobs.AddAsync(job, CancellationToken.None);
        }

        foreach (var worker in workers)
        {
            await seed.Workers.AddAsync(worker, CancellationToken.None);
        }

        await seed.UnitOfWork.CommitAsync(CancellationToken.None);
    }

    private static WorkerNode Worker(string name, DateTimeOffset heartbeat)
    {
        var worker = new WorkerNode(WorkerNodeId.New(), name, SqlServerTestData.Time);
        worker.RecordHeartbeat(heartbeat);
        return worker;
    }

    private static AcquireJobLeaseHandler CreateAcquireHandler(
        PersistenceTestScope scope,
        DateTimeOffset clock,
        IUnitOfWork? unitOfWork = null) =>
        new(
            scope.Jobs,
            scope.Workers,
            scope.FencingTokens,
            scope.Audits,
            unitOfWork ?? scope.UnitOfWork,
            new FixedClock(clock));

    private static AcquireJobLeaseCommand Command(JobId jobId, WorkerNodeId workerId) =>
        new(
            jobId,
            JobWorkKind.Execute,
            workerId,
            TimeSpan.FromMinutes(2),
            TimeSpan.FromHours(1));

    private static async Task<(ClaimedJobWork? Work, Exception? Exception)> CaptureAsync(
        Func<Task<ClaimedJobWork>> action)
    {
        try
        {
            return (await action(), null);
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
    }

    private static async Task<(T? Result, Exception? Exception)> CaptureOperationAsync<T>(
        Func<Task<T>> action)
    {
        try
        {
            return (await action(), null);
        }
        catch (Exception exception)
        {
            return (default, exception);
        }
    }

    private static async Task<Exception?> CaptureExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

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

    private sealed class CoordinatedUnitOfWork(
        IUnitOfWork inner,
        CommitBarrier barrier) : IUnitOfWork
    {
        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await barrier.SignalAndWaitAsync(cancellationToken);
            await inner.CommitAsync(cancellationToken);
        }
    }

    private sealed class DelegatingUnitOfWork(
        Func<CancellationToken, Task> commit) : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken cancellationToken) =>
            commit(cancellationToken);
    }

    private sealed class RenewalBeforeTerminalCommit
    {
        private readonly TaskCompletionSource terminalReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource renewalCommitted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task CommitRenewalAsync(
            IUnitOfWork unitOfWork,
            CancellationToken cancellationToken)
        {
            await terminalReady.Task.WaitAsync(cancellationToken);
            try
            {
                await unitOfWork.CommitAsync(cancellationToken);
                renewalCommitted.TrySetResult();
            }
            catch (Exception exception)
            {
                renewalCommitted.TrySetException(exception);
                throw;
            }
        }

        public async Task CommitTerminalAsync(
            IUnitOfWork unitOfWork,
            CancellationToken cancellationToken)
        {
            terminalReady.TrySetResult();
            await renewalCommitted.Task.WaitAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
    }

    private sealed class CommitBarrier(int participantCount)
    {
        private readonly TaskCompletionSource ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public async Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrivals) == participantCount)
            {
                ready.TrySetResult();
            }

            await ready.Task.WaitAsync(cancellationToken);
        }
    }
}
