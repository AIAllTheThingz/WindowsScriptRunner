using System.Globalization;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Contracts.Reports;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Reports;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Reporting;

namespace WindowsScriptRunner.Application.Reports;

public sealed record CompleteLocalHostInventoryDryRunCommand(
    JobId JobId,
    JobLeaseCredentials Credentials,
    ValidatedLocalHostInventoryReport Inventory,
    UserIdentity ActingUser);

public sealed record LocalHostInventoryReportCompletion(
    JobReportId ReportId,
    bool Created);

public sealed record GetLocalHostInventoryReportByIdQuery(JobReportId ReportId);

public sealed record GetLocalHostInventoryReportByJobIdQuery(JobId JobId);

public sealed class CompleteLocalHostInventoryDryRunHandler(
    IJobRepository jobRepository,
    IScriptDefinitionRepository scriptRepository,
    IJobReportRepository reportRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    private const string ExpectedScriptPath =
        "windows.local-host-inventory/1.0.0/Collect-LocalHostInventory.ps1";
    private const string ExpectedScriptSha256 =
        "b85b29bbfc04dfb9c85f3fcc391e58c1ea0ef8aeeddcb5b796d8968b3729c368";
    private const string ExpectedMinimumPowerShellVersion = "7.4.0";
    private static readonly Guid ExpectedDefinitionId =
        Guid.Parse("7fc1cf27-4d30-48b2-9ae5-6b41a7f57758");
    private static readonly Guid ExpectedScriptVersionId =
        Guid.Parse("6f1e7581-b7e2-4114-aa0f-28f90c95e6af");
    private static readonly ScriptVersionNumber ExpectedVersion =
        ScriptVersionNumber.Parse(JobReport.LocalHostInventoryPackageVersion);

    public async Task<LocalHostInventoryReportCompletion> HandleAsync(
        CompleteLocalHostInventoryDryRunCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Credentials);
        ArgumentNullException.ThrowIfNull(command.Inventory);
        ArgumentNullException.ThrowIfNull(command.ActingUser);

        var job = await jobRepository.GetByIdAsync(
            command.JobId,
            cancellationToken)
            ?? throw new EntityNotFoundException(
                nameof(Job),
                command.JobId.ToString());
        var reportId = JobReport.CreateDeterministicId(job.Id);
        var existing = await reportRepository.GetByIdAsync(
            reportId,
            cancellationToken);
        if (existing is not null)
        {
            EnsureExactReplay(job, existing, command);
            return new LocalHostInventoryReportCompletion(existing.Id, Created: false);
        }

        var version = await LoadAndValidatePinnedPackageAsync(
            job,
            cancellationToken);
        var reportForJob = await reportRepository.GetByJobIdAsync(
            job.Id,
            cancellationToken);
        if (reportForJob is not null)
        {
            throw new ApplicationConflictException(
                "The job already has a conflicting durable report.");
        }

        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        try
        {
            job.ValidateWorkLease(command.Credentials, JobWorkKind.DryRun, now);
        }
        catch (DomainException exception)
        {
            throw new ApplicationConflictException(
                "The Local Host Inventory lease is stale, expired, or invalid.",
                exception);
        }

        if (job.Status != JobStatus.DryRunRunning ||
            command.Inventory.ExecutionId != job.Id.Value)
        {
            throw new ApplicationConflictException(
                "The Local Host Inventory result does not belong to the running job.");
        }

        var payload = ToPayload(command.Inventory);
        var digest = LocalHostInventoryReportDigest.Create(
            job,
            command.Credentials,
            command.Inventory,
            payload);
        JobReport report;
        try
        {
            report = JobReport.CreateLocalHostInventory(
                job.Id,
                job.ScriptDefinitionId,
                version.Id,
                command.Credentials.WorkerNodeId,
                command.Credentials.LeaseId,
                command.Credentials.FencingToken,
                command.Inventory.ExecutionId,
                now,
                command.Inventory.CollectedUtc,
                payload,
                digest);
        }
        catch (DomainException exception)
        {
            throw new ApplicationValidationException(
                "The validated Local Host Inventory report violates durable report invariants.",
                exception);
        }

        await reportRepository.AddAsync(report, cancellationToken);
        try
        {
            job.CompleteDryRun(
                command.Credentials,
                command.ActingUser,
                now);
            job.CompleteReadOnlyAfterDryRun(command.ActingUser, now);
        }
        catch (DomainException exception)
        {
            throw new ApplicationConflictException(
                "The running Local Host Inventory job cannot be completed.",
                exception);
        }

        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            CreateAudit(report, command.ActingUser, now),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return new LocalHostInventoryReportCompletion(report.Id, Created: true);
    }

    private async Task<ScriptVersion> LoadAndValidatePinnedPackageAsync(
        Job job,
        CancellationToken cancellationToken)
    {
        var definition = await scriptRepository.GetByIdAsync(
            job.ScriptDefinitionId,
            cancellationToken)
            ?? throw new ApplicationConflictException(
                "The pinned Local Host Inventory definition is unavailable.");
        var version = definition.Versions.SingleOrDefault(
            candidate => candidate.Id == job.ScriptVersionId)
            ?? throw new ApplicationConflictException(
                "The pinned Local Host Inventory version is unavailable.");
        var valid =
            definition.Id == job.ScriptDefinitionId &&
            definition.Id.Value == ExpectedDefinitionId &&
            definition.IsEnabled &&
            string.Equals(
                definition.Name.Value,
                JobReport.LocalHostInventoryPackageId,
                StringComparison.Ordinal) &&
            definition.RiskLevel == RiskLevel.ReadOnly &&
            version.Id == job.ScriptVersionId &&
            version.Id.Value == ExpectedScriptVersionId &&
            version.IsPublished &&
            version.Version == ExpectedVersion &&
            string.Equals(
                version.RelativeScriptPath,
                ExpectedScriptPath,
                StringComparison.Ordinal) &&
            string.Equals(
                version.Sha256,
                ExpectedScriptSha256,
                StringComparison.Ordinal) &&
            string.Equals(
                version.MinimumPowerShellVersion,
                ExpectedMinimumPowerShellVersion,
                StringComparison.Ordinal) &&
            version.DefaultTimeoutMinutes == 1 &&
            version.ParameterDefinitions.Count == 0 &&
            version.SupportedPhases.Count == 1 &&
            version.SupportedPhases.Contains(ExecutionPhase.DryRun) &&
            version.SupportedReportFormats.Count == 1 &&
            version.SupportedReportFormats.Contains(ReportFormat.Json);
        if (!valid)
        {
            throw new ApplicationConflictException(
                "The pinned script is not the reviewed Local Host Inventory package.");
        }

        return version;
    }

    private static void EnsureExactReplay(
        Job job,
        JobReport existing,
        CompleteLocalHostInventoryDryRunCommand command)
    {
        var payload = ToPayload(command.Inventory);
        var digest = LocalHostInventoryReportDigest.Create(
            job,
            command.Credentials,
            command.Inventory,
            payload);
        var matches =
            job.Status == JobStatus.Completed &&
            job.Lease is null &&
            existing.JobId == job.Id &&
            existing.ScriptDefinitionId == job.ScriptDefinitionId &&
            existing.ScriptVersionId == job.ScriptVersionId &&
            existing.WorkerNodeId == command.Credentials.WorkerNodeId &&
            existing.LeaseId == command.Credentials.LeaseId &&
            existing.FencingToken == command.Credentials.FencingToken &&
            existing.PowerShellExecutionId == command.Inventory.ExecutionId &&
            existing.CollectedUtc == command.Inventory.CollectedUtc.ToUniversalTime() &&
            existing.Inventory.Equals(payload) &&
            string.Equals(existing.Sha256, digest, StringComparison.Ordinal);
        if (!matches)
        {
            throw new ApplicationConflictException(
                "The deterministic report identity conflicts with persisted content or provenance.");
        }
    }

    private static LocalHostInventoryReportPayload ToPayload(
        ValidatedLocalHostInventoryReport inventory)
    {
        if (!Enum.TryParse<InventoryOsArchitecture>(
                inventory.OsArchitecture,
                ignoreCase: false,
                out var architecture) ||
            !Enum.IsDefined(architecture))
        {
            throw new ApplicationValidationException(
                "The validated inventory architecture is unsupported.");
        }

        try
        {
            return new LocalHostInventoryReportPayload(
                inventory.ComputerName,
                inventory.OsDescription,
                inventory.OsVersion,
                architecture,
                inventory.PowerShellVersion);
        }
        catch (DomainException exception)
        {
            throw new ApplicationValidationException(
                "The validated inventory payload violates report invariants.",
                exception);
        }
    }

    private static AuditEvent CreateAudit(
        JobReport report,
        UserIdentity actor,
        DateTimeOffset occurredUtc) =>
        new(
            AuditEventId.New(),
            "LocalHostInventoryReportPersisted",
            nameof(JobReport),
            report.Id.ToString(),
            actor,
            occurredUtc,
            "A trusted Local Host Inventory report was persisted and its job completed.",
            new Dictionary<string, string>
            {
                ["ReportId"] = report.Id.ToString(),
                ["ReportType"] = report.ReportType.ToString(),
                ["SchemaVersion"] = report.SchemaVersion,
                ["Format"] = report.Format.ToString(),
                ["ScriptVersionId"] = report.ScriptVersionId.ToString(),
                ["WorkerNodeId"] = report.WorkerNodeId.ToString(),
                ["Created"] = bool.TrueString,
            });
}

public sealed class GetLocalHostInventoryReportHandler(
    IJobReportRepository reportRepository)
{
    public async Task<LocalHostInventoryReportResponse> HandleAsync(
        GetLocalHostInventoryReportByIdQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var report = await reportRepository.GetByIdAsync(
            query.ReportId,
            cancellationToken)
            ?? throw new EntityNotFoundException(
                nameof(JobReport),
                query.ReportId.ToString());
        return ToResponse(report);
    }

    public async Task<LocalHostInventoryReportResponse> HandleAsync(
        GetLocalHostInventoryReportByJobIdQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var report = await reportRepository.GetByJobIdAsync(
            query.JobId,
            cancellationToken)
            ?? throw new EntityNotFoundException(
                nameof(JobReport),
                query.JobId.ToString());
        return ToResponse(report);
    }

    private static LocalHostInventoryReportResponse ToResponse(JobReport report) =>
        new(
            report.Id.Value,
            report.JobId.Value,
            report.ScriptDefinitionId.Value,
            report.ScriptVersionId.Value,
            report.PackageId,
            report.PackageVersion,
            report.ReportType.ToString(),
            report.SchemaVersion,
            report.Format.ToString(),
            report.WorkerNodeId.Value,
            report.LeaseId.Value,
            report.FencingToken,
            report.PowerShellExecutionId,
            report.CreatedUtc,
            report.CollectedUtc,
            report.Inventory.ComputerName,
            report.Inventory.OsDescription,
            report.Inventory.OsVersion,
            report.Inventory.OsArchitecture.ToString(),
            report.Inventory.PowerShellVersion,
            report.Sha256);
}
