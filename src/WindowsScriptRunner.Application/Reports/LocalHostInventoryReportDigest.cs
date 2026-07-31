using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Reports;
using WindowsScriptRunner.Reporting;

namespace WindowsScriptRunner.Application.Reports;

public static class LocalHostInventoryReportDigest
{
    public static string Create(
        Job job,
        JobLeaseCredentials credentials,
        ValidatedLocalHostInventoryReport inventory,
        LocalHostInventoryReportPayload payload)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(payload);
        return Create(
            job.Id,
            job.ScriptDefinitionId,
            job.ScriptVersionId,
            credentials.WorkerNodeId,
            credentials.LeaseId,
            credentials.FencingToken,
            inventory.ExecutionId,
            inventory.CollectedUtc,
            payload);
    }

    public static string Create(JobReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return Create(
            report.JobId,
            report.ScriptDefinitionId,
            report.ScriptVersionId,
            report.WorkerNodeId,
            report.LeaseId,
            report.FencingToken,
            report.PowerShellExecutionId,
            report.CollectedUtc,
            report.Inventory);
    }

    private static string Create(
        JobId jobId,
        ScriptDefinitionId scriptDefinitionId,
        ScriptVersionId scriptVersionId,
        WorkerNodeId workerNodeId,
        JobLeaseId leaseId,
        long fencingToken,
        Guid powerShellExecutionId,
        DateTimeOffset collectedUtc,
        LocalHostInventoryReportPayload payload) =>
        LocalHostInventoryCanonicalizer.CreateSha256(
            new LocalHostInventoryCanonicalReport(
                jobId.Value,
                scriptDefinitionId.Value,
                scriptVersionId.Value,
                workerNodeId.Value,
                leaseId.Value,
                fencingToken,
                powerShellExecutionId,
                collectedUtc,
                payload.ComputerName,
                payload.OsDescription,
                payload.OsVersion,
                payload.OsArchitecture.ToString(),
                payload.PowerShellVersion));
}
