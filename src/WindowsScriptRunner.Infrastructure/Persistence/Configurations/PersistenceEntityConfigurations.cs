using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WindowsScriptRunner.Infrastructure.Persistence.Entities;

namespace WindowsScriptRunner.Infrastructure.Persistence.Configurations;

internal sealed class ScriptDefinitionConfiguration : IEntityTypeConfiguration<ScriptDefinitionEntity>
{
    public void Configure(EntityTypeBuilder<ScriptDefinitionEntity> builder)
    {
        builder.ToTable(
            "ScriptDefinitions",
            "wsr",
            table =>
            {
                table.HasCheckConstraint("CK_ScriptDefinitions_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint("CK_ScriptDefinitions_RiskLevel", "[RiskLevel] IN ('ReadOnly','Low','Medium','High','Critical')");
                table.HasCheckConstraint("CK_ScriptDefinitions_Timestamps", "[CreatedUtc] <= [UpdatedUtc]");
            });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.Name).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Description).HasMaxLength(2000).IsRequired();
        builder.Property(entity => entity.RiskLevel).HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(entity => entity.CreatedUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.UpdatedUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.HasIndex(entity => entity.NormalizedName)
            .IsUnique()
            .HasDatabaseName("UX_ScriptDefinitions_NormalizedName");
    }
}

internal sealed class ScriptVersionConfiguration : IEntityTypeConfiguration<ScriptVersionEntity>
{
    public void Configure(EntityTypeBuilder<ScriptVersionEntity> builder)
    {
        builder.ToTable(
            "ScriptVersions",
            "wsr",
            table =>
            {
                table.HasCheckConstraint("CK_ScriptVersions_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint("CK_ScriptVersions_Version", "[Major] >= 0 AND [Minor] >= 0 AND [Patch] >= 0");
                table.HasCheckConstraint("CK_ScriptVersions_Timeout", "[DefaultTimeoutMinutes] BETWEEN 1 AND 480");
                table.HasCheckConstraint(
                    "CK_ScriptVersions_Sha256",
                    "LEN([Sha256]) = 64 AND [Sha256] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                table.HasCheckConstraint(
                    "CK_ScriptVersions_GitCommitSha",
                    "[GitCommitSha] IS NULL OR (LEN([GitCommitSha]) BETWEEN 7 AND 64 AND [GitCommitSha] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2)");
                table.UseSqlOutputClause(false);
            });
        builder.HasKey(entity => entity.Id);
        builder.HasAlternateKey(entity => new { entity.ScriptDefinitionId, entity.Id })
            .HasName("AK_ScriptVersions_ScriptDefinitionId_Id");
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.RelativeScriptPath).HasMaxLength(500).IsRequired();
        builder.Property(entity => entity.Sha256).HasColumnType("char(64)").IsRequired();
        builder.Property(entity => entity.GitCommitSha).HasColumnType("varchar(64)");
        builder.Property(entity => entity.MinimumPowerShellVersion).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.CreatedUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.CreatedBy).HasMaxLength(256).IsRequired();
        builder.HasIndex(entity => new
        {
            entity.ScriptDefinitionId,
            entity.Major,
            entity.Minor,
            entity.Patch,
        }).IsUnique().HasDatabaseName("UX_ScriptVersions_Definition_SemanticVersion");
        builder.HasOne(entity => entity.ScriptDefinition)
            .WithMany(entity => entity.Versions)
            .HasForeignKey(entity => entity.ScriptDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ScriptVersionPhaseConfiguration : IEntityTypeConfiguration<ScriptVersionPhaseEntity>
{
    public void Configure(EntityTypeBuilder<ScriptVersionPhaseEntity> builder)
    {
        builder.ToTable(
            "ScriptVersionPhases",
            "wsr",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_ScriptVersionPhases_Phase",
                    "[Phase] IN ('Discovery','Validation','DryRun','Report','Execute','PostValidation')");
                table.UseSqlOutputClause(false);
            });
        builder.HasKey(entity => new { entity.ScriptVersionId, entity.Phase });
        builder.Property(entity => entity.Phase).HasMaxLength(32);
        builder.HasOne(entity => entity.ScriptVersion)
            .WithMany(entity => entity.SupportedPhases)
            .HasForeignKey(entity => entity.ScriptVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ScriptVersionReportFormatConfiguration :
    IEntityTypeConfiguration<ScriptVersionReportFormatEntity>
{
    public void Configure(EntityTypeBuilder<ScriptVersionReportFormatEntity> builder)
    {
        builder.ToTable(
            "ScriptVersionReportFormats",
            "wsr",
            table => table.HasCheckConstraint(
                "CK_ScriptVersionReportFormats_Format",
                "[ReportFormat] IN ('Text','Csv','Json','Html')"));
        builder.HasKey(entity => new { entity.ScriptVersionId, entity.ReportFormat });
        builder.Property(entity => entity.ReportFormat).HasMaxLength(16);
        builder.HasOne(entity => entity.ScriptVersion)
            .WithMany(entity => entity.SupportedReportFormats)
            .HasForeignKey(entity => entity.ScriptVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ScriptParameterDefinitionConfiguration :
    IEntityTypeConfiguration<ScriptParameterDefinitionEntity>
{
    public void Configure(EntityTypeBuilder<ScriptParameterDefinitionEntity> builder)
    {
        builder.ToTable(
            "ScriptParameterDefinitions",
            "wsr",
            table =>
            {
                table.HasCheckConstraint("CK_ScriptParameterDefinitions_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint(
                    "CK_ScriptParameterDefinitions_Type",
                    "[ParameterType] IN ('String','StringArray','Integer','Boolean','DateTime','Enum','SecureReference')");
                table.HasCheckConstraint(
                    "CK_ScriptParameterDefinitions_SecureReference",
                    "[ParameterType] <> 'SecureReference' OR [IsSensitive] = 1");
                table.HasCheckConstraint(
                    "CK_ScriptParameterDefinitions_SensitiveDefault",
                    "[IsSensitive] = 0 OR [DefaultValue] IS NULL");
                table.UseSqlOutputClause(false);
            });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.Name).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Description).HasMaxLength(1000);
        builder.Property(entity => entity.ParameterType).HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.DefaultValue).HasMaxLength(4000);
        builder.HasIndex(entity => new { entity.ScriptVersionId, entity.NormalizedName })
            .IsUnique()
            .HasDatabaseName("UX_ScriptParameterDefinitions_Version_NormalizedName");
        builder.HasOne(entity => entity.ScriptVersion)
            .WithMany(entity => entity.ParameterDefinitions)
            .HasForeignKey(entity => entity.ScriptVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ScriptParameterAllowedValueConfiguration :
    IEntityTypeConfiguration<ScriptParameterAllowedValueEntity>
{
    public void Configure(EntityTypeBuilder<ScriptParameterAllowedValueEntity> builder)
    {
        builder.ToTable(
            "ScriptParameterAllowedValues",
            "wsr",
            table => table.UseSqlOutputClause(false));
        builder.HasKey(entity => new { entity.ScriptParameterDefinitionId, entity.NormalizedValue });
        builder.Property(entity => entity.Value).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.NormalizedValue).HasMaxLength(200).IsRequired();
        builder.HasOne(entity => entity.ScriptParameterDefinition)
            .WithMany(entity => entity.AllowedValues)
            .HasForeignKey(entity => entity.ScriptParameterDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class JobConfiguration : IEntityTypeConfiguration<JobEntity>
{
    public void Configure(EntityTypeBuilder<JobEntity> builder)
    {
        builder.ToTable(
            "Jobs",
            "wsr",
            table =>
            {
                table.HasCheckConstraint("CK_Jobs_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint(
                    "CK_Jobs_RequestedPhase",
                    "[RequestedPhase] IN ('Discovery','Validation','DryRun','Report','Execute','PostValidation')");
                table.HasCheckConstraint(
                    "CK_Jobs_Status",
                    "[Status] IN ('Draft','Submitted','Validated','DryRunQueued','DryRunRunning','DryRunCompleted','AwaitingApproval','Approved','ExecutionQueued','Claimed','Executing','PostValidation','Completed','CompletedWithWarnings','Failed','Rejected','Cancelled','TimedOut','Blocked','NotRun')");
                table.HasCheckConstraint(
                    "CK_Jobs_Timestamps",
                    "[CreatedUtc] <= [UpdatedUtc] AND ([SubmittedUtc] IS NULL OR ([SubmittedUtc] >= [CreatedUtc] AND [SubmittedUtc] <= [UpdatedUtc]))");
                table.HasCheckConstraint(
                    "CK_Jobs_PolicySnapshot_AllOrNone",
                    "([PolicyScriptDefinitionId] IS NULL AND [PolicyScriptVersionId] IS NULL AND [PolicyRiskLevel] IS NULL AND [PolicySupportsExecute] IS NULL AND [PolicySupportsPostValidation] IS NULL AND [SubmittedUtc] IS NULL AND [Status] = 'Draft') OR ([PolicyScriptDefinitionId] IS NOT NULL AND [PolicyScriptVersionId] IS NOT NULL AND [PolicyRiskLevel] IS NOT NULL AND [PolicySupportsExecute] IS NOT NULL AND [PolicySupportsPostValidation] IS NOT NULL AND [SubmittedUtc] IS NOT NULL AND [Status] <> 'Draft')");
                table.HasCheckConstraint(
                    "CK_Jobs_PolicySnapshot_Ids",
                    "[PolicyScriptDefinitionId] IS NULL OR ([PolicyScriptDefinitionId] = [ScriptDefinitionId] AND [PolicyScriptVersionId] = [ScriptVersionId])");
                table.HasCheckConstraint(
                    "CK_Jobs_PolicyRiskLevel",
                    "[PolicyRiskLevel] IS NULL OR [PolicyRiskLevel] IN ('ReadOnly','Low','Medium','High','Critical')");
                table.HasCheckConstraint(
                    "CK_Jobs_AcceptedDryRunEvidence",
                    "([AcceptedDryRunEvidenceWorkKind] IS NULL AND [AcceptedDryRunEvidenceSource] IS NULL AND [AcceptedDryRunEvidenceWorkerNodeId] IS NULL AND [AcceptedDryRunEvidenceLeaseId] IS NULL AND [AcceptedDryRunEvidenceFencingToken] IS NULL AND [AcceptedDryRunEvidenceWindowOpenedUtc] IS NULL AND [AcceptedDryRunEvidenceCompletedUtc] IS NULL) OR ([AcceptedDryRunEvidenceWorkKind] = 'DryRun' AND [AcceptedDryRunEvidenceSource] = 'InternalLifecycle' AND [AcceptedDryRunEvidenceWorkerNodeId] IS NULL AND [AcceptedDryRunEvidenceLeaseId] IS NULL AND [AcceptedDryRunEvidenceFencingToken] IS NULL AND [AcceptedDryRunEvidenceWindowOpenedUtc] IS NOT NULL AND [AcceptedDryRunEvidenceCompletedUtc] IS NOT NULL AND [AcceptedDryRunEvidenceWindowOpenedUtc] <= [AcceptedDryRunEvidenceCompletedUtc]) OR ([AcceptedDryRunEvidenceWorkKind] = 'DryRun' AND [AcceptedDryRunEvidenceSource] = 'LeasedWorker' AND [AcceptedDryRunEvidenceWorkerNodeId] IS NOT NULL AND [AcceptedDryRunEvidenceLeaseId] IS NOT NULL AND [AcceptedDryRunEvidenceFencingToken] > 0 AND [AcceptedDryRunEvidenceWindowOpenedUtc] IS NOT NULL AND [AcceptedDryRunEvidenceCompletedUtc] IS NOT NULL AND [AcceptedDryRunEvidenceWindowOpenedUtc] <= [AcceptedDryRunEvidenceCompletedUtc])");
            });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.RequestedPhase).HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.RequestedBy).HasMaxLength(256).IsRequired();
        builder.Property(entity => entity.LastActingUser).HasMaxLength(256).IsRequired();
        builder.Property(entity => entity.CreatedUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.UpdatedUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.SubmittedUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.Description).HasMaxLength(2000);
        builder.Property(entity => entity.ChangeReference).HasMaxLength(100);
        builder.Property(entity => entity.PolicyRiskLevel).HasMaxLength(16);
        builder.Property(entity => entity.AcceptedDryRunEvidenceWorkKind).HasMaxLength(16);
        builder.Property(entity => entity.AcceptedDryRunEvidenceSource).HasMaxLength(32);
        builder.Property(entity => entity.AcceptedDryRunEvidenceWindowOpenedUtc)
            .HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.AcceptedDryRunEvidenceCompletedUtc)
            .HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.HasIndex(entity => new { entity.Status, entity.UpdatedUtc })
            .HasDatabaseName("IX_Jobs_Status_UpdatedUtc");
        builder.HasIndex(entity => entity.CreatedUtc).HasDatabaseName("IX_Jobs_CreatedUtc");
        builder.HasIndex(entity => new { entity.RequestedBy, entity.CreatedUtc })
            .HasDatabaseName("IX_Jobs_RequestedBy_CreatedUtc");
        builder.HasIndex(entity => new
        {
            entity.ScriptDefinitionId,
            entity.ScriptVersionId,
        }).HasDatabaseName("IX_Jobs_ScriptDefinitionId_ScriptVersionId");
        builder.HasIndex(entity => entity.ScriptVersionId)
            .HasDatabaseName("IX_Jobs_ScriptVersionId");
        builder.HasOne<ScriptDefinitionEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.ScriptDefinitionId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ScriptVersionEntity>()
            .WithMany()
            .HasForeignKey(entity => new
            {
                entity.ScriptDefinitionId,
                entity.ScriptVersionId,
            })
            .HasPrincipalKey(entity => new
            {
                entity.ScriptDefinitionId,
                entity.Id,
            })
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class JobTargetConfiguration : IEntityTypeConfiguration<JobTargetEntity>
{
    public void Configure(EntityTypeBuilder<JobTargetEntity> builder)
    {
        builder.ToTable("JobTargets", "wsr");
        builder.HasKey(entity => new { entity.JobId, entity.NormalizedName });
        builder.Property(entity => entity.Name).HasMaxLength(255).IsRequired();
        builder.Property(entity => entity.NormalizedName).HasMaxLength(255).IsRequired();
        builder.Property(entity => entity.AddedUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.AddedBy).HasMaxLength(256).IsRequired();
        builder.HasOne(entity => entity.Job)
            .WithMany(entity => entity.Targets)
            .HasForeignKey(entity => entity.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class JobParameterConfiguration : IEntityTypeConfiguration<JobParameterEntity>
{
    public void Configure(EntityTypeBuilder<JobParameterEntity> builder)
    {
        builder.ToTable(
            "JobParameters",
            "wsr",
            table => table.HasCheckConstraint(
                "CK_JobParameters_PresentValue",
                "LEN(LTRIM(RTRIM([SerializedValue]))) > 0"));
        builder.HasKey(entity => new { entity.JobId, entity.NormalizedName });
        builder.Property(entity => entity.Name).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.SerializedValue).HasColumnType("nvarchar(max)").IsRequired();
        builder.HasOne(entity => entity.Job)
            .WithMany(entity => entity.Parameters)
            .HasForeignKey(entity => entity.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class JobExecutionConfiguration : IEntityTypeConfiguration<JobExecutionEntity>
{
    public void Configure(EntityTypeBuilder<JobExecutionEntity> builder)
    {
        builder.ToTable(
            "JobExecutions",
            "wsr",
            table =>
            {
                table.HasCheckConstraint("CK_JobExecutions_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint("CK_JobExecutions_Attempt", "[AttemptNumber] > 0");
                table.HasCheckConstraint(
                    "CK_JobExecutions_Start",
                    "[StartedUtc] IS NULL OR [StartedUtc] >= [CreatedUtc]");
                table.HasCheckConstraint(
                    "CK_JobExecutions_Completion",
                    "([CompletedUtc] IS NULL AND [Outcome] IS NULL AND [ExitCode] IS NULL AND [Summary] IS NULL) OR ([CompletedUtc] IS NOT NULL AND [StartedUtc] IS NOT NULL AND [CompletedUtc] >= [StartedUtc] AND [Outcome] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_JobExecutions_Outcome",
                    "[Outcome] IS NULL OR [Outcome] IN ('Succeeded','SucceededWithWarnings','Failed','Cancelled','TimedOut','Blocked','NotRun')");
                table.HasCheckConstraint(
                    "CK_JobExecutions_ExitCode",
                    "[Outcome] IS NULL OR [ExitCode] IS NOT NULL OR [Outcome] IN ('Cancelled','TimedOut','Blocked','NotRun')");
            });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.CreatedUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.StartedUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.CompletedUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.Outcome).HasMaxLength(32);
        builder.Property(entity => entity.Summary).HasMaxLength(2000);
        builder.HasIndex(entity => new { entity.JobId, entity.AttemptNumber })
            .IsUnique()
            .HasDatabaseName("UX_JobExecutions_Job_Attempt");
        builder.HasIndex(entity => entity.JobId)
            .IsUnique()
            .HasFilter("[StartedUtc] IS NOT NULL AND [CompletedUtc] IS NULL")
            .HasDatabaseName("UX_JobExecutions_OneActivePerJob");
        builder.HasOne(entity => entity.Job)
            .WithMany(entity => entity.Executions)
            .HasForeignKey(entity => entity.JobId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<WorkerNodeEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.WorkerNodeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class JobApprovalConfiguration : IEntityTypeConfiguration<JobApprovalEntity>
{
    public void Configure(EntityTypeBuilder<JobApprovalEntity> builder)
    {
        builder.ToTable(
            "JobApprovals",
            "wsr",
            table =>
            {
                table.HasCheckConstraint("CK_JobApprovals_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint(
                    "CK_JobApprovals_Decision",
                    "[Decision] IN ('Approved','Rejected')");
                table.HasCheckConstraint(
                    "CK_JobApprovals_Fingerprint",
                    "LEN([ApprovalFingerprint]) = 64 AND [ApprovalFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
            });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.Decision).HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.Approver).HasMaxLength(256).IsRequired();
        builder.Property(entity => entity.DecisionUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.Comment).HasMaxLength(2000);
        builder.Property(entity => entity.ApprovalFingerprint).HasColumnType("char(64)").IsRequired();
        builder.HasIndex(entity => new { entity.JobId, entity.DecisionUtc })
            .HasDatabaseName("IX_JobApprovals_Job_DecisionUtc");
        builder.HasOne(entity => entity.Job)
            .WithMany(entity => entity.Approvals)
            .HasForeignKey(entity => entity.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class JobLeaseConfiguration : IEntityTypeConfiguration<JobLeaseEntity>
{
    public void Configure(EntityTypeBuilder<JobLeaseEntity> builder)
    {
        builder.ToTable(
            "JobLeases",
            "wsr",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_JobLeases_JobId",
                    "[JobId] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint(
                    "CK_JobLeases_LeaseId",
                    "[LeaseId] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint(
                    "CK_JobLeases_WorkerNodeId",
                    "[WorkerNodeId] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint(
                    "CK_JobLeases_WorkKind",
                    "[WorkKind] IN ('DryRun','Execute')");
                table.HasCheckConstraint(
                    "CK_JobLeases_FencingToken",
                    "[FencingToken] > 0");
                table.HasCheckConstraint(
                    "CK_JobLeases_Timestamps",
                    "[LastRenewedUtc] >= [AcquiredUtc] AND [ExpiresUtc] > [LastRenewedUtc]");
            });
        builder.HasKey(entity => entity.JobId);
        builder.Property(entity => entity.JobId).ValueGeneratedNever();
        builder.Property(entity => entity.LeaseId).ValueGeneratedNever();
        builder.Property(entity => entity.WorkKind).HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.AcquiredUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.LastRenewedUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.ExpiresUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.HasIndex(entity => entity.LeaseId)
            .IsUnique()
            .HasDatabaseName("UX_JobLeases_LeaseId");
        builder.HasIndex(entity => entity.ExpiresUtc)
            .HasDatabaseName("IX_JobLeases_ExpiresUtc");
        builder.HasIndex(entity => new { entity.WorkerNodeId, entity.ExpiresUtc })
            .HasDatabaseName("IX_JobLeases_WorkerNodeId_ExpiresUtc");
        builder.HasIndex(entity => new { entity.WorkKind, entity.ExpiresUtc })
            .HasDatabaseName("IX_JobLeases_WorkKind_ExpiresUtc");
        builder.HasOne(entity => entity.Job)
            .WithOne(entity => entity.Lease)
            .HasForeignKey<JobLeaseEntity>(entity => entity.JobId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<WorkerNodeEntity>()
            .WithMany()
            .HasForeignKey(entity => entity.WorkerNodeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class WorkerNodeConfiguration : IEntityTypeConfiguration<WorkerNodeEntity>
{
    public void Configure(EntityTypeBuilder<WorkerNodeEntity> builder)
    {
        builder.ToTable(
            "WorkerNodes",
            "wsr",
            table =>
            {
                table.HasCheckConstraint("CK_WorkerNodes_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint(
                    "CK_WorkerNodes_Heartbeat",
                    "[LastHeartbeatUtc] IS NULL OR [LastHeartbeatUtc] >= [RegisteredUtc]");
            });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.Name).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.NormalizedName).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.RegisteredUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.LastHeartbeatUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.HasIndex(entity => entity.NormalizedName)
            .IsUnique()
            .HasDatabaseName("UX_WorkerNodes_NormalizedName");
        builder.HasIndex(entity => entity.IsEnabled).HasDatabaseName("IX_WorkerNodes_IsEnabled");
        builder.HasIndex(entity => entity.LastHeartbeatUtc)
            .HasDatabaseName("IX_WorkerNodes_LastHeartbeatUtc");
    }
}

internal sealed class WorkerCapabilityConfiguration : IEntityTypeConfiguration<WorkerCapabilityEntity>
{
    public void Configure(EntityTypeBuilder<WorkerCapabilityEntity> builder)
    {
        builder.ToTable("WorkerCapabilities", "wsr");
        builder.HasKey(entity => new { entity.WorkerNodeId, entity.NormalizedName });
        builder.Property(entity => entity.Name).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.NormalizedName).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Value).HasMaxLength(200).IsRequired();
        builder.HasOne(entity => entity.WorkerNode)
            .WithMany(entity => entity.Capabilities)
            .HasForeignKey(entity => entity.WorkerNodeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CredentialReferenceConfiguration :
    IEntityTypeConfiguration<CredentialReferenceEntity>
{
    public void Configure(EntityTypeBuilder<CredentialReferenceEntity> builder)
    {
        builder.ToTable(
            "CredentialReferences",
            "wsr",
            table =>
            {
                table.HasCheckConstraint("CK_CredentialReferences_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint(
                    "CK_CredentialReferences_Hash",
                    "DATALENGTH([ExternalIdentifierHash]) = 32");
            });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.ProviderType).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.NormalizedProviderType).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.ExternalIdentifier).HasMaxLength(500).IsRequired();
        builder.Property(entity => entity.ExternalIdentifierHash).HasColumnType("binary(32)").IsRequired();
        builder.Property(entity => entity.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.CreatedUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(entity => entity.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.HasIndex(entity => new
        {
            entity.NormalizedProviderType,
            entity.ExternalIdentifierHash,
        }).IsUnique().HasDatabaseName("UX_CredentialReferences_Provider_ExternalHash");
    }
}

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEventEntity>
{
    public void Configure(EntityTypeBuilder<AuditEventEntity> builder)
    {
        builder.ToTable(
            "AuditEvents",
            "wsr",
            table => table.HasCheckConstraint(
                "CK_AuditEvents_Id",
                "[Id] <> '00000000-0000-0000-0000-000000000000'"));
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.EventType).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.EntityType).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.EntityId).HasMaxLength(500).IsRequired();
        builder.Property(entity => entity.Actor).HasMaxLength(256).IsRequired();
        builder.Property(entity => entity.OccurredUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.Summary).HasMaxLength(2000).IsRequired();
        builder.HasIndex(entity => entity.OccurredUtc)
            .HasDatabaseName("IX_AuditEvents_OccurredUtc");
        builder.HasIndex(entity => new
        {
            entity.EntityType,
            entity.EntityId,
            entity.OccurredUtc,
        }).HasDatabaseName("IX_AuditEvents_Entity_OccurredUtc");
        builder.HasIndex(entity => new { entity.Actor, entity.OccurredUtc })
            .HasDatabaseName("IX_AuditEvents_Actor_OccurredUtc");
        builder.HasIndex(entity => new { entity.EventType, entity.OccurredUtc })
            .HasDatabaseName("IX_AuditEvents_EventType_OccurredUtc");
    }
}

internal sealed class AuditEventPropertyConfiguration :
    IEntityTypeConfiguration<AuditEventPropertyEntity>
{
    public void Configure(EntityTypeBuilder<AuditEventPropertyEntity> builder)
    {
        builder.ToTable(
            "AuditEventProperties",
            "wsr",
            table => table.HasCheckConstraint(
                "CK_AuditEventProperties_NonSensitiveKey",
                "[NormalizedKey] NOT LIKE '%PASSWORD%' AND [NormalizedKey] NOT LIKE '%SECRET%' AND ([NormalizedKey] NOT LIKE '%TOKEN%' OR [NormalizedKey] = 'FENCINGTOKEN')"));
        builder.HasKey(entity => new { entity.AuditEventId, entity.NormalizedKey });
        builder.Property(entity => entity.Key).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.NormalizedKey).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Value).HasMaxLength(2000).IsRequired();
        builder.HasOne(entity => entity.AuditEvent)
            .WithMany(entity => entity.Properties)
            .HasForeignKey(entity => entity.AuditEventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
