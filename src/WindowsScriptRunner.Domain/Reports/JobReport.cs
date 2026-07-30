using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Domain.Reports;

public sealed class LocalHostInventoryReportPayload :
    IEquatable<LocalHostInventoryReportPayload>
{
    private static readonly Regex VersionPattern = new(
        @"\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*)){1,2}\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex ComputerNamePattern = new(
        @"\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\z",
        RegexOptions.CultureInvariant);
    private static readonly Version MinimumPowerShellVersion = new(7, 4, 0);

    public LocalHostInventoryReportPayload(
        string computerName,
        string osDescription,
        string osVersion,
        InventoryOsArchitecture osArchitecture,
        string powerShellVersion)
    {
        ComputerName = RequiredExact(
            computerName,
            nameof(ComputerName),
            63);
        if (!ComputerNamePattern.IsMatch(ComputerName))
        {
            throw new DomainValidationException("Inventory computer name is malformed.");
        }

        OsDescription = RequiredExact(
            osDescription,
            nameof(OsDescription),
            256);
        OsVersion = RequiredVersion(
            osVersion,
            nameof(OsVersion),
            minimum: null);
        OsArchitecture = EnumGuard.RequireDefined(
            osArchitecture,
            nameof(OsArchitecture));
        PowerShellVersion = RequiredVersion(
            powerShellVersion,
            nameof(PowerShellVersion),
            MinimumPowerShellVersion);
    }

    public string ComputerName { get; }
    public string OsDescription { get; }
    public string OsVersion { get; }
    public InventoryOsArchitecture OsArchitecture { get; }
    public string PowerShellVersion { get; }

    public bool Equals(LocalHostInventoryReportPayload? other) =>
        other is not null &&
        string.Equals(ComputerName, other.ComputerName, StringComparison.Ordinal) &&
        string.Equals(OsDescription, other.OsDescription, StringComparison.Ordinal) &&
        string.Equals(OsVersion, other.OsVersion, StringComparison.Ordinal) &&
        OsArchitecture == other.OsArchitecture &&
        string.Equals(
            PowerShellVersion,
            other.PowerShellVersion,
            StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is LocalHostInventoryReportPayload other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(
            ComputerName,
            OsDescription,
            OsVersion,
            OsArchitecture,
            PowerShellVersion);

    private static string RequiredVersion(
        string value,
        string fieldName,
        Version? minimum)
    {
        var result = RequiredExact(value, fieldName, 32);
        if (!VersionPattern.IsMatch(result) ||
            !Version.TryParse(result, out var parsed) ||
            (minimum is not null && parsed < minimum))
        {
            throw new DomainValidationException(
                $"{fieldName} is unsupported or malformed.");
        }

        return result;
    }

    private static string RequiredExact(
        string? value,
        string fieldName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new DomainValidationException($"{fieldName} is malformed.");
        }

        Guard.NoControlCharacters(value, fieldName);
        if (value.Any(char.IsSurrogate))
        {
            throw new DomainValidationException(
                $"{fieldName} cannot contain malformed Unicode.");
        }

        return value;
    }
}

public sealed class JobReport
{
    public const string LocalHostInventoryPackageId =
        "windows.local-host-inventory";
    public const string LocalHostInventoryPackageVersion = "1.0.0";
    public const string LocalHostInventorySchemaVersion = "1.0";
    private static readonly Regex Sha256Pattern = new(
        @"\A[0-9a-f]{64}\z",
        RegexOptions.CultureInvariant);
    private static readonly TimeSpan CreationTolerance = TimeSpan.FromSeconds(5);

    private JobReport(
        JobReportId id,
        JobId jobId,
        ScriptDefinitionId scriptDefinitionId,
        ScriptVersionId scriptVersionId,
        string packageId,
        string packageVersion,
        JobReportType reportType,
        string schemaVersion,
        ReportFormat format,
        WorkerNodeId workerNodeId,
        JobLeaseId leaseId,
        long fencingToken,
        Guid powerShellExecutionId,
        DateTimeOffset createdUtc,
        DateTimeOffset collectedUtc,
        LocalHostInventoryReportPayload inventory,
        string sha256)
    {
        Id = id ?? throw new DomainValidationException(
            "Report identifier is required.");
        JobId = jobId ?? throw new DomainValidationException(
            "Report job identifier is required.");
        ScriptDefinitionId = scriptDefinitionId ??
            throw new DomainValidationException(
                "Report script definition identifier is required.");
        ScriptVersionId = scriptVersionId ??
            throw new DomainValidationException(
                "Report script version identifier is required.");
        PackageId = RequireExact(
            packageId,
            LocalHostInventoryPackageId,
            "Report package identifier");
        PackageVersion = RequireExact(
            packageVersion,
            LocalHostInventoryPackageVersion,
            "Report package version");
        ReportType = EnumGuard.RequireDefined(reportType, nameof(ReportType));
        if (ReportType != JobReportType.LocalHostInventory)
        {
            throw new DomainValidationException("Report type is unsupported.");
        }

        SchemaVersion = RequireExact(
            schemaVersion,
            LocalHostInventorySchemaVersion,
            "Report schema version");
        Format = EnumGuard.RequireDefined(format, nameof(Format));
        if (Format != ReportFormat.Json)
        {
            throw new DomainValidationException("Report format is unsupported.");
        }

        WorkerNodeId = workerNodeId ??
            throw new DomainValidationException(
                "Report worker node identifier is required.");
        LeaseId = leaseId ??
            throw new DomainValidationException(
                "Report lease identifier is required.");
        if (fencingToken <= 0)
        {
            throw new DomainValidationException(
                "Report fencing token must be positive.");
        }

        if (powerShellExecutionId == Guid.Empty)
        {
            throw new DomainValidationException(
                "PowerShell execution identifier is required.");
        }

        CreatedUtc = createdUtc.ToUniversalTime();
        CollectedUtc = collectedUtc.ToUniversalTime();
        if (CollectedUtc > CreatedUtc + CreationTolerance)
        {
            throw new DomainValidationException(
                "Report collection timestamp cannot follow report creation.");
        }

        Inventory = inventory ??
            throw new DomainValidationException(
                "Local Host Inventory report detail is required.");
        if (sha256 is null || !Sha256Pattern.IsMatch(sha256))
        {
            throw new DomainValidationException(
                "Report SHA-256 digest must be 64 lowercase hexadecimal characters.");
        }

        FencingToken = fencingToken;
        PowerShellExecutionId = powerShellExecutionId;
        Sha256 = sha256;
    }

    public JobReportId Id { get; }
    public JobId JobId { get; }
    public ScriptDefinitionId ScriptDefinitionId { get; }
    public ScriptVersionId ScriptVersionId { get; }
    public string PackageId { get; }
    public string PackageVersion { get; }
    public JobReportType ReportType { get; }
    public string SchemaVersion { get; }
    public ReportFormat Format { get; }
    public WorkerNodeId WorkerNodeId { get; }
    public JobLeaseId LeaseId { get; }
    public long FencingToken { get; }
    public Guid PowerShellExecutionId { get; }
    public DateTimeOffset CreatedUtc { get; }
    public DateTimeOffset CollectedUtc { get; }
    public LocalHostInventoryReportPayload Inventory { get; }
    public string Sha256 { get; }

    public static JobReport CreateLocalHostInventory(
        JobId jobId,
        ScriptDefinitionId scriptDefinitionId,
        ScriptVersionId scriptVersionId,
        WorkerNodeId workerNodeId,
        JobLeaseId leaseId,
        long fencingToken,
        Guid powerShellExecutionId,
        DateTimeOffset createdUtc,
        DateTimeOffset collectedUtc,
        LocalHostInventoryReportPayload inventory,
        string sha256) =>
        new(
            CreateDeterministicId(jobId),
            jobId,
            scriptDefinitionId,
            scriptVersionId,
            LocalHostInventoryPackageId,
            LocalHostInventoryPackageVersion,
            JobReportType.LocalHostInventory,
            LocalHostInventorySchemaVersion,
            ReportFormat.Json,
            workerNodeId,
            leaseId,
            fencingToken,
            powerShellExecutionId,
            createdUtc,
            collectedUtc,
            inventory,
            sha256);

    internal static JobReport Rehydrate(
        JobReportId id,
        JobId jobId,
        ScriptDefinitionId scriptDefinitionId,
        ScriptVersionId scriptVersionId,
        string packageId,
        string packageVersion,
        JobReportType reportType,
        string schemaVersion,
        ReportFormat format,
        WorkerNodeId workerNodeId,
        JobLeaseId leaseId,
        long fencingToken,
        Guid powerShellExecutionId,
        DateTimeOffset createdUtc,
        DateTimeOffset collectedUtc,
        LocalHostInventoryReportPayload inventory,
        string sha256)
    {
        var report = new JobReport(
            id,
            jobId,
            scriptDefinitionId,
            scriptVersionId,
            packageId,
            packageVersion,
            reportType,
            schemaVersion,
            format,
            workerNodeId,
            leaseId,
            fencingToken,
            powerShellExecutionId,
            createdUtc,
            collectedUtc,
            inventory,
            sha256);
        if (report.Id != CreateDeterministicId(report.JobId))
        {
            throw new DomainValidationException(
                "Persisted report identity is not deterministic for its job.");
        }

        return report;
    }

    public static JobReportId CreateDeterministicId(JobId jobId)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        var identity = string.Create(
            CultureInfo.InvariantCulture,
            $"windows-script-runner/job-report/v1\n{jobId.Value:D}\n{LocalHostInventoryPackageId}\n{LocalHostInventorySchemaVersion}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, guidBytes.Length).CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0f) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3f) | 0x80);
        return new JobReportId(new Guid(guidBytes));
    }

    private static string RequireExact(
        string? actual,
        string expected,
        string fieldName)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new DomainValidationException($"{fieldName} is unsupported.");
        }

        return expected;
    }
}
