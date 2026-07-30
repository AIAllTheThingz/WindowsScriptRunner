namespace WindowsScriptRunner.Contracts.Reports;

public sealed record LocalHostInventoryReportResponse(
    Guid ReportId,
    Guid JobId,
    Guid ScriptDefinitionId,
    Guid ScriptVersionId,
    string PackageId,
    string PackageVersion,
    string ReportType,
    string SchemaVersion,
    string Format,
    Guid WorkerNodeId,
    Guid LeaseId,
    long FencingToken,
    Guid PowerShellExecutionId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset CollectedUtc,
    string ComputerName,
    string OsDescription,
    string OsVersion,
    string OsArchitecture,
    string PowerShellVersion,
    string Sha256);
