using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;

namespace WindowsScriptRunner.Application.Jobs;

public sealed class ApprovalFingerprintService(IScriptDefinitionRepository scriptRepository)
    : IJobFingerprintService
{
    private const string FormatVersion = "windows-script-runner-approval-v2";

    public async Task<string> CreateFingerprintAsync(Job job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var definition = await scriptRepository.GetByIdAsync(
            job.ScriptDefinitionId,
            cancellationToken)
            ?? throw new EntityNotFoundException(
                nameof(ScriptDefinition),
                job.ScriptDefinitionId.ToString());
        var version = definition.GetVersion(job.ScriptVersionId);
        ValidateTrustedApprovalState(job, definition, version);

        var canonical = new CanonicalApprovalFingerprintWriter();
        canonical.Write("format", FormatVersion);
        canonical.Write("job-id", job.Id.Value.ToString("D", CultureInfo.InvariantCulture));
        canonical.Write("requester", job.RequestedBy.Value);
        canonical.Write("requested-phase", job.RequestedPhase.ToString());
        canonical.Write("job-status", job.Status.ToString());
        canonical.Write("created-utc", FormatTimestamp(job.CreatedUtc));
        canonical.Write("submitted-utc", FormatTimestamp(job.SubmittedUtc));
        canonical.Write("updated-utc", FormatTimestamp(job.UpdatedUtc));
        canonical.Write("description", job.Description);
        canonical.Write("change-reference", job.ChangeReference?.Value);

        canonical.Write("script-definition-id", definition.Id.Value.ToString("D", CultureInfo.InvariantCulture));
        canonical.Write("script-version-id", version.Id.Value.ToString("D", CultureInfo.InvariantCulture));
        canonical.Write("script-version", version.Version.ToString());
        canonical.Write("script-path", version.RelativeScriptPath);
        canonical.Write("script-sha256", version.Sha256);
        canonical.Write("script-git-commit", version.GitCommitSha);
        canonical.Write("script-minimum-powershell", version.MinimumPowerShellVersion);
        canonical.Write(
            "script-default-timeout-minutes",
            version.DefaultTimeoutMinutes.ToString(CultureInfo.InvariantCulture));
        canonical.Write("script-supported-phases", string.Join(
            ",",
            version.SupportedPhases.OrderBy(value => value).Select(value => value.ToString())));

        var policy = job.PolicySnapshot!;
        canonical.Write("policy-script-definition-id", policy.ScriptDefinitionId.Value.ToString("D", CultureInfo.InvariantCulture));
        canonical.Write("policy-script-version-id", policy.ScriptVersionId.Value.ToString("D", CultureInfo.InvariantCulture));
        canonical.Write("policy-risk", policy.RiskLevel.ToString());
        canonical.Write("policy-supports-execute", policy.SupportsExecutePhase ? "true" : "false");
        canonical.Write("policy-supports-post-validation", policy.SupportsPostValidationPhase ? "true" : "false");

        WriteTargets(canonical, job);
        WriteParameters(canonical, job);
        WriteAcceptedDryRunEvidence(canonical, job.AcceptedDryRunEvidence!);

        return Convert.ToHexStringLower(SHA256.HashData(canonical.ToUtf8Bytes()));
    }

    public bool IsExpectedFingerprintCurrent(string? expectedFingerprint, string currentFingerprint)
    {
        if (expectedFingerprint is not { } expected ||
            !IsLowercaseHexFingerprint(expected) ||
            !IsLowercaseHexFingerprint(currentFingerprint))
        {
            return false;
        }

        var expectedBytes = Convert.FromHexString(expected);
        var currentBytes = Convert.FromHexString(currentFingerprint);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, currentBytes);
    }

    private static void ValidateTrustedApprovalState(
        Job job,
        ScriptDefinition definition,
        ScriptVersion version)
    {
        var policy = job.PolicySnapshot;
        if (job.Status != JobStatus.AwaitingApproval ||
            job.RequestedPhase != ExecutionPhase.Execute ||
            policy is null ||
            policy.ScriptDefinitionId != definition.Id ||
            policy.ScriptVersionId != version.Id ||
            !version.IsPublished ||
            job.AcceptedDryRunEvidence is null ||
            job.AcceptedDryRunEvidence.WorkKind != JobWorkKind.DryRun)
        {
            throw new ApplicationConflictException(
                "The job does not have current accepted dry-run evidence for approval.");
        }
    }

    private static void WriteTargets(CanonicalApprovalFingerprintWriter canonical, Job job)
    {
        var targets = job.Targets
            .OrderBy(target => target.Name.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.Name.Value, StringComparer.Ordinal)
            .ToArray();
        canonical.Write("target-count", targets.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var target in targets)
        {
            canonical.Write("target-name", target.Name.Value);
            canonical.Write("target-added-utc", FormatTimestamp(target.AddedUtc));
            canonical.Write("target-added-by", target.AddedBy.Value);
        }
    }

    private static void WriteParameters(CanonicalApprovalFingerprintWriter canonical, Job job)
    {
        var parameters = job.Parameters
            .OrderBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(parameter => parameter.Name, StringComparer.Ordinal)
            .ToArray();
        canonical.Write("parameter-count", parameters.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var parameter in parameters)
        {
            canonical.Write("parameter-name", parameter.Name);
            canonical.Write("parameter-value", parameter.SerializedValue);
        }
    }

    private static void WriteAcceptedDryRunEvidence(
        CanonicalApprovalFingerprintWriter canonical,
        JobDryRunEvidence evidence)
    {
        canonical.Write("accepted-dry-run-work-kind", evidence.WorkKind.ToString());
        canonical.Write("accepted-dry-run-source", evidence.Source.ToString());
        canonical.Write("accepted-dry-run-worker", evidence.WorkerNodeId?.Value.ToString("D", CultureInfo.InvariantCulture));
        canonical.Write("accepted-dry-run-lease", evidence.LeaseId?.Value.ToString("D", CultureInfo.InvariantCulture));
        canonical.Write("accepted-dry-run-fencing-token", evidence.FencingToken?.ToString(CultureInfo.InvariantCulture));
        canonical.Write("accepted-dry-run-window-opened-utc", FormatTimestamp(evidence.ExecutionWindowOpenedUtc));
        canonical.Write("accepted-dry-run-completed-utc", FormatTimestamp(evidence.CompletedUtc));
    }

    private static string? FormatTimestamp(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static bool IsLowercaseHexFingerprint(string? value) =>
        value is { Length: 64 } && value.All(character =>
            (character is >= '0' and <= '9') || (character is >= 'a' and <= 'f'));

    private sealed class CanonicalApprovalFingerprintWriter
    {
        private readonly StringBuilder _builder = new();

        public void Write(string name, string? value)
        {
            _builder.Append(name);
            _builder.Append(':');
            if (value is null)
            {
                _builder.Append("-1:");
            }
            else
            {
                _builder.Append(Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture));
                _builder.Append(':');
                _builder.Append(value);
            }

            _builder.Append('\n');
        }

        public byte[] ToUtf8Bytes() => Encoding.UTF8.GetBytes(_builder.ToString());
    }
}
