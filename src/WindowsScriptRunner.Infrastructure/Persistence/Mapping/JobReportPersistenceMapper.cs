using WindowsScriptRunner.Application.Reports;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Reports;
using WindowsScriptRunner.Infrastructure.Persistence.Entities;

namespace WindowsScriptRunner.Infrastructure.Persistence.Mapping;

internal static class JobReportPersistenceMapper
{
    public static JobReportEntity ToEntity(JobReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new JobReportEntity
        {
            Id = report.Id.Value,
            JobId = report.JobId.Value,
            ScriptDefinitionId = report.ScriptDefinitionId.Value,
            ScriptVersionId = report.ScriptVersionId.Value,
            PackageId = report.PackageId,
            PackageVersion = report.PackageVersion,
            ReportType = report.ReportType.ToString(),
            SchemaVersion = report.SchemaVersion,
            Format = report.Format.ToString(),
            WorkerNodeId = report.WorkerNodeId.Value,
            LeaseId = report.LeaseId.Value,
            FencingToken = report.FencingToken,
            PowerShellExecutionId = report.PowerShellExecutionId,
            CreatedUtc = report.CreatedUtc,
            CollectedUtc = report.CollectedUtc,
            Sha256 = report.Sha256,
            Inventory = new LocalHostInventoryReportEntity
            {
                ReportId = report.Id.Value,
                ComputerName = report.Inventory.ComputerName,
                OsDescription = report.Inventory.OsDescription,
                OsVersion = report.Inventory.OsVersion,
                OsArchitecture = report.Inventory.OsArchitecture.ToString(),
                PowerShellVersion = report.Inventory.PowerShellVersion,
            },
        };
    }

    public static JobReport ToDomain(JobReportEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var inventory = entity.Inventory ??
            throw new InvalidOperationException(
                "Persisted report detail is missing.");
        if (inventory.ReportId != entity.Id)
        {
            throw new InvalidOperationException(
                "Persisted report envelope and detail identifiers disagree.");
        }

        var report = JobReport.Rehydrate(
            new JobReportId(entity.Id),
            new JobId(entity.JobId),
            new ScriptDefinitionId(entity.ScriptDefinitionId),
            new ScriptVersionId(entity.ScriptVersionId),
            entity.PackageId,
            entity.PackageVersion,
            ParseEnum<JobReportType>(entity.ReportType),
            entity.SchemaVersion,
            ParseEnum<ReportFormat>(entity.Format),
            new WorkerNodeId(entity.WorkerNodeId),
            new JobLeaseId(entity.LeaseId),
            entity.FencingToken,
            entity.PowerShellExecutionId,
            entity.CreatedUtc.ToUniversalTime(),
            entity.CollectedUtc.ToUniversalTime(),
            new LocalHostInventoryReportPayload(
                inventory.ComputerName,
                inventory.OsDescription,
                inventory.OsVersion,
                ParseEnum<InventoryOsArchitecture>(inventory.OsArchitecture),
                inventory.PowerShellVersion),
            entity.Sha256);
        if (!string.Equals(
                report.Sha256,
                LocalHostInventoryReportDigest.Create(report),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Persisted report digest does not match its typed content and provenance.");
        }

        return report;
    }

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(
                value,
                ignoreCase: false,
                out var result) ||
            !Enum.IsDefined(result))
        {
            throw new InvalidOperationException(
                "Persisted report contains an unsupported enum value.");
        }

        return result;
    }
}
