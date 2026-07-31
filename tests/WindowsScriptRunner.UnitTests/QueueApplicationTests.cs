using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Application.Workers;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.Workers;

namespace WindowsScriptRunner.UnitTests;

public sealed class QueueApplicationTests
{
    [Fact]
    public async Task RegistrationCreatesWorkerSynchronizesCapabilitiesHeartbeatsAndAuditsAtomically()
    {
        var fixture = new QueueFixture();
        var workerId = WorkerNodeId.New();
        var capabilities = new[]
        {
            new WorkerCapability("OS", "Windows"),
            new WorkerCapability("Role", "General"),
        };

        var result = await fixture.Register.HandleAsync(
            new RegisterWorkerCommand(workerId, "worker-01", capabilities),
            CancellationToken.None);

        Assert.True(result.Created);
        Assert.True(result.CapabilitiesChanged);
        Assert.Equal(fixture.Clock.UtcNow, result.HeartbeatUtc);
        Assert.Equal(fixture.Clock.UtcNow, fixture.Workers.Worker!.LastHeartbeatUtc);
        Assert.Equal(2, fixture.Workers.Worker.Capabilities.Count);
        Assert.Equal("WorkerRegistered", Assert.Single(fixture.Audits.Events).EventType);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task MatchingRegistrationIsIdempotentAndCapabilityChangesReplaceCompleteSet()
    {
        var fixture = new QueueFixture();
        var workerId = WorkerNodeId.New();
        fixture.Workers.Worker = LiveWorker(workerId, fixture.Clock.UtcNow);
        fixture.Workers.Worker.RegisterCapability(new WorkerCapability("Old", "Value"));

        var result = await fixture.Register.HandleAsync(
            new RegisterWorkerCommand(
                workerId,
                "worker-01",
                [new WorkerCapability("OS", "Windows")]),
            CancellationToken.None);

        Assert.False(result.Created);
        Assert.True(result.CapabilitiesChanged);
        var capability = Assert.Single(fixture.Workers.Worker.Capabilities);
        Assert.Equal("OS", capability.Name);
        Assert.Equal("WorkerCapabilitiesSynchronized", Assert.Single(fixture.Audits.Events).EventType);

        fixture.Audits.Events.Clear();
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(1);
        var restart = await fixture.Register.HandleAsync(
            new RegisterWorkerCommand(
                workerId,
                "worker-01",
                [new WorkerCapability("OS", "Windows")]),
            CancellationToken.None);

        Assert.False(restart.CapabilitiesChanged);
        Assert.Empty(fixture.Audits.Events);
    }

    [Fact]
    public async Task RegistrationRejectsNameMismatchDisabledWorkerAndDuplicateCapabilities()
    {
        var fixture = new QueueFixture();
        var workerId = WorkerNodeId.New();
        fixture.Workers.Worker = LiveWorker(workerId, fixture.Clock.UtcNow);

        await Assert.ThrowsAsync<ApplicationConflictException>(
            () => fixture.Register.HandleAsync(
                new RegisterWorkerCommand(workerId, "different", []),
                CancellationToken.None));

        fixture.Workers.Worker.Disable();
        await Assert.ThrowsAsync<ApplicationValidationException>(
            () => fixture.Register.HandleAsync(
                new RegisterWorkerCommand(workerId, "worker-01", []),
                CancellationToken.None));

        fixture.Workers.Worker = null;
        await Assert.ThrowsAsync<Domain.Exceptions.DomainValidationException>(
            () => fixture.Register.HandleAsync(
                new RegisterWorkerCommand(
                    WorkerNodeId.New(),
                    "worker-02",
                    [
                        new WorkerCapability("OS", "Windows"),
                        new WorkerCapability("os", "Linux"),
                    ]),
                CancellationToken.None));
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
        Assert.Empty(fixture.Audits.Events);
    }

    [Fact]
    public async Task HeartbeatPersistsWithoutAuditAndRejectsMissingOrDisabledWorker()
    {
        var fixture = new QueueFixture();
        var workerId = WorkerNodeId.New();
        fixture.Workers.Worker = LiveWorker(workerId, fixture.Clock.UtcNow.AddMinutes(-1));

        var heartbeat = await fixture.Heartbeat.HandleAsync(
            new RecordWorkerHeartbeatCommand(workerId),
            CancellationToken.None);

        Assert.Equal(fixture.Clock.UtcNow, heartbeat);
        Assert.Equal(1, fixture.Workers.UpdateCount);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
        Assert.Empty(fixture.Audits.Events);

        fixture.Workers.Worker.Disable();
        await Assert.ThrowsAsync<ApplicationValidationException>(
            () => fixture.Heartbeat.HandleAsync(
                new RecordWorkerHeartbeatCommand(workerId),
                CancellationToken.None));
        fixture.Workers.Worker = null;
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => fixture.Heartbeat.HandleAsync(
                new RecordWorkerHeartbeatCommand(workerId),
                CancellationToken.None));
    }

    [Fact]
    public async Task AcquisitionRequiresLiveWorkerAndCommitsLeaseWithFencingAudit()
    {
        var fixture = QueueFixture.WithExecutionQueuedJob();
        var workerId = WorkerNodeId.New();
        fixture.Workers.Worker = LiveWorker(workerId, fixture.Clock.UtcNow);
        fixture.Fencing.Next = 42;

        var work = await fixture.Acquire.HandleAsync(
            new AcquireJobLeaseCommand(
                fixture.Jobs.Job!.Id,
                JobWorkKind.Execute,
                fixture.Jobs.Job.ScriptVersionId,
                workerId,
                TimeSpan.FromMinutes(2),
                TimeSpan.FromMinutes(1)),
            CancellationToken.None);

        Assert.Equal(JobStatus.Claimed, fixture.Jobs.Job.Status);
        Assert.Equal(42, work.FencingToken);
        Assert.Equal(workerId, work.WorkerNodeId);
        Assert.Equal(JobWorkKind.Execute, work.WorkKind);
        var audit = Assert.Single(fixture.Audits.Events);
        Assert.Equal("JobLeaseAcquired", audit.EventType);
        Assert.Equal("42", audit.Properties["FencingToken"]);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task AcquisitionRejectsStaleWorkerBeforeRequestingFencingToken()
    {
        var fixture = QueueFixture.WithExecutionQueuedJob();
        var workerId = WorkerNodeId.New();
        fixture.Workers.Worker = LiveWorker(
            workerId,
            fixture.Clock.UtcNow.AddMinutes(-5));

        await Assert.ThrowsAsync<ApplicationValidationException>(
            () => fixture.Acquire.HandleAsync(
                new AcquireJobLeaseCommand(
                    fixture.Jobs.Job!.Id,
                    JobWorkKind.Execute,
                    fixture.Jobs.Job.ScriptVersionId,
                    workerId,
                    TimeSpan.FromMinutes(2),
                    TimeSpan.FromMinutes(1)),
                CancellationToken.None));

        Assert.Equal(0, fixture.Fencing.CallCount);
        Assert.Equal(JobStatus.ExecutionQueued, fixture.Jobs.Job!.Status);
        Assert.Null(fixture.Jobs.Job.Lease);
        Assert.Empty(fixture.Audits.Events);
    }

    [Fact]
    public async Task RenewalExtendsLeaseWithoutAudit()
    {
        var fixture = QueueFixture.WithExecutionQueuedJob();
        var credentials = fixture.AcquireDirect(JobWorkKind.Execute, 10);
        var oldExpiration = fixture.Jobs.Job!.Lease!.ExpiresUtc;
        fixture.Clock.UtcNow = oldExpiration.AddMinutes(-1);

        var expiration = await fixture.Renew.HandleAsync(
            new RenewJobLeaseCommand(
                fixture.Jobs.Job.Id,
                credentials,
                TimeSpan.FromMinutes(2)),
            CancellationToken.None);

        Assert.Equal(fixture.Clock.UtcNow.AddMinutes(2), expiration);
        Assert.Equal(expiration, fixture.Jobs.Job.Lease!.ExpiresUtc);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Equal(1, fixture.Jobs.LeaseUpdateCount);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task ReleaseRequeuesExecutionAndWritesBoundedAudit()
    {
        const string parameterValue = "audit-parameter-sentinel";
        var parameter = TestDomainFactory.Parameter();
        var fixture = QueueFixture.WithExecutionQueuedJob([(parameter, parameterValue)]);
        var credentials = fixture.AcquireDirect(JobWorkKind.Execute, 10);
        fixture.Clock.UtcNow = fixture.Jobs.Job!.UpdatedUtc.AddSeconds(1);

        await fixture.Release.HandleAsync(
            new ReleaseUnstartedJobLeaseCommand(fixture.Jobs.Job.Id, credentials),
            CancellationToken.None);

        Assert.Equal(JobStatus.ExecutionQueued, fixture.Jobs.Job.Status);
        Assert.Null(fixture.Jobs.Job.Lease);
        var audit = Assert.Single(fixture.Audits.Events);
        Assert.Equal("JobLeaseReleased", audit.EventType);
        Assert.Single(fixture.Jobs.Job.Parameters);
        Assert.DoesNotContain(parameterValue, audit.Properties.Values);
        Assert.DoesNotContain(
            fixture.Jobs.Job.Parameters.Select(parameter => parameter.SerializedValue),
            value => audit.Properties.Values.Contains(value));
    }

    [Fact]
    public async Task RecoveryUsesCandidateCredentialsAndWritesExpiredAndRecoveredAudits()
    {
        var fixture = QueueFixture.WithExecutionQueuedJob();
        var credentials = fixture.AcquireDirect(JobWorkKind.Execute, 10);
        var expiration = fixture.Jobs.Job!.Lease!.ExpiresUtc;
        fixture.Clock.UtcNow = expiration;

        var disposition = await fixture.Recover.HandleAsync(
            new RecoverExpiredJobLeaseCommand(
                new ExpiredJobLeaseCandidate(
                    fixture.Jobs.Job.Id,
                    credentials,
                    expiration)),
            CancellationToken.None);

        Assert.Equal(JobLeaseRecoveryDisposition.RequeuedUnstartedExecution, disposition);
        Assert.Equal(JobStatus.ExecutionQueued, fixture.Jobs.Job.Status);
        Assert.Null(fixture.Jobs.Job.Lease);
        Assert.Equal(
            ["JobLeaseExpired", "JobLeaseRecovered"],
            fixture.Audits.Events.Select(item => item.EventType));
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task QueueHandlersPropagateCancellation()
    {
        var fixture = QueueFixture.WithExecutionQueuedJob();
        var workerId = WorkerNodeId.New();
        fixture.Workers.Worker = LiveWorker(workerId, fixture.Clock.UtcNow);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Acquire.HandleAsync(
                new AcquireJobLeaseCommand(
                    fixture.Jobs.Job!.Id,
                    JobWorkKind.Execute,
                    fixture.Jobs.Job.ScriptVersionId,
                    workerId,
                    TimeSpan.FromMinutes(2),
                    TimeSpan.FromMinutes(1)),
                source.Token));
        Assert.Contains(source.Token, fixture.Jobs.ObservedTokens);
    }

    private static WorkerNode LiveWorker(WorkerNodeId id, DateTimeOffset heartbeat)
    {
        var worker = new WorkerNode(id, "worker-01", TestDomainFactory.Time);
        worker.RecordHeartbeat(heartbeat);
        return worker;
    }

    private sealed class QueueFixture
    {
        public QueueFixture()
        {
            Register = new RegisterWorkerHandler(Workers, Audits, UnitOfWork, Clock);
            Heartbeat = new RecordWorkerHeartbeatHandler(Workers, UnitOfWork, Clock);
            Acquire = new AcquireJobLeaseHandler(
                Jobs,
                Workers,
                Fencing,
                Audits,
                UnitOfWork,
                Clock);
            Renew = new RenewJobLeaseHandler(Jobs, UnitOfWork, Clock);
            Release = new ReleaseUnstartedJobLeaseHandler(
                Jobs,
                Audits,
                UnitOfWork,
                Clock);
            Recover = new RecoverExpiredJobLeaseHandler(
                Jobs,
                Audits,
                UnitOfWork,
                Clock);
        }

        public TestClock Clock { get; } = new(TestDomainFactory.Time.AddHours(1));
        public FakeJobRepository Jobs { get; } = new();
        public FakeWorkerRepository Workers { get; } = new();
        public FakeAuditWriter Audits { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public FakeFencingTokenSource Fencing { get; } = new();
        public RegisterWorkerHandler Register { get; }
        public RecordWorkerHeartbeatHandler Heartbeat { get; }
        public AcquireJobLeaseHandler Acquire { get; }
        public RenewJobLeaseHandler Renew { get; }
        public ReleaseUnstartedJobLeaseHandler Release { get; }
        public RecoverExpiredJobLeaseHandler Recover { get; }

        public static QueueFixture WithExecutionQueuedJob(
            IReadOnlyCollection<(ScriptParameterDefinition Definition, string? Value)>? parameters = null)
        {
            var fixture = new QueueFixture();
            var version = TestDomainFactory.Version(
                parameters?.Select(parameter => parameter.Definition));
            var script = TestDomainFactory.Script(version);
            var job = TestDomainFactory.SubmittedJob(
                script,
                version,
                parameters,
                requestedPhase: ExecutionPhase.Execute);
            TestDomainFactory.AdvanceToAwaitingApproval(job);
            job.RecordApproval(
                TestDomainFactory.OtherUser,
                TestDomainFactory.Fingerprint,
                null,
                job.UpdatedUtc.AddMinutes(1));
            job.QueueExecution(
                TestDomainFactory.OtherUser,
                job.UpdatedUtc.AddMinutes(1));
            fixture.Jobs.Job = job;
            return fixture;
        }

        public JobLeaseCredentials AcquireDirect(JobWorkKind workKind, long fencingToken)
        {
            var workerId = WorkerNodeId.New();
            var acquired = Jobs.Job!.UpdatedUtc.AddSeconds(1);
            return Jobs.Job.AcquireWorkLease(
                JobLeaseId.New(),
                workerId,
                workKind,
                fencingToken,
                TestDomainFactory.OtherUser,
                acquired,
                acquired.AddMinutes(2)).Credentials;
        }
    }

    private sealed class TestClock(DateTimeOffset utcNow) :
        IClock,
        IWorkerCoordinationClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public Task<DateTimeOffset> GetUtcNowAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(UtcNow);
        }
    }

    private sealed class FakeJobRepository : IJobRepository
    {
        public Job? Job { get; set; }
        public int UpdateCount { get; private set; }
        public int LeaseUpdateCount { get; private set; }
        public List<CancellationToken> ObservedTokens { get; } = [];

        public Task<Job?> GetByIdAsync(JobId id, CancellationToken cancellationToken)
        {
            ObservedTokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Job?.Id == id ? Job : null);
        }

        public Task<IReadOnlyList<Job>> ListAwaitingApprovalAsync(
            int maximumCount,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Job>>([]);

        public Task<bool> ExistsAsync(JobId id, CancellationToken cancellationToken) =>
            Task.FromResult(Job?.Id == id);

        public Task AddAsync(Job job, CancellationToken cancellationToken)
        {
            Job = job;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Job job, CancellationToken cancellationToken)
        {
            UpdateCount++;
            Job = job;
            return Task.CompletedTask;
        }

        public Task UpdateLeaseAsync(Job job, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LeaseUpdateCount++;
            Job = job;
            return Task.CompletedTask;
        }

        public Task<bool> TryRefreshLeaseAsync(
            JobId jobId,
            JobLeaseCredentials credentials,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class FakeWorkerRepository : IWorkerNodeRepository
    {
        public WorkerNode? Worker { get; set; }
        public int UpdateCount { get; private set; }

        public Task<WorkerNode?> GetByIdAsync(
            WorkerNodeId id,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Worker?.Id == id ? Worker : null);
        }

        public Task AddAsync(WorkerNode workerNode, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Worker = workerNode;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(WorkerNode workerNode, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdateCount++;
            Worker = workerNode;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuditWriter : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = [];

        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int CommitCount { get; private set; }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommitCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFencingTokenSource : IFencingTokenSource
    {
        public long Next { get; set; } = 1;
        public int CallCount { get; private set; }

        public Task<long> GetNextAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(Next);
        }
    }
}
