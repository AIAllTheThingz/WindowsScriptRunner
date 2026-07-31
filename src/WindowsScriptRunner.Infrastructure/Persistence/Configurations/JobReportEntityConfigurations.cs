using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WindowsScriptRunner.Infrastructure.Persistence.Entities;

namespace WindowsScriptRunner.Infrastructure.Persistence.Configurations;

internal sealed class JobReportConfiguration :
    IEntityTypeConfiguration<JobReportEntity>
{
    public void Configure(EntityTypeBuilder<JobReportEntity> builder)
    {
        builder.ToTable(
            "JobReports",
            "wsr",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_JobReports_Identifiers",
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND " +
                    "[JobId] <> '00000000-0000-0000-0000-000000000000' AND " +
                    "[ScriptDefinitionId] <> '00000000-0000-0000-0000-000000000000' AND " +
                    "[ScriptVersionId] <> '00000000-0000-0000-0000-000000000000' AND " +
                    "[WorkerNodeId] <> '00000000-0000-0000-0000-000000000000' AND " +
                    "[LeaseId] <> '00000000-0000-0000-0000-000000000000' AND " +
                    "[PowerShellExecutionId] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint(
                    "CK_JobReports_SupportedType",
                    "[PackageId] COLLATE Latin1_General_100_BIN2 = 'windows.local-host-inventory' AND " +
                    "[PackageVersion] COLLATE Latin1_General_100_BIN2 = '1.0.0' AND " +
                    "[ReportType] COLLATE Latin1_General_100_BIN2 = 'LocalHostInventory' AND " +
                    "[SchemaVersion] COLLATE Latin1_General_100_BIN2 = '1.0' AND " +
                    "[Format] COLLATE Latin1_General_100_BIN2 = 'Json'");
                table.HasCheckConstraint(
                    "CK_JobReports_FencingToken",
                    "[FencingToken] > 0");
                table.HasCheckConstraint(
                    "CK_JobReports_Timestamps",
                    "[CollectedUtc] <= DATEADD(second, 5, [CreatedUtc])");
                table.HasCheckConstraint(
                    "CK_JobReports_Sha256",
                    "LEN([Sha256]) = 64 AND " +
                    "[Sha256] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
            });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.PackageId).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.PackageVersion).HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.ReportType).HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.SchemaVersion).HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.Format).HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.CreatedUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.CollectedUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(entity => entity.Sha256).HasColumnType("char(64)").IsRequired();
        builder.HasIndex(entity => new
        {
            entity.JobId,
            entity.PackageId,
            entity.SchemaVersion,
        }).IsUnique().HasDatabaseName(
            "UX_JobReports_Job_Package_Schema");
        builder.HasIndex(entity => entity.LeaseId)
            .IsUnique()
            .HasDatabaseName("UX_JobReports_LeaseId");
        builder.HasIndex(entity => entity.PowerShellExecutionId)
            .IsUnique()
            .HasDatabaseName("UX_JobReports_PowerShellExecutionId");
        builder.HasIndex(entity => new
        {
            entity.ReportType,
            entity.CreatedUtc,
            entity.Id,
        }).HasDatabaseName("IX_JobReports_ReportType_CreatedUtc_Id");
        builder.HasOne(entity => entity.Job)
            .WithMany()
            .HasForeignKey(entity => entity.JobId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(entity => entity.ScriptDefinition)
            .WithMany()
            .HasForeignKey(entity => entity.ScriptDefinitionId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(entity => entity.ScriptVersion)
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
        builder.HasOne(entity => entity.WorkerNode)
            .WithMany()
            .HasForeignKey(entity => entity.WorkerNodeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class LocalHostInventoryReportConfiguration :
    IEntityTypeConfiguration<LocalHostInventoryReportEntity>
{
    public void Configure(
        EntityTypeBuilder<LocalHostInventoryReportEntity> builder)
    {
        builder.ToTable(
            "LocalHostInventoryReports",
            "wsr",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_LocalHostInventoryReports_ReportId",
                    "[ReportId] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint(
                    "CK_LocalHostInventoryReports_ComputerName",
                    "LEN([ComputerName]) BETWEEN 1 AND 63 AND " +
                    "[ComputerName] NOT LIKE '%[^A-Za-z0-9-]%' COLLATE Latin1_General_100_BIN2 AND " +
                    "[ComputerName] NOT LIKE '-%' AND [ComputerName] NOT LIKE '%-'");
                table.HasCheckConstraint(
                    "CK_LocalHostInventoryReports_Architecture",
                    "[OsArchitecture] COLLATE Latin1_General_100_BIN2 " +
                    "IN ('X86','X64','Arm','Arm64')");
                table.HasCheckConstraint(
                    "CK_LocalHostInventoryReports_OsDescription",
                    "LEN([OsDescription]) BETWEEN 1 AND 256");
                table.HasCheckConstraint(
                    "CK_LocalHostInventoryReports_Versions",
                    "LEN([OsVersion]) BETWEEN 5 AND 32 AND " +
                    "LEN([PowerShellVersion]) BETWEEN 5 AND 32 AND " +
                    "[OsVersion] NOT LIKE '%[^0-9.]%' AND " +
                    "[PowerShellVersion] NOT LIKE '%[^0-9.]%' AND " +
                    "[OsVersion] NOT LIKE '.%' AND [OsVersion] NOT LIKE '%.' AND " +
                    "[PowerShellVersion] NOT LIKE '.%' AND [PowerShellVersion] NOT LIKE '%.' AND " +
                    "[OsVersion] NOT LIKE '%..%' AND [PowerShellVersion] NOT LIKE '%..%'");
            });
        builder.HasKey(entity => entity.ReportId);
        builder.Property(entity => entity.ReportId).ValueGeneratedNever();
        builder.Property(entity => entity.ComputerName).HasMaxLength(63).IsRequired();
        builder.Property(entity => entity.OsDescription).HasMaxLength(256).IsRequired();
        builder.Property(entity => entity.OsVersion).HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.OsArchitecture).HasMaxLength(8).IsRequired();
        builder.Property(entity => entity.PowerShellVersion).HasMaxLength(32).IsRequired();
        builder.HasOne(entity => entity.Report)
            .WithOne(entity => entity.Inventory)
            .HasForeignKey<LocalHostInventoryReportEntity>(
                entity => entity.ReportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
