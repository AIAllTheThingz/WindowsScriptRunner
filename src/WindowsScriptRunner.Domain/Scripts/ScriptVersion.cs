using System.Text.RegularExpressions;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Domain.Scripts;

public sealed class ScriptVersion
{
    private static readonly Regex Sha256Pattern = new(@"\A[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant);
    private static readonly Regex CommitPattern = new(@"\A[0-9a-fA-F]{7,64}\z", RegexOptions.CultureInvariant);

    private readonly List<ScriptParameterDefinition> _parameterDefinitions = [];
    private readonly IReadOnlyCollection<ExecutionPhase> _supportedPhases;
    private readonly IReadOnlyCollection<ReportFormat> _supportedReportFormats;

    public ScriptVersion(
        ScriptVersionId id,
        ScriptVersionNumber version,
        string relativeScriptPath,
        string sha256,
        string? gitCommitSha,
        string minimumPowerShellVersion,
        int defaultTimeoutMinutes,
        IEnumerable<ExecutionPhase> supportedPhases,
        IEnumerable<ReportFormat>? supportedReportFormats,
        DateTimeOffset createdUtc,
        UserIdentity createdBy)
    {
        Id = id ?? throw new DomainValidationException("Script version identifier is required.");
        Version = version;
        RelativeScriptPath = ValidateRelativePath(relativeScriptPath);
        Sha256 = ValidateSha256(sha256);
        GitCommitSha = ValidateCommitSha(gitCommitSha);
        MinimumPowerShellVersion = Guard.RequiredTrimmed(
            minimumPowerShellVersion,
            nameof(MinimumPowerShellVersion),
            50);
        DefaultTimeoutMinutes = defaultTimeoutMinutes is >= 1 and <= 480
            ? defaultTimeoutMinutes
            : throw new InvalidScriptVersionException("Default timeout must be between 1 and 480 minutes.");
        _supportedPhases = NormalizeUnique(supportedPhases, "supported execution phases", requireValue: true);
        _supportedReportFormats = NormalizeUnique(
            supportedReportFormats ?? [],
            "supported report formats",
            requireValue: false);
        CreatedUtc = createdUtc;
        CreatedBy = createdBy ?? throw new DomainValidationException("Version creator is required.");
    }

    public ScriptVersionId Id { get; }
    public ScriptVersionNumber Version { get; }
    public string RelativeScriptPath { get; }
    public string Sha256 { get; }
    public string? GitCommitSha { get; }
    public string MinimumPowerShellVersion { get; }
    public int DefaultTimeoutMinutes { get; }
    public bool IsPublished { get; private set; }
    public DateTimeOffset CreatedUtc { get; }
    public UserIdentity CreatedBy { get; }
    public IReadOnlyCollection<ExecutionPhase> SupportedPhases => _supportedPhases;
    public IReadOnlyCollection<ScriptParameterDefinition> ParameterDefinitions => _parameterDefinitions.AsReadOnly();
    public IReadOnlyCollection<ReportFormat> SupportedReportFormats => _supportedReportFormats;

    public void AddParameterDefinition(ScriptParameterDefinition definition)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(definition);

        if (_parameterDefinitions.Any(existing => existing.Id == definition.Id))
        {
            throw new InvalidParameterDefinitionException(
                $"Parameter definition identifier '{definition.Id}' already exists in version {Version}.");
        }

        if (_parameterDefinitions.Any(
            existing => string.Equals(existing.Name, definition.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidParameterDefinitionException(
                $"Parameter name '{definition.Name}' already exists in version {Version}.");
        }

        _parameterDefinitions.Add(definition);
    }

    public void Publish()
    {
        if (IsPublished)
        {
            throw new InvalidScriptVersionException($"Version {Version} is already published.");
        }

        if (_supportedPhases.Contains(ExecutionPhase.Execute) &&
            !_supportedPhases.Contains(ExecutionPhase.DryRun))
        {
            throw new InvalidScriptVersionException("Published Execute-capable versions must also support DryRun.");
        }

        IsPublished = true;
    }

    public ScriptParameterDefinition GetParameterDefinition(string name) =>
        _parameterDefinitions.SingleOrDefault(
            definition => string.Equals(definition.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidJobParameterException(name, "the script version does not define this parameter.");

    private void EnsureMutable()
    {
        if (IsPublished)
        {
            throw new InvalidScriptVersionException($"Published version {Version} is immutable.");
        }
    }

    private static string ValidateRelativePath(string value)
    {
        var path = Guard.RequiredTrimmed(value, "Relative script path", 500);
        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (Path.IsPathRooted(path) ||
            path.StartsWith('\\') ||
            path.StartsWith('/') ||
            path.Contains(':') ||
            path.Contains("..", StringComparison.Ordinal) ||
            segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidScriptVersionException("Script path must be relative and cannot contain traversal markers.");
        }

        return path;
    }

    private static string ValidateSha256(string value)
    {
        var hash = Guard.RequiredTrimmed(value, "SHA-256", 64);
        return Sha256Pattern.IsMatch(hash)
            ? hash.ToLowerInvariant()
            : throw new InvalidScriptVersionException("SHA-256 must contain exactly 64 hexadecimal characters.");
    }

    private static string? ValidateCommitSha(string? value)
    {
        var hash = value?.Trim();
        if (string.IsNullOrEmpty(hash))
        {
            return null;
        }

        return CommitPattern.IsMatch(hash)
            ? hash.ToLowerInvariant()
            : throw new InvalidScriptVersionException("Git commit SHA must contain 7 to 64 hexadecimal characters.");
    }

    private static IReadOnlyCollection<T> NormalizeUnique<T>(
        IEnumerable<T> values,
        string fieldName,
        bool requireValue)
        where T : struct, Enum
    {
        var normalized = values
            .Select(value => EnumGuard.RequireDefined(value, fieldName))
            .ToArray();
        if (requireValue && normalized.Length == 0)
        {
            throw new InvalidScriptVersionException($"At least one {fieldName} value is required.");
        }

        if (normalized.Distinct().Count() != normalized.Length)
        {
            throw new InvalidScriptVersionException($"{fieldName} cannot contain duplicates.");
        }

        return Array.AsReadOnly(normalized);
    }
}
