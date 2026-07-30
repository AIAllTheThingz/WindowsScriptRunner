using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace WindowsScriptRunner.PowerShell;

public sealed record PowerShellExecutionId
{
    public PowerShellExecutionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "PowerShellExecutionId cannot contain an empty GUID.",
                nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static PowerShellExecutionId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public enum PowerShellArgumentSensitivity
{
    NonSensitive,
    Sensitive,
}

public sealed record PowerShellArgument(
    string Name,
    string Value,
    PowerShellArgumentSensitivity Sensitivity = PowerShellArgumentSensitivity.NonSensitive);

public sealed class TrustedPowerShellScript
{
    private readonly FrozenSet<string> _allowedParameterNames;

    internal TrustedPowerShellScript(
        string artifactName,
        string canonicalPath,
        string sha256,
        IEnumerable<string> allowedParameterNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactName);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        ArgumentNullException.ThrowIfNull(allowedParameterNames);

        ArtifactName = artifactName;
        CanonicalPath = canonicalPath;
        Sha256 = sha256;
        _allowedParameterNames = allowedParameterNames.ToFrozenSet(
            StringComparer.OrdinalIgnoreCase);
    }

    public string ArtifactName { get; }

    public string CanonicalPath { get; }

    public string Sha256 { get; }

    public IReadOnlySet<string> AllowedParameterNames => _allowedParameterNames;
}

public sealed record PowerShellExecutionRequest
{
    public PowerShellExecutionRequest(
        PowerShellExecutionId executionId,
        TrustedPowerShellScript script,
        IEnumerable<PowerShellArgument> arguments,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(arguments);

        ExecutionId = executionId;
        Script = script;
        Arguments = new ReadOnlyCollection<PowerShellArgument>(arguments.ToArray());
        Timeout = timeout;
    }

    public PowerShellExecutionId ExecutionId { get; }

    public TrustedPowerShellScript Script { get; }

    public IReadOnlyList<PowerShellArgument> Arguments { get; }

    public TimeSpan? Timeout { get; }
}

public sealed record PowerShellRuntimeInfo(
    string ExecutablePath,
    Version Version,
    string PSEdition,
    string Platform,
    string OperatingSystem,
    string ProcessArchitecture,
    bool IsPreview);

public enum PowerShellTerminationReason
{
    Exited,
    TimedOut,
    OutputLimitExceeded,
}

public sealed record PowerShellExecutionResult(
    PowerShellExecutionId ExecutionId,
    PowerShellRuntimeInfo Runtime,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    TimeSpan Duration,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    long StandardOutputBytes,
    long StandardErrorBytes,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    PowerShellTerminationReason TerminationReason);

public interface IPowerShellExecutionBoundary
{
    Task<PowerShellExecutionResult> ExecuteAsync(
        PowerShellExecutionRequest request,
        CancellationToken cancellationToken);
}

public interface IPowerShellExecutableLocator
{
    Task<PowerShellRuntimeInfo> LocateAsync(CancellationToken cancellationToken);
}
