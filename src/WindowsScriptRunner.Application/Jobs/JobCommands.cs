using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Application.Jobs;

public sealed record CreateDraftJobCommand(
    ScriptDefinitionId ScriptDefinitionId,
    ScriptVersionId ScriptVersionId,
    ExecutionPhase RequestedPhase,
    string? Description = null,
    ChangeReference? ChangeReference = null);

public sealed record AddJobTargetCommand(
    JobId JobId,
    TargetName TargetName,
    UserIdentity ActingUser);

public sealed record SetJobParameterCommand(
    JobId JobId,
    string ParameterName,
    string? SerializedValue,
    UserIdentity ActingUser);

public sealed record SubmitJobCommand(JobId JobId, UserIdentity ActingUser);

public sealed record TransitionJobCommand(
    JobId JobId,
    JobStatus NewStatus,
    UserIdentity ActingUser);

public sealed record ApproveJobCommand(
    JobId JobId,
    string ExpectedFingerprint,
    string? Comment);

public sealed record RejectJobCommand(
    JobId JobId,
    string ExpectedFingerprint,
    string? Comment);

public sealed record CompleteReadOnlyJobCommand(JobId JobId, UserIdentity ActingUser);

public sealed record CompleteValidationJobCommand(JobId JobId, UserIdentity ActingUser);

public sealed record CompleteDryRunJobCommand(JobId JobId, UserIdentity ActingUser);

public sealed record StartExecutionAttemptCommand(
    JobId JobId,
    JobLeaseCredentials LeaseCredentials,
    UserIdentity ActingUser);

public sealed record RecordExecutionOutcomeCommand(
    JobId JobId,
    JobLeaseCredentials LeaseCredentials,
    ExecutionOutcome Outcome,
    int? ExitCode,
    string? Summary,
    UserIdentity ActingUser);

public sealed record GetJobQuery(JobId JobId);

public sealed record ListAwaitingApprovalJobsQuery(int MaximumCount);

public sealed record GetApprovalReviewQuery(JobId JobId);
