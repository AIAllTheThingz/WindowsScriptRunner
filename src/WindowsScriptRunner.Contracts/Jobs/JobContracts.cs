namespace WindowsScriptRunner.Contracts.Jobs;

public sealed record CreateJobRequest(
    Guid ScriptDefinitionId,
    Guid ScriptVersionId,
    string RequestedPhase,
    string? Description,
    string? ChangeReference);

public sealed record CreateJobResponse(Guid JobId);

public sealed record AddJobTargetRequest(Guid JobId, string TargetName);

public sealed record SetJobParameterRequest(Guid JobId, string Name, string? SerializedValue);

public sealed record SubmitJobRequest(Guid JobId);

public sealed record TransitionJobRequest(Guid JobId, string NewStatus);

public sealed record CompleteValidationJobRequest(Guid JobId);

public sealed record CompleteDryRunJobRequest(Guid JobId);

public sealed record RecordExecutionOutcomeRequest(
    Guid JobId,
    string Outcome,
    int? ExitCode,
    string? Summary);

public sealed record ApproveJobRequest(
    Guid JobId,
    string ApprovalFingerprint,
    string? Comment);

public sealed record RejectJobRequest(
    Guid JobId,
    string ApprovalFingerprint,
    string? Comment);

public sealed record JobSummaryResponse(
    Guid Id,
    Guid ScriptDefinitionId,
    Guid ScriptVersionId,
    string RequestedPhase,
    string Status,
    string RequestedBy,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? SubmittedUtc,
    string? Description);

public sealed record JobDetailResponse(
    Guid Id,
    Guid ScriptDefinitionId,
    Guid ScriptVersionId,
    string RequestedPhase,
    string Status,
    string RequestedBy,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? SubmittedUtc,
    string? Description,
    string? ChangeReference,
    IReadOnlyList<JobTargetResponse> Targets,
    IReadOnlyList<JobParameterResponse> Parameters,
    IReadOnlyList<JobExecutionResponse> Executions,
    IReadOnlyList<JobApprovalResponse> Approvals);

public sealed record JobTargetResponse(
    string Name,
    DateTimeOffset AddedUtc,
    string AddedBy);

public sealed record JobParameterResponse(
    string Name,
    string ParameterType,
    string? DisplayValue,
    bool IsSensitive,
    bool IsRedacted);

public sealed record JobExecutionResponse(
    Guid Id,
    int AttemptNumber,
    Guid? WorkerNodeId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? CompletedUtc,
    string? Outcome,
    int? ExitCode,
    string? Summary);

public sealed record JobApprovalResponse(
    Guid Id,
    string Decision,
    string Approver,
    DateTimeOffset DecisionUtc,
    string? Comment);
