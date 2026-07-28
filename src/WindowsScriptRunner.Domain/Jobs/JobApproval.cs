using System.Text.RegularExpressions;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Domain.Jobs;

public sealed class JobApproval
{
    private static readonly Regex FingerprintPattern = new(
        @"\A[0-9a-fA-F]{64}\z",
        RegexOptions.CultureInvariant);

    public JobApproval(
        JobApprovalId id,
        ApprovalDecision decision,
        UserIdentity approver,
        DateTimeOffset decisionUtc,
        string? comment,
        string approvalFingerprint)
    {
        if (decision == ApprovalDecision.Pending)
        {
            throw new DomainValidationException("A recorded approval decision cannot remain pending.");
        }

        var normalizedComment = comment?.Trim();
        if (normalizedComment?.Length > 2000)
        {
            throw new DomainValidationException("Approval comment cannot exceed 2,000 characters.");
        }

        var normalizedFingerprint = approvalFingerprint?.Trim() ?? string.Empty;
        if (!FingerprintPattern.IsMatch(normalizedFingerprint))
        {
            throw new DomainValidationException(
                "Approval fingerprint must contain exactly 64 hexadecimal characters.");
        }

        Id = id ?? throw new DomainValidationException("Job approval identifier is required.");
        Decision = decision;
        Approver = approver ?? throw new DomainValidationException("Approver is required.");
        DecisionUtc = decisionUtc;
        Comment = normalizedComment;
        ApprovalFingerprint = normalizedFingerprint.ToLowerInvariant();
    }

    public JobApprovalId Id { get; }
    public ApprovalDecision Decision { get; }
    public UserIdentity Approver { get; }
    public DateTimeOffset DecisionUtc { get; }
    public string? Comment { get; }
    public string ApprovalFingerprint { get; }
}
