using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WindowsScriptRunner.Reporting;

public sealed record LocalHostInventoryProcessResult
{
    public LocalHostInventoryProcessResult(
        Guid executionId,
        DateTimeOffset startedUtc,
        DateTimeOffset completedUtc,
        int? exitCode,
        string standardOutput,
        string standardError,
        bool standardOutputTruncated,
        bool standardErrorTruncated,
        bool exited)
    {
        ExecutionId = executionId;
        StartedUtc = startedUtc;
        CompletedUtc = completedUtc;
        ExitCode = exitCode;
        StandardOutput = standardOutput ??
            throw new ArgumentNullException(nameof(standardOutput));
        StandardError = standardError ??
            throw new ArgumentNullException(nameof(standardError));
        StandardOutputTruncated = standardOutputTruncated;
        StandardErrorTruncated = standardErrorTruncated;
        Exited = exited;
    }

    public Guid ExecutionId { get; }
    public DateTimeOffset StartedUtc { get; }
    public DateTimeOffset CompletedUtc { get; }
    public int? ExitCode { get; }
    public string StandardOutput { get; }
    public string StandardError { get; }
    public bool StandardOutputTruncated { get; }
    public bool StandardErrorTruncated { get; }
    public bool Exited { get; }
}

public sealed record ValidatedLocalHostInventoryReport
{
    internal ValidatedLocalHostInventoryReport(
        Guid executionId,
        DateTimeOffset startedUtc,
        DateTimeOffset completedUtc,
        DateTimeOffset collectedUtc,
        string computerName,
        string osDescription,
        string osVersion,
        string osArchitecture,
        string powerShellVersion)
    {
        ExecutionId = executionId;
        StartedUtc = startedUtc;
        CompletedUtc = completedUtc;
        CollectedUtc = collectedUtc;
        ComputerName = computerName;
        OsDescription = osDescription;
        OsVersion = osVersion;
        OsArchitecture = osArchitecture;
        PowerShellVersion = powerShellVersion;
    }

    public Guid ExecutionId { get; }
    public DateTimeOffset StartedUtc { get; }
    public DateTimeOffset CompletedUtc { get; }
    public DateTimeOffset CollectedUtc { get; }
    public string ComputerName { get; }
    public string OsDescription { get; }
    public string OsVersion { get; }
    public string OsArchitecture { get; }
    public string PowerShellVersion { get; }
}

public sealed class LocalHostInventoryReportValidationException : Exception
{
    public LocalHostInventoryReportValidationException(string message)
        : base(message)
    {
    }

    public LocalHostInventoryReportValidationException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class LocalHostInventoryReportParser
{
    public const int MaximumDocumentUtf8Bytes = 8 * 1024;
    public const int MaximumDocumentCharacters = 8 * 1024;
    public const int MaximumComputerNameLength = 63;
    public const int MaximumOsDescriptionLength = 256;
    public const int MaximumVersionLength = 32;
    public const int MaximumJsonDepth = 4;
    public static readonly TimeSpan CollectionTimestampTolerance =
        TimeSpan.FromSeconds(5);

    private const string ExpectedSchemaVersion = "1.0";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex ComputerNamePattern = new(
        @"\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new(
        @"\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*)){1,2}\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex RoundTripTimestampPattern = new(
        @"(?:Z|[+-][0-9]{2}:[0-9]{2})\z",
        RegexOptions.CultureInvariant);
    private static readonly HashSet<string> SupportedArchitectures =
        new(StringComparer.Ordinal)
        {
            "X86",
            "X64",
            "Arm",
            "Arm64",
        };
    private static readonly Version MinimumPowerShellVersion = new(7, 4, 0);

    public ValidatedLocalHostInventoryReport Parse(
        LocalHostInventoryProcessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ValidateProcessResult(result);

        byte[] utf8;
        try
        {
            utf8 = StrictUtf8.GetBytes(result.StandardOutput);
        }
        catch (EncoderFallbackException exception)
        {
            throw Invalid("Inventory output contains malformed Unicode.", exception);
        }

        if (result.StandardOutput.Length == 0 ||
            result.StandardOutput.Length > MaximumDocumentCharacters ||
            utf8.Length > MaximumDocumentUtf8Bytes)
        {
            throw Invalid("Inventory output is empty or exceeds the allowed size.");
        }

        try
        {
            return ParseDocument(utf8, result);
        }
        catch (JsonException exception)
        {
            throw Invalid("Inventory output is not valid strict JSON.", exception);
        }
    }

    private static ValidatedLocalHostInventoryReport ParseDocument(
        ReadOnlySpan<byte> utf8,
        LocalHostInventoryProcessResult processResult)
    {
        var reader = new Utf8JsonReader(
            utf8,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumJsonDepth,
            });
        RequireRead(ref reader, JsonTokenType.StartObject);
        var rootProperties = new HashSet<string>(StringComparer.Ordinal);
        string? schemaVersion = null;
        string? computerName = null;
        string? osDescription = null;
        string? osVersion = null;
        string? osArchitecture = null;
        string? powerShellVersion = null;
        string? collectedUtcText = null;

        while (ReadPropertyNameOrEnd(ref reader, rootProperties, out var propertyName))
        {
            switch (propertyName)
            {
                case "schemaVersion":
                    schemaVersion = ReadRequiredString(ref reader, "schemaVersion");
                    break;
                case "computerName":
                    computerName = ReadRequiredString(ref reader, "computerName");
                    break;
                case "os":
                    ReadOs(
                        ref reader,
                        out osDescription,
                        out osVersion,
                        out osArchitecture);
                    break;
                case "powerShell":
                    ReadPowerShell(ref reader, out powerShellVersion);
                    break;
                case "collectedUtc":
                    collectedUtcText = ReadRequiredString(ref reader, "collectedUtc");
                    break;
                default:
                    throw Invalid("Inventory output contains an unexpected property.");
            }
        }

        if (reader.Read())
        {
            throw Invalid("Inventory output contains trailing content.");
        }

        RequireExactProperties(
            rootProperties,
            "schemaVersion",
            "computerName",
            "os",
            "powerShell",
            "collectedUtc");
        if (!string.Equals(schemaVersion, ExpectedSchemaVersion, StringComparison.Ordinal))
        {
            throw Invalid("Inventory schema version is unsupported.");
        }

        computerName = ValidateComputerName(computerName);
        osDescription = ValidateText(
            osDescription,
            "OS description",
            MaximumOsDescriptionLength);
        osVersion = ValidateVersion(osVersion, "OS version", minimum: null);
        osArchitecture = ValidateArchitecture(osArchitecture);
        powerShellVersion = ValidateVersion(
            powerShellVersion,
            "PowerShell version",
            MinimumPowerShellVersion);
        var collectedUtc = ParseCollectedUtc(collectedUtcText);
        if (collectedUtc < processResult.StartedUtc.ToUniversalTime() -
            CollectionTimestampTolerance ||
            collectedUtc > processResult.CompletedUtc.ToUniversalTime() +
            CollectionTimestampTolerance)
        {
            throw Invalid(
                "Inventory collection timestamp falls outside the execution window.");
        }

        return new ValidatedLocalHostInventoryReport(
            processResult.ExecutionId,
            processResult.StartedUtc.ToUniversalTime(),
            processResult.CompletedUtc.ToUniversalTime(),
            collectedUtc,
            computerName,
            osDescription,
            osVersion,
            osArchitecture,
            powerShellVersion);
    }

    private static void ReadOs(
        ref Utf8JsonReader reader,
        out string? description,
        out string? version,
        out string? architecture)
    {
        RequireRead(ref reader, JsonTokenType.StartObject);
        var properties = new HashSet<string>(StringComparer.Ordinal);
        description = null;
        version = null;
        architecture = null;
        while (ReadPropertyNameOrEnd(ref reader, properties, out var propertyName))
        {
            switch (propertyName)
            {
                case "description":
                    description = ReadRequiredString(ref reader, "os.description");
                    break;
                case "version":
                    version = ReadRequiredString(ref reader, "os.version");
                    break;
                case "architecture":
                    architecture = ReadRequiredString(ref reader, "os.architecture");
                    break;
                default:
                    throw Invalid("Inventory OS data contains an unexpected property.");
            }
        }

        RequireExactProperties(properties, "description", "version", "architecture");
    }

    private static void ReadPowerShell(
        ref Utf8JsonReader reader,
        out string? version)
    {
        RequireRead(ref reader, JsonTokenType.StartObject);
        var properties = new HashSet<string>(StringComparer.Ordinal);
        version = null;
        while (ReadPropertyNameOrEnd(ref reader, properties, out var propertyName))
        {
            switch (propertyName)
            {
                case "version":
                    version = ReadRequiredString(ref reader, "powerShell.version");
                    break;
                default:
                    throw Invalid(
                        "Inventory PowerShell data contains an unexpected property.");
            }
        }

        RequireExactProperties(properties, "version");
    }

    private static bool ReadPropertyNameOrEnd(
        ref Utf8JsonReader reader,
        HashSet<string> properties,
        out string propertyName)
    {
        if (!reader.Read())
        {
            throw Invalid("Inventory JSON ended before the current object was complete.");
        }

        if (reader.TokenType == JsonTokenType.EndObject)
        {
            propertyName = string.Empty;
            return false;
        }

        if (reader.TokenType != JsonTokenType.PropertyName)
        {
            throw Invalid("Inventory JSON object contains an invalid token.");
        }

        propertyName = reader.GetString() ??
            throw Invalid("Inventory JSON contains an invalid property name.");
        if (!properties.Add(propertyName))
        {
            throw Invalid("Inventory JSON contains a duplicate property.");
        }

        return true;
    }

    private static string ReadRequiredString(
        ref Utf8JsonReader reader,
        string fieldName)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.String)
        {
            throw Invalid($"Inventory field '{fieldName}' must be a string.");
        }

        return reader.GetString() ??
            throw Invalid($"Inventory field '{fieldName}' cannot be null.");
    }

    private static void RequireRead(
        ref Utf8JsonReader reader,
        JsonTokenType tokenType)
    {
        if (!reader.Read() || reader.TokenType != tokenType)
        {
            throw Invalid("Inventory output does not contain the expected JSON object.");
        }
    }

    private static void RequireExactProperties(
        HashSet<string> actual,
        params string[] expected)
    {
        if (actual.Count != expected.Length ||
            expected.Any(property => !actual.Contains(property)))
        {
            throw Invalid("Inventory output is missing a required property.");
        }
    }

    private static void ValidateProcessResult(
        LocalHostInventoryProcessResult result)
    {
        if (result.ExecutionId == Guid.Empty ||
            !result.Exited ||
            result.ExitCode != 0 ||
            result.StandardOutputTruncated ||
            result.StandardErrorTruncated)
        {
            throw Invalid(
                "Inventory parsing requires a successful, complete process result.");
        }

        if (result.StartedUtc > result.CompletedUtc)
        {
            throw Invalid("Inventory process timestamps are inconsistent.");
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            throw Invalid("Successful inventory execution produced error output.");
        }
    }

    private static string ValidateComputerName(string? value)
    {
        var result = ValidateText(
            value,
            "computer name",
            MaximumComputerNameLength);
        if (!ComputerNamePattern.IsMatch(result))
        {
            throw Invalid("Inventory computer name is malformed.");
        }

        return result;
    }

    private static string ValidateText(
        string? value,
        string fieldName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.Any(char.IsControl) ||
            value.Any(char.IsSurrogate))
        {
            throw Invalid($"Inventory {fieldName} is malformed.");
        }

        return value;
    }

    private static string ValidateVersion(
        string? value,
        string fieldName,
        Version? minimum)
    {
        var result = ValidateText(value, fieldName, MaximumVersionLength);
        if (!VersionPattern.IsMatch(result) ||
            !Version.TryParse(result, out var parsed) ||
            (minimum is not null && parsed < minimum))
        {
            throw Invalid($"Inventory {fieldName} is unsupported or malformed.");
        }

        return result;
    }

    private static string ValidateArchitecture(string? value)
    {
        var result = ValidateText(value, "OS architecture", 8);
        if (!SupportedArchitectures.Contains(result))
        {
            throw Invalid("Inventory OS architecture is unsupported.");
        }

        return result;
    }

    private static DateTimeOffset ParseCollectedUtc(string? value)
    {
        var text = ValidateText(value, "collection timestamp", 40);
        if (!RoundTripTimestampPattern.IsMatch(text) ||
            !DateTimeOffset.TryParseExact(
                text,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            throw Invalid("Inventory collection timestamp is malformed.");
        }

        return parsed.ToUniversalTime();
    }

    private static LocalHostInventoryReportValidationException Invalid(
        string message,
        Exception? innerException = null) =>
        innerException is null
            ? new LocalHostInventoryReportValidationException(message)
            : new LocalHostInventoryReportValidationException(message, innerException);
}

public sealed record LocalHostInventoryCanonicalReport(
    Guid JobId,
    Guid ScriptDefinitionId,
    Guid ScriptVersionId,
    Guid WorkerNodeId,
    Guid LeaseId,
    long FencingToken,
    Guid PowerShellExecutionId,
    DateTimeOffset CollectedUtc,
    string ComputerName,
    string OsDescription,
    string OsVersion,
    string OsArchitecture,
    string PowerShellVersion);

public static class LocalHostInventoryCanonicalizer
{
    public static string CreateSha256(LocalHostInventoryCanonicalReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("packageId", "windows.local-host-inventory");
            writer.WriteString("packageVersion", "1.0.0");
            writer.WriteString("reportType", "LocalHostInventory");
            writer.WriteString("schemaVersion", "1.0");
            writer.WriteString("format", "Json");
            writer.WriteString("jobId", report.JobId);
            writer.WriteString("scriptDefinitionId", report.ScriptDefinitionId);
            writer.WriteString("scriptVersionId", report.ScriptVersionId);
            writer.WriteString("workerNodeId", report.WorkerNodeId);
            writer.WriteString("leaseId", report.LeaseId);
            writer.WriteNumber("fencingToken", report.FencingToken);
            writer.WriteString("powerShellExecutionId", report.PowerShellExecutionId);
            writer.WriteString(
                "collectedUtc",
                report.CollectedUtc.ToUniversalTime().ToString(
                    "O",
                    CultureInfo.InvariantCulture));
            writer.WriteString("computerName", report.ComputerName);
            writer.WriteString("osDescription", report.OsDescription);
            writer.WriteString("osVersion", report.OsVersion);
            writer.WriteString("osArchitecture", report.OsArchitecture);
            writer.WriteString("powerShellVersion", report.PowerShellVersion);
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }
}
