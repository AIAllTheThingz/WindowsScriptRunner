using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Reports;
using WindowsScriptRunner.Automation;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Reports;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Reporting;

namespace WindowsScriptRunner.UnitTests;

public sealed class Phase7ApplicationTests
{
    private static readonly DateTimeOffset Time =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompletionStagesTypedReportJobAndAuditInOneCommit()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Handler.HandleAsync(
            fixture.Command,
            CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(JobStatus.Completed, fixture.Job.Status);
        Assert.Null(fixture.Job.Lease);
        var report = Assert.IsType<JobReport>(fixture.Reports.Report);
        Assert.Equal(fixture.Job.Id, report.JobId);
        Assert.Equal(fixture.Credentials.LeaseId, report.LeaseId);
        Assert.Equal(fixture.Credentials.FencingToken, report.FencingToken);
        Assert.Equal("WORKER-01", report.Inventory.ComputerName);
        Assert.Equal(1, fixture.Jobs.UpdateCount);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
        var audit = Assert.Single(fixture.Audits.Events);
        Assert.Equal("LocalHostInventoryReportPersisted", audit.EventType);
        Assert.DoesNotContain(
            audit.Properties.Values.Append(audit.Summary),
            value => value.Contains("WORKER-01", StringComparison.Ordinal) ||
                value.Contains("Microsoft Windows", StringComparison.Ordinal) ||
                value.Contains("10.0.26100", StringComparison.Ordinal) ||
                value.Contains("7.4.0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExactReplayIsIdempotentWithoutASecondCommitOrAudit()
    {
        var fixture = Fixture.Create();
        var first = await fixture.Handler.HandleAsync(
            fixture.Command,
            CancellationToken.None);

        var replay = await fixture.Handler.HandleAsync(
            fixture.Command,
            CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(replay.Created);
        Assert.Equal(first.ReportId, replay.ReportId);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
        Assert.Single(fixture.Audits.Events);
        Assert.Equal(1, fixture.Reports.AddCount);
    }

    [Fact]
    public async Task ExactReplayRemainsIdempotentAfterDefinitionIsDisabled()
    {
        var fixture = Fixture.Create();
        var first = await fixture.Handler.HandleAsync(
            fixture.Command,
            CancellationToken.None);
        fixture.Scripts.Definition.Disable(Time.AddMinutes(1));

        var replay = await fixture.Handler.HandleAsync(
            fixture.Command,
            CancellationToken.None);

        Assert.False(replay.Created);
        Assert.Equal(first.ReportId, replay.ReportId);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
        Assert.Single(fixture.Audits.Events);
        Assert.Equal(1, fixture.Reports.AddCount);
    }

    [Fact]
    public async Task ConflictingReplayFailsClosed()
    {
        var fixture = Fixture.Create();
        _ = await fixture.Handler.HandleAsync(
            fixture.Command,
            CancellationToken.None);
        var conflicting = fixture.Command with
        {
            Inventory = Parse(fixture.Job.Id, computerName: "WORKER-02"),
        };

        await Assert.ThrowsAsync<ApplicationConflictException>(
            () => fixture.Handler.HandleAsync(
                conflicting,
                CancellationToken.None));

        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
        Assert.Single(fixture.Audits.Events);
        Assert.Equal("WORKER-01", fixture.Reports.Report!.Inventory.ComputerName);
    }

    [Fact]
    public async Task StaleLeaseCausesNoReportMutationAuditOrCommit()
    {
        var fixture = Fixture.Create();
        var stale = fixture.Command with
        {
            Credentials = new JobLeaseCredentials(
                JobLeaseId.New(),
                fixture.Credentials.WorkerNodeId,
                fixture.Credentials.FencingToken),
        };

        await Assert.ThrowsAsync<ApplicationConflictException>(
            () => fixture.Handler.HandleAsync(stale, CancellationToken.None));

        Assert.Equal(JobStatus.DryRunRunning, fixture.Job.Status);
        Assert.NotNull(fixture.Job.Lease);
        Assert.Null(fixture.Reports.Report);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.Jobs.UpdateCount);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task UnsupportedPackageCannotCreateAReport()
    {
        var fixture = Fixture.Create();
        fixture.Scripts.Definition = CreateUnsupportedDefinition(
            fixture.Job.ScriptDefinitionId,
            fixture.Job.ScriptVersionId);

        await Assert.ThrowsAsync<ApplicationConflictException>(
            () => fixture.Handler.HandleAsync(
                fixture.Command,
                CancellationToken.None));

        Assert.Null(fixture.Reports.Report);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task TypedQueriesReturnOnlyTheDedicatedResponse()
    {
        var fixture = Fixture.Create();
        var completion = await fixture.Handler.HandleAsync(
            fixture.Command,
            CancellationToken.None);
        var queryHandler = new GetLocalHostInventoryReportHandler(
            fixture.Reports);

        var byId = await queryHandler.HandleAsync(
            new GetLocalHostInventoryReportByIdQuery(completion.ReportId),
            CancellationToken.None);
        var byJob = await queryHandler.HandleAsync(
            new GetLocalHostInventoryReportByJobIdQuery(fixture.Job.Id),
            CancellationToken.None);

        Assert.Equal(byId, byJob);
        Assert.Equal("WORKER-01", byId.ComputerName);
        Assert.Equal("LocalHostInventory", byId.ReportType);
        Assert.Equal("Json", byId.Format);
        Assert.DoesNotContain(
            byId.GetType().GetProperties(),
            property => property.Name.Contains(
                "Standard",
                StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains(
                    "Json",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TypedReportListIsBoundedAndExposesOnlyLocalHostInventoryResponses()
    {
        var fixture = Fixture.Create();
        _ = await fixture.Handler.HandleAsync(fixture.Command, CancellationToken.None);
        var listHandler = new ListLocalHostInventoryReportsHandler(fixture.Reports);

        var reports = await listHandler.HandleAsync(
            new ListLocalHostInventoryReportsQuery(1),
            CancellationToken.None);

        var report = Assert.Single(reports);
        Assert.Equal(fixture.Job.Id.Value, report.JobId);
        Assert.Equal("LocalHostInventory", report.ReportType);
        Assert.Equal(1, fixture.Reports.ListCallCount);
        Assert.Equal(1, fixture.Reports.LastListMaximumCount);
        Assert.DoesNotContain(
            report.GetType().GetProperties(),
            property => property.Name.Contains("Standard", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Raw", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Json", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task TypedReportListRejectsOutOfRangeBoundsBeforeRepositoryAccess(int maximumCount)
    {
        var fixture = Fixture.Create();
        var listHandler = new ListLocalHostInventoryReportsHandler(fixture.Reports);

        await Assert.ThrowsAsync<ApplicationValidationException>(
            () => listHandler.HandleAsync(
                new ListLocalHostInventoryReportsQuery(maximumCount),
                CancellationToken.None));

        Assert.Equal(0, fixture.Reports.ListCallCount);
    }

    [Fact]
    public async Task CancellationPropagatesBeforeMutation()
    {
        var fixture = Fixture.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Handler.HandleAsync(
                fixture.Command,
                cancellation.Token));

        Assert.Null(fixture.Reports.Report);
        Assert.Empty(fixture.Audits.Events);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    private static ValidatedLocalHostInventoryReport Parse(
        JobId jobId,
        string computerName = "WORKER-01")
    {
        var json =
            $$"""
            {"schemaVersion":"1.0","computerName":"{{computerName}}","os":{"description":"Microsoft Windows 11","version":"10.0.26100","architecture":"X64"},"powerShell":{"version":"7.4.0"},"collectedUtc":"2026-07-30T12:00:00.5000000Z"}
            """;
        return new LocalHostInventoryReportParser().Parse(
            new LocalHostInventoryProcessResult(
                jobId.Value,
                Time,
                Time.AddSeconds(1),
                0,
                json,
                string.Empty,
                standardOutputTruncated: false,
                standardErrorTruncated: false,
                exited: true));
    }

    private static ScriptDefinition CreateUnsupportedDefinition(
        ScriptDefinitionId definitionId,
        ScriptVersionId versionId)
    {
        var definition = ScriptDefinition.Create(
            definitionId,
            new ScriptName("unsupported.package"),
            "Unsupported",
            "Unsupported package",
            RiskLevel.ReadOnly,
            new UserIdentity("system:test"),
            Time);
        var version = new ScriptVersion(
            versionId,
            ScriptVersionNumber.Parse("1.0.0"),
            LocalHostInventoryPackageMetadata.RelativeScriptPath,
            LocalHostInventoryPackageMetadata.Sha256,
            null,
            "7.4.0",
            1,
            [ExecutionPhase.DryRun],
            [ReportFormat.Json],
            Time,
            new UserIdentity("system:test"));
        version.Publish();
        definition.AddVersion(version, Time);
        return definition;
    }

    private sealed class Fixture
    {
        private Fixture(
            Job job,
            JobLeaseCredentials credentials,
            FakeJobRepository jobs,
            FakeScriptRepository scripts,
            FakeReportRepository reports,
            FakeAuditWriter audits,
            FakeUnitOfWork unitOfWork,
            CompleteLocalHostInventoryDryRunHandler handler,
            CompleteLocalHostInventoryDryRunCommand command)
        {
            Job = job;
            Credentials = credentials;
            Jobs = jobs;
            Scripts = scripts;
            Reports = reports;
            Audits = audits;
            UnitOfWork = unitOfWork;
            Handler = handler;
            Command = command;
        }

        internal Job Job { get; }
        internal JobLeaseCredentials Credentials { get; }
        internal FakeJobRepository Jobs { get; }
        internal FakeScriptRepository Scripts { get; }
        internal FakeReportRepository Reports { get; }
        internal FakeAuditWriter Audits { get; }
        internal FakeUnitOfWork UnitOfWork { get; }
        internal CompleteLocalHostInventoryDryRunHandler Handler { get; }
        internal CompleteLocalHostInventoryDryRunCommand Command { get; }

        internal static Fixture Create()
        {
            var definition =
                LocalHostInventoryPackageMetadata.CreateDefinition(Time);
            var version = Assert.Single(definition.Versions);
            var requester = new UserIdentity("DOMAIN\\requester");
            var job = Job.CreateDraft(
                JobId.New(),
                definition.Id,
                version.Id,
                ExecutionPhase.DryRun,
                requester,
                Time);
            job.AddTarget(new TargetName("local-worker"), requester, Time);
            job.Submit(definition, requester, Time);
            job.MarkValidated(requester, Time);
            job.QueueDryRun(requester, Time);
            var workerId = WorkerNodeId.New();
            var actor = new UserIdentity($"worker:{workerId}");
            var credentials = job.AcquireWorkLease(
                JobLeaseId.New(),
                workerId,
                JobWorkKind.DryRun,
                17,
                actor,
                Time,
                Time.AddMinutes(5)).Credentials;
            job.StartDryRun(credentials, actor, Time);

            var jobs = new FakeJobRepository(job);
            var scripts = new FakeScriptRepository(definition);
            var reports = new FakeReportRepository();
            var audits = new FakeAuditWriter();
            var unitOfWork = new FakeUnitOfWork();
            var handler = new CompleteLocalHostInventoryDryRunHandler(
                jobs,
                scripts,
                reports,
                audits,
                unitOfWork,
                new FixedClock(Time.AddSeconds(2)));
            var command = new CompleteLocalHostInventoryDryRunCommand(
                job.Id,
                credentials,
                Parse(job.Id),
                actor);
            return new Fixture(
                job,
                credentials,
                jobs,
                scripts,
                reports,
                audits,
                unitOfWork,
                handler,
                command);
        }
    }

    private sealed class FakeJobRepository(Job job) : IJobRepository
    {
        internal int UpdateCount { get; private set; }

        public Task<Job?> GetByIdAsync(
            JobId id,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(job.Id == id ? job : null);
        }

        public Task<IReadOnlyList<Job>> ListAwaitingApprovalAsync(
            int maximumCount,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Job>>([]);

        public Task<bool> ExistsAsync(
            JobId id,
            CancellationToken cancellationToken) =>
            Task.FromResult(job.Id == id);

        public Task AddAsync(
            Job value,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateAsync(
            Job value,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Same(job, value);
            UpdateCount++;
            return Task.CompletedTask;
        }

        public Task UpdateLeaseAsync(
            Job value,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> TryRefreshLeaseAsync(
            JobId jobId,
            JobLeaseCredentials credentials,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class FakeScriptRepository(ScriptDefinition definition) :
        IScriptDefinitionRepository
    {
        internal ScriptDefinition Definition { get; set; } = definition;

        public Task<ScriptDefinition?> GetByIdAsync(
            ScriptDefinitionId id,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                Definition.Id == id ? Definition : null);
        }

        public Task AddAsync(
            ScriptDefinition value,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateAsync(
            ScriptDefinition value,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeReportRepository : IJobReportRepository
    {
        internal JobReport? Report { get; private set; }
        internal int AddCount { get; private set; }
        internal int ListCallCount { get; private set; }
        internal int? LastListMaximumCount { get; private set; }

        public Task<JobReport?> GetByIdAsync(
            JobReportId id,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Report?.Id == id ? Report : null);
        }

        public Task<JobReport?> GetByJobIdAsync(
            JobId jobId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Report?.JobId == jobId ? Report : null);
        }

        public Task<IReadOnlyList<JobReport>> ListLocalHostInventoryAsync(
            int maximumCount,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ListCallCount++;
            LastListMaximumCount = maximumCount;
            IReadOnlyList<JobReport> reports = Report is null ? [] : [Report];
            return Task.FromResult(reports);
        }

        public Task AddAsync(
            JobReport report,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Report is not null)
            {
                throw new ApplicationConflictException(
                    "A report already exists.");
            }

            AddCount++;
            Report = report;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuditWriter : IAuditWriter
    {
        internal List<AuditEvent> Events { get; } = [];

        public Task WriteAsync(
            AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        internal int CommitCount { get; private set; }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommitCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset time) :
        IWorkerCoordinationClock
    {
        public Task<DateTimeOffset> GetUtcNowAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(time);
        }
    }
}
