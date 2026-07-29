using Microsoft.EntityFrameworkCore;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Jobs;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
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

    private static AuditEvent Audit(string eventType, JobId jobId) =>
        new(
            AuditEventId.New(),
            eventType,
            "Job",
            jobId.ToString(),
            SqlServerTestData.Requester,
            SqlServerTestData.Time.AddMinutes(10),
            eventType);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
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
}
