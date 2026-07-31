using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Reports;
using WindowsScriptRunner.Automation;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Reports;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Infrastructure.Persistence;
using WindowsScriptRunner.Infrastructure.Persistence.Entities;
using WindowsScriptRunner.Reporting;

namespace WindowsScriptRunner.SqlServerTests;

public sealed class Phase7ReportPersistenceTests
{
    private const string Phase6Migration =
        "20260729224310_AddWorkerQueueLeases";
    private const string Phase7Migration =
        "20260730221709_AddDurableLocalHostInventoryReports";

    [Fact]
    public async Task Phase7MigrationUpgradesAndRollsBackThePhase6Schema()
    {
        await using var database = await SqlServerDatabase.CreateAsync(
            applyMigrations: false);
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(Phase6Migration);
        Assert.Equal(
            2,
            (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(0, await ReportTableCountAsync(context));

        await migrator.MigrateAsync(Phase7Migration);
        Assert.Equal(
            3,
            (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(2, await ReportTableCountAsync(context));

        await migrator.MigrateAsync(Phase6Migration);
        Assert.Equal(
            2,
            (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(0, await ReportTableCountAsync(context));

        await migrator.MigrateAsync();
        Assert.Equal(2, await ReportTableCountAsync(context));
    }

    [Fact]
    public async Task AtomicCompletionRoundTripsTypedReportAndExactReplay()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var running = await SeedRunningJobAsync(database);
        LocalHostInventoryReportCompletion completion;
        await using (var scope = new PersistenceTestScope(database))
        {
            completion = await Handler(scope).HandleAsync(
                running.Command,
                CancellationToken.None);
        }

        Assert.True(completion.Created);
        await using (var verification = new PersistenceTestScope(database))
        {
            var response = await new GetLocalHostInventoryReportHandler(
                verification.Reports).HandleAsync(
                    new GetLocalHostInventoryReportByJobIdQuery(running.JobId),
                    CancellationToken.None);
            var job = Assert.IsType<Job>(
                await verification.Jobs.GetByIdAsync(
                    running.JobId,
                    CancellationToken.None));

            Assert.Equal(completion.ReportId.Value, response.ReportId);
            Assert.Equal("WORKER-01", response.ComputerName);
            Assert.Equal("Microsoft Windows 11", response.OsDescription);
            Assert.Equal("10.0.26100", response.OsVersion);
            Assert.Equal("X64", response.OsArchitecture);
            Assert.Equal("7.4.0", response.PowerShellVersion);
            Assert.Equal(JobStatus.Completed, job.Status);
            Assert.Null(job.Lease);
            Assert.Equal(
                0,
                await RawOutputColumnCountAsync(verification.Context));
            Assert.Equal(
                0,
                await SensitiveAuditValueCountAsync(
                    verification.Context,
                    "WORKER-01"));
        }

        await using (var replayScope = new PersistenceTestScope(database))
        {
            var replay = await Handler(replayScope).HandleAsync(
                running.Command,
                CancellationToken.None);
            Assert.False(replay.Created);
            Assert.Equal(completion.ReportId, replay.ReportId);
        }

        await using (var conflictScope = new PersistenceTestScope(database))
        {
            var conflict = running.Command with
            {
                Inventory = Parse(
                    running.JobId,
                    running.ProcessStartedUtc,
                    "WORKER-02"),
            };
            await Assert.ThrowsAsync<ApplicationConflictException>(
                () => Handler(conflictScope).HandleAsync(
                    conflict,
                    CancellationToken.None));
        }
    }

    [Fact]
    public async Task TypedReportListIsBoundedAndReturnsOnlyThePersistedInventoryReport()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var running = await SeedRunningJobAsync(database);
        await using (var completion = new PersistenceTestScope(database))
        {
            _ = await Handler(completion).HandleAsync(
                running.Command,
                CancellationToken.None);
        }

        await using var verification = new PersistenceTestScope(database);
        var reports = await verification.Reports.ListLocalHostInventoryAsync(
            1,
            CancellationToken.None);

        var report = Assert.Single(reports);
        Assert.Equal(running.JobId, report.JobId);
        Assert.Equal(JobReportType.LocalHostInventory, report.ReportType);
        Assert.Equal(ReportFormat.Json, report.Format);
        var requesterReports = await verification.Reports.ListLocalHostInventoryForRequesterAsync(
            new UserIdentity("DOMAIN\\phase7-sql"),
            1,
            CancellationToken.None);
        var otherRequesterReports = await verification.Reports.ListLocalHostInventoryForRequesterAsync(
            new UserIdentity("DOMAIN\\different-requester"),
            1,
            CancellationToken.None);

        Assert.Single(requesterReports);
        Assert.Empty(otherRequesterReports);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => verification.Reports.ListLocalHostInventoryAsync(
                0,
                CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => verification.Reports.ListLocalHostInventoryAsync(
                101,
                CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => verification.Reports.ListLocalHostInventoryForRequesterAsync(
                new UserIdentity("DOMAIN\\phase7-sql"),
                101,
                CancellationToken.None));
    }

    [Fact]
    public async Task ConstraintFailureRollsBackReportJobLeaseAndAuditTogether()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var running = await SeedRunningJobAsync(database);
        await using (var scope = new PersistenceTestScope(database))
        {
            var corruptingUnitOfWork = new CorruptingUnitOfWork(
                scope.Context,
                scope.UnitOfWork);
            var handler = new CompleteLocalHostInventoryDryRunHandler(
                scope.Jobs,
                scope.Scripts,
                scope.Reports,
                scope.Audits,
                corruptingUnitOfWork,
                scope.CoordinationClock);

            await Assert.ThrowsAsync<ApplicationValidationException>(
                () => handler.HandleAsync(
                    running.Command,
                    CancellationToken.None));
        }

        await using var verification = new PersistenceTestScope(database);
        var job = Assert.IsType<Job>(
            await verification.Jobs.GetByIdAsync(
                running.JobId,
                CancellationToken.None));
        Assert.Equal(JobStatus.DryRunRunning, job.Status);
        Assert.NotNull(job.Lease);
        Assert.Null(
            await verification.Reports.GetByJobIdAsync(
                running.JobId,
                CancellationToken.None));
        Assert.Equal(
            0,
            await verification.Context.AuditEvents
                .CountAsync(
                    audit =>
                        audit.EventType ==
                        "LocalHostInventoryReportPersisted"));
    }

    [Fact]
    public async Task ConcurrentDuplicateInsertsProduceOneDurableReport()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var running = await SeedRunningJobAsync(database);
        var report = CreateReport(running);

        var results = await Task.WhenAll(
            InsertReportAsync(database, report),
            InsertReportAsync(database, report));

        Assert.Single(results, exception => exception is null);
        Assert.Single(
            results,
            exception => exception is ApplicationConflictException);
        await using var verification = new PersistenceTestScope(database);
        Assert.Equal(
            1,
            await verification.Context.JobReports.CountAsync());
        Assert.Equal(
            1,
            await verification.Context.LocalHostInventoryReports.CountAsync());
    }

    [Fact]
    public async Task MissingTypedDetailFailsClosedAndCancellationPropagates()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var running = await SeedRunningJobAsync(database);
        await using (var completion = new PersistenceTestScope(database))
        {
            _ = await Handler(completion).HandleAsync(
                running.Command,
                CancellationToken.None);
        }

        await using (var corruption = database.CreateContext())
        {
            await corruption.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM [wsr].[LocalHostInventoryReports]
                WHERE [ReportId] = {JobReport.CreateDeterministicId(running.JobId).Value}
                """);
        }

        await using var verification = new PersistenceTestScope(database);
        await Assert.ThrowsAsync<PersistenceOperationException>(
            () => verification.Reports.GetByJobIdAsync(
                running.JobId,
                CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => verification.Reports.GetByJobIdAsync(
                running.JobId,
                cancellation.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistedDigestOrTypedContentMismatchFailsClosed(
        bool corruptDigest)
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var running = await SeedRunningJobAsync(database);
        await using (var completion = new PersistenceTestScope(database))
        {
            _ = await Handler(completion).HandleAsync(
                running.Command,
                CancellationToken.None);
        }

        await using (var corruption = database.CreateContext())
        {
            if (corruptDigest)
            {
                await corruption.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE [wsr].[JobReports]
                    SET [Sha256] = REPLICATE('b', 64)
                    """);
            }
            else
            {
                await corruption.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE [wsr].[LocalHostInventoryReports]
                    SET [ComputerName] = 'WORKER-02'
                    """);
            }
        }

        await using var verification = new PersistenceTestScope(database);
        await Assert.ThrowsAsync<PersistenceOperationException>(
            () => verification.Reports.GetByJobIdAsync(
                running.JobId,
                CancellationToken.None));
    }

    private static CompleteLocalHostInventoryDryRunHandler Handler(
        PersistenceTestScope scope) =>
        new(
            scope.Jobs,
            scope.Scripts,
            scope.Reports,
            scope.Audits,
            scope.UnitOfWork,
            scope.CoordinationClock);

    private static async Task<RunningJob> SeedRunningJobAsync(
        SqlServerDatabase database)
    {
        JobId jobId;
        WorkerNodeId workerId;
        DateTimeOffset now;
        await using (var seed = new PersistenceTestScope(database))
        {
            now = await seed.CoordinationClock.GetUtcNowAsync(
                CancellationToken.None);
            var definition =
                LocalHostInventoryPackageMetadata.CreateDefinition(now);
            var version = Assert.Single(definition.Versions);
            var worker = new WindowsScriptRunner.Domain.Workers.WorkerNode(
                WorkerNodeId.New(),
                "phase7-report-worker",
                now);
            worker.RecordHeartbeat(now);
            workerId = worker.Id;
            var requester = new UserIdentity("DOMAIN\\phase7-sql");
            var job = Job.CreateDraft(
                JobId.New(),
                definition.Id,
                version.Id,
                ExecutionPhase.DryRun,
                requester,
                now);
            job.AddTarget(new TargetName("local-worker"), requester, now);
            job.Submit(definition, requester, now);
            job.MarkValidated(requester, now);
            job.QueueDryRun(requester, now);
            jobId = job.Id;
            await seed.Scripts.AddAsync(definition, CancellationToken.None);
            await seed.Workers.AddAsync(worker, CancellationToken.None);
            await seed.Jobs.AddAsync(job, CancellationToken.None);
            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        JobLeaseCredentials credentials;
        UserIdentity actor;
        await using (var leaseScope = new PersistenceTestScope(database))
        {
            var job = Assert.IsType<Job>(
                await leaseScope.Jobs.GetByIdAsync(
                    jobId,
                    CancellationToken.None));
            var leaseNow = await leaseScope.CoordinationClock.GetUtcNowAsync(
                CancellationToken.None);
            actor = new UserIdentity($"worker:{workerId}");
            credentials = job.AcquireWorkLease(
                JobLeaseId.New(),
                workerId,
                JobWorkKind.DryRun,
                51,
                actor,
                leaseNow,
                leaseNow.AddMinutes(5)).Credentials;
            job.StartDryRun(credentials, actor, leaseNow);
            await leaseScope.Jobs.UpdateAsync(job, CancellationToken.None);
            await leaseScope.UnitOfWork.CommitAsync(CancellationToken.None);
            now = leaseNow;
        }

        return new RunningJob(
            jobId,
            credentials,
            now,
            new CompleteLocalHostInventoryDryRunCommand(
                jobId,
                credentials,
                Parse(jobId, now),
                actor));
    }

    private static ValidatedLocalHostInventoryReport Parse(
        JobId jobId,
        DateTimeOffset startedUtc,
        string computerName = "WORKER-01")
    {
        var collectedUtc = startedUtc.AddMilliseconds(500);
        var json =
            $$"""
            {"schemaVersion":"1.0","computerName":"{{computerName}}","os":{"description":"Microsoft Windows 11","version":"10.0.26100","architecture":"X64"},"powerShell":{"version":"7.4.0"},"collectedUtc":"{{collectedUtc.ToString("O", CultureInfo.InvariantCulture)}}"}
            """;
        return new LocalHostInventoryReportParser().Parse(
            new LocalHostInventoryProcessResult(
                jobId.Value,
                startedUtc,
                startedUtc.AddSeconds(1),
                0,
                json,
                string.Empty,
                standardOutputTruncated: false,
                standardErrorTruncated: false,
                exited: true));
    }

    private static JobReport CreateReport(RunningJob running)
    {
        var inventory = running.Command.Inventory;
        var payload = new LocalHostInventoryReportPayload(
            inventory.ComputerName,
            inventory.OsDescription,
            inventory.OsVersion,
            InventoryOsArchitecture.X64,
            inventory.PowerShellVersion);
        var digest = LocalHostInventoryCanonicalizer.CreateSha256(
            new LocalHostInventoryCanonicalReport(
                running.JobId.Value,
                LocalHostInventoryPackageMetadata.DefinitionId.Value,
                LocalHostInventoryPackageMetadata.VersionId.Value,
                running.Credentials.WorkerNodeId.Value,
                running.Credentials.LeaseId.Value,
                running.Credentials.FencingToken,
                inventory.ExecutionId,
                inventory.CollectedUtc,
                inventory.ComputerName,
                inventory.OsDescription,
                inventory.OsVersion,
                inventory.OsArchitecture,
                inventory.PowerShellVersion));
        return JobReport.CreateLocalHostInventory(
            running.JobId,
            LocalHostInventoryPackageMetadata.DefinitionId,
            LocalHostInventoryPackageMetadata.VersionId,
            running.Credentials.WorkerNodeId,
            running.Credentials.LeaseId,
            running.Credentials.FencingToken,
            inventory.ExecutionId,
            inventory.CollectedUtc.AddMilliseconds(500),
            inventory.CollectedUtc,
            payload,
            digest);
    }

    private static async Task<Exception?> InsertReportAsync(
        SqlServerDatabase database,
        JobReport report)
    {
        await using var scope = new PersistenceTestScope(database);
        try
        {
            await scope.Reports.AddAsync(report, CancellationToken.None);
            await scope.UnitOfWork.CommitAsync(CancellationToken.None);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<int> ReportTableCountAsync(
        WindowsScriptRunnerDbContext context) =>
        await context.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*) AS [Value]
            FROM [sys].[tables]
            WHERE [object_id] IN
            (
                OBJECT_ID(N'[wsr].[JobReports]'),
                OBJECT_ID(N'[wsr].[LocalHostInventoryReports]')
            )
            """).SingleAsync();

    private static async Task<int> RawOutputColumnCountAsync(
        WindowsScriptRunnerDbContext context) =>
        await context.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*) AS [Value]
            FROM [INFORMATION_SCHEMA].[COLUMNS]
            WHERE [TABLE_SCHEMA] = N'wsr'
              AND (
                    [COLUMN_NAME] LIKE N'%StandardOutput%'
                 OR [COLUMN_NAME] LIKE N'%StandardError%'
                 OR [COLUMN_NAME] IN (N'RawJson', N'JsonPayload')
              )
            """).SingleAsync();

    private static async Task<int> SensitiveAuditValueCountAsync(
        WindowsScriptRunnerDbContext context,
        string value) =>
        await context.AuditEventProperties.CountAsync(
            property => property.Value.Contains(value));

    private sealed record RunningJob(
        JobId JobId,
        JobLeaseCredentials Credentials,
        DateTimeOffset ProcessStartedUtc,
        CompleteLocalHostInventoryDryRunCommand Command);

    private sealed class CorruptingUnitOfWork(
        WindowsScriptRunnerDbContext context,
        IUnitOfWork inner) : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken cancellationToken)
        {
            context.ChangeTracker
                .Entries<JobReportEntity>()
                .Single()
                .Entity
                .PackageId = "unsupported.package";
            return inner.CommitAsync(cancellationToken);
        }
    }
}
