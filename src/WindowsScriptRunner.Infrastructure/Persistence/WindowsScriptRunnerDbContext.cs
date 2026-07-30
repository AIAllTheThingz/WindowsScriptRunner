using Microsoft.EntityFrameworkCore;
using WindowsScriptRunner.Infrastructure.Persistence.Entities;

namespace WindowsScriptRunner.Infrastructure.Persistence;

public sealed class WindowsScriptRunnerDbContext(DbContextOptions<WindowsScriptRunnerDbContext> options)
    : DbContext(options)
{
    internal DbSet<ScriptDefinitionEntity> ScriptDefinitions => Set<ScriptDefinitionEntity>();
    internal DbSet<ScriptVersionEntity> ScriptVersions => Set<ScriptVersionEntity>();
    internal DbSet<ScriptVersionPhaseEntity> ScriptVersionPhases => Set<ScriptVersionPhaseEntity>();
    internal DbSet<ScriptVersionReportFormatEntity> ScriptVersionReportFormats =>
        Set<ScriptVersionReportFormatEntity>();
    internal DbSet<ScriptParameterDefinitionEntity> ScriptParameterDefinitions =>
        Set<ScriptParameterDefinitionEntity>();
    internal DbSet<ScriptParameterAllowedValueEntity> ScriptParameterAllowedValues =>
        Set<ScriptParameterAllowedValueEntity>();
    internal DbSet<JobEntity> Jobs => Set<JobEntity>();
    internal DbSet<JobTargetEntity> JobTargets => Set<JobTargetEntity>();
    internal DbSet<JobParameterEntity> JobParameters => Set<JobParameterEntity>();
    internal DbSet<JobExecutionEntity> JobExecutions => Set<JobExecutionEntity>();
    internal DbSet<JobApprovalEntity> JobApprovals => Set<JobApprovalEntity>();
    internal DbSet<JobLeaseEntity> JobLeases => Set<JobLeaseEntity>();
    internal DbSet<WorkerNodeEntity> WorkerNodes => Set<WorkerNodeEntity>();
    internal DbSet<WorkerCapabilityEntity> WorkerCapabilities => Set<WorkerCapabilityEntity>();
    internal DbSet<CredentialReferenceEntity> CredentialReferences => Set<CredentialReferenceEntity>();
    internal DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();
    internal DbSet<AuditEventPropertyEntity> AuditEventProperties => Set<AuditEventPropertyEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema("wsr");
        modelBuilder.HasSequence<long>("JobLeaseFencingSequence", "wsr")
            .StartsAt(1)
            .HasMin(1)
            .IncrementsBy(1);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WindowsScriptRunnerDbContext).Assembly);
    }
}
