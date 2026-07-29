using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Infrastructure.Persistence;
using WindowsScriptRunner.Infrastructure.Persistence.Entities;
using WindowsScriptRunner.Infrastructure.Persistence.Mapping;

namespace WindowsScriptRunner.SqlServerTests;

public sealed class ConstraintTests
{
    [Fact]
    public async Task SqlServerRejectsRequiredDuplicateAndCorruptRepresentations()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var parameter = SqlServerTestData.Parameter("Mode");
        var version = SqlServerTestData.Version([parameter]);
        var script = SqlServerTestData.Script(version);
        var otherVersion = SqlServerTestData.Version();
        var otherScript = SqlServerTestData.Script(
            otherVersion,
            name: "other.script");
        var job = SqlServerTestData.SubmittedJob(
            script,
            version,
            [(parameter, "Safe")]);
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
        job.Claim(SqlServerTestData.Approver, job.UpdatedUtc.AddMinutes(1));
        job.StartExecutionAttempt(
            null,
            SqlServerTestData.Approver,
            job.UpdatedUtc.AddMinutes(1));
        var worker = SqlServerTestData.Worker();
        var audit = new AuditEvent(
            AuditEventId.New(),
            "Seeded",
            "Job",
            job.Id.ToString(),
            SqlServerTestData.Requester,
            SqlServerTestData.Time,
            "Seeded",
            new Dictionary<string, string>
            {
                ["Attempt"] = "1",
            });
        await using (var seed = new PersistenceTestScope(database))
        {
            await seed.Scripts.AddAsync(script, CancellationToken.None);
            await seed.Scripts.AddAsync(otherScript, CancellationToken.None);
            await seed.Jobs.AddAsync(job, CancellationToken.None);
            await seed.Workers.AddAsync(worker, CancellationToken.None);
            await seed.Audits.WriteAsync(audit, CancellationToken.None);
            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await AssertRejectedAsync(
            database,
            context => context.ScriptDefinitions.Add(
                new ScriptDefinitionEntity
                {
                    Id = Guid.NewGuid(),
                    Name = "TEST.SCRIPT",
                    NormalizedName = PersistenceMapper.Normalize("TEST.SCRIPT"),
                    DisplayName = "Duplicate",
                    Description = string.Empty,
                    RiskLevel = nameof(RiskLevel.Low),
                    IsEnabled = true,
                    CreatedBy = SqlServerTestData.Requester.Value,
                    CreatedUtc = SqlServerTestData.Time,
                    UpdatedUtc = SqlServerTestData.Time,
                }));
        await AssertRejectedAsync(
            database,
            context => context.ScriptVersions.Add(
                ValidVersionEntity(Guid.NewGuid(), script.Id.Value, 1, 0, 0)));
        await AssertRejectedAsync(
            database,
            context => context.Jobs.Add(
                new JobEntity
                {
                    Id = Guid.NewGuid(),
                    ScriptDefinitionId = script.Id.Value,
                    ScriptVersionId = otherVersion.Id.Value,
                    RequestedPhase = nameof(ExecutionPhase.DryRun),
                    Status = nameof(JobStatus.Draft),
                    RequestedBy = SqlServerTestData.Requester.Value,
                    LastActingUser = SqlServerTestData.Requester.Value,
                    CreatedUtc = SqlServerTestData.Time,
                    UpdatedUtc = SqlServerTestData.Time,
                }));
        await AssertRejectedAsync(
            database,
            context => context.ScriptParameterDefinitions.Add(
                new ScriptParameterDefinitionEntity
                {
                    Id = Guid.NewGuid(),
                    ScriptVersionId = version.Id.Value,
                    Name = "MODE",
                    NormalizedName = PersistenceMapper.Normalize("MODE"),
                    DisplayName = "Mode duplicate",
                    ParameterType = nameof(ScriptParameterType.String),
                }));
        await AssertRejectedAsync(
            database,
            context => context.JobTargets.Add(
                new JobTargetEntity
                {
                    JobId = job.Id.Value,
                    Name = "SERVER-01",
                    NormalizedName = PersistenceMapper.Normalize("SERVER-01"),
                    AddedUtc = SqlServerTestData.Time,
                    AddedBy = SqlServerTestData.Requester.Value,
                }));
        await AssertRejectedAsync(
            database,
            context => context.JobParameters.Add(
                new JobParameterEntity
                {
                    JobId = job.Id.Value,
                    Name = "MODE",
                    NormalizedName = PersistenceMapper.Normalize("MODE"),
                    SerializedValue = "Duplicate",
                }));
        await AssertRejectedAsync(
            database,
            context => context.JobExecutions.Add(
                new JobExecutionEntity
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id.Value,
                    AttemptNumber = 1,
                    CreatedUtc = job.UpdatedUtc,
                    StartedUtc = job.UpdatedUtc,
                }));
        await AssertRejectedAsync(
            database,
            context => context.JobExecutions.Add(
                new JobExecutionEntity
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id.Value,
                    AttemptNumber = 2,
                    CreatedUtc = job.UpdatedUtc,
                    StartedUtc = job.UpdatedUtc,
                }));
        await AssertRejectedAsync(
            database,
            context => context.JobExecutions.Add(
                new JobExecutionEntity
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id.Value,
                    AttemptNumber = 3,
                    CreatedUtc = job.UpdatedUtc,
                    ExitCode = 1,
                }));
        await AssertRejectedAsync(
            database,
            context => context.WorkerNodes.Add(
                new WorkerNodeEntity
                {
                    Id = Guid.NewGuid(),
                    Name = "WORKER-01",
                    NormalizedName = PersistenceMapper.Normalize("WORKER-01"),
                    IsEnabled = true,
                    RegisteredUtc = SqlServerTestData.Time,
                }));
        await AssertRejectedAsync(
            database,
            context => context.WorkerCapabilities.Add(
                new WorkerCapabilityEntity
                {
                    WorkerNodeId = worker.Id.Value,
                    Name = "POWERSHELL",
                    NormalizedName = PersistenceMapper.Normalize("POWERSHELL"),
                    Value = "7.5",
                }));
        await AssertRejectedAsync(
            database,
            context => context.AuditEventProperties.Add(
                new AuditEventPropertyEntity
                {
                    AuditEventId = audit.Id.Value,
                    Key = "ATTEMPT",
                    NormalizedKey = PersistenceMapper.Normalize("ATTEMPT"),
                    Value = "2",
                }));
        await AssertRejectedAsync(
            database,
            async context =>
            {
                var entity = await context.Jobs.FindAsync(job.Id.Value);
                entity!.Status = "UnsupportedStatus";
            });
        await AssertRejectedAsync(
            database,
            async context =>
            {
                var entity = await context.ScriptDefinitions.FindAsync(script.Id.Value);
                entity!.UpdatedUtc = entity.CreatedUtc.AddTicks(-1);
            });
        await AssertRejectedAsync(
            database,
            async context =>
            {
                var entity = await context.Jobs.FindAsync(job.Id.Value);
                entity!.PolicyRiskLevel = null;
            });
        await AssertRejectedAsync(
            database,
            context => context.ScriptParameterAllowedValues.Add(
                new ScriptParameterAllowedValueEntity
                {
                    ScriptParameterDefinitionId = parameter.Id.Value,
                    Value = "NotAllowedForString",
                    NormalizedValue = PersistenceMapper.Normalize("NotAllowedForString"),
                }));
        await AssertRejectedAsync(
            database,
            context =>
            {
                var executeOnly = ValidVersionEntity(
                    Guid.NewGuid(),
                    script.Id.Value,
                    3,
                    0,
                    0);
                executeOnly.IsPublished = true;
                executeOnly.SupportedPhases.Add(
                    new ScriptVersionPhaseEntity
                    {
                        ScriptVersionId = executeOnly.Id,
                        Phase = nameof(ExecutionPhase.Execute),
                    });
                context.ScriptVersions.Add(executeOnly);
            });
    }

    private static async Task AssertRejectedAsync(
        SqlServerDatabase database,
        Action<WindowsScriptRunnerDbContext> stage)
    {
        await AssertRejectedAsync(
            database,
            context =>
            {
                stage(context);
                return Task.CompletedTask;
            });
    }

    private static async Task AssertRejectedAsync(
        SqlServerDatabase database,
        Func<WindowsScriptRunnerDbContext, Task> stage)
    {
        await using var scope = new PersistenceTestScope(database);
        await stage(scope.Context);
        var exception = await Assert.ThrowsAnyAsync<ApplicationExceptionBase>(
            () => scope.UnitOfWork.CommitAsync(CancellationToken.None));
        Assert.DoesNotContain("Server=", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT ", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ScriptVersionEntity ValidVersionEntity(
        Guid id,
        Guid scriptDefinitionId,
        int major,
        int minor,
        int patch) =>
        new()
        {
            Id = id,
            ScriptDefinitionId = scriptDefinitionId,
            Major = major,
            Minor = minor,
            Patch = patch,
            RelativeScriptPath = $"scripts/{id:N}.ps1",
            Sha256 = new string('a', 64),
            GitCommitSha = "abcdef1",
            MinimumPowerShellVersion = "7.4",
            DefaultTimeoutMinutes = 30,
            CreatedUtc = SqlServerTestData.Time,
            CreatedBy = SqlServerTestData.Requester.Value,
        };
}
