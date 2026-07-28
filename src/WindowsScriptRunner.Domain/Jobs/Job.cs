using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Domain.Jobs;

public sealed class Job
{
    private readonly List<JobTarget> _targets = [];
    private readonly List<JobParameter> _parameters = [];
    private readonly List<JobExecution> _executions = [];
    private readonly List<JobApproval> _approvals = [];

    private Job(
        JobId id,
        ScriptDefinitionId scriptDefinitionId,
        ScriptVersionId scriptVersionId,
        ExecutionPhase requestedPhase,
        UserIdentity requestedBy,
        DateTimeOffset createdUtc,
        string? description,
        ChangeReference? changeReference)
    {
        Id = id ?? throw new DomainValidationException("Job identifier is required.");
        ScriptDefinitionId = scriptDefinitionId ?? throw new DomainValidationException("Script definition identifier is required.");
        ScriptVersionId = scriptVersionId ?? throw new DomainValidationException("Script version identifier is required.");
        RequestedPhase = requestedPhase;
        RequestedBy = requestedBy ?? throw new DomainValidationException("Requester is required.");
        CreatedUtc = createdUtc;
        UpdatedUtc = createdUtc;
        LastActingUser = requestedBy;
        Description = ValidateDescription(description);
        ChangeReference = changeReference;
        Status = JobStatus.Draft;
    }

    public JobId Id { get; }
    public ScriptDefinitionId ScriptDefinitionId { get; }
    public ScriptVersionId ScriptVersionId { get; }
    public ExecutionPhase RequestedPhase { get; }
    public JobStatus Status { get; private set; }
    public UserIdentity RequestedBy { get; }
    public UserIdentity LastActingUser { get; private set; }
    public DateTimeOffset CreatedUtc { get; }
    public DateTimeOffset UpdatedUtc { get; private set; }
    public DateTimeOffset? SubmittedUtc { get; private set; }
    public JobPolicySnapshot? PolicySnapshot { get; private set; }
    public ChangeReference? ChangeReference { get; private set; }
    public string? Description { get; private set; }
    public IReadOnlyCollection<JobTarget> Targets => _targets.AsReadOnly();
    public IReadOnlyCollection<JobParameter> Parameters => _parameters.AsReadOnly();
    public IReadOnlyCollection<JobExecution> Executions => _executions.AsReadOnly();
    public IReadOnlyCollection<JobApproval> Approvals => _approvals.AsReadOnly();

    public static Job CreateDraft(
        JobId id,
        ScriptDefinitionId scriptDefinitionId,
        ScriptVersionId scriptVersionId,
        ExecutionPhase requestedPhase,
        UserIdentity requestedBy,
        DateTimeOffset createdUtc,
        string? description = null,
        ChangeReference? changeReference = null) =>
        new(
            id,
            scriptDefinitionId,
            scriptVersionId,
            requestedPhase,
            requestedBy,
            createdUtc,
            description,
            changeReference);

    public void AddTarget(TargetName targetName, UserIdentity actingUser, DateTimeOffset addedUtc)
    {
        EnsureDraft();
        ValidateMutation(actingUser, addedUtc);
        if (_targets.Any(target => target.Name.Equals(targetName)))
        {
            throw new DuplicateJobTargetException(targetName.Value);
        }

        var target = new JobTarget(targetName, addedUtc, actingUser);
        _targets.Add(target);
        ApplyTouch(actingUser, addedUtc);
    }

    public void RemoveTarget(TargetName targetName, UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureDraft();
        ValidateMutation(actingUser, updatedUtc);
        var target = _targets.SingleOrDefault(existing => existing.Name.Equals(targetName));
        if (target is null)
        {
            throw new DomainValidationException($"Target '{targetName}' is not part of the job.");
        }

        _targets.Remove(target);
        ApplyTouch(actingUser, updatedUtc);
    }

    public void SetParameter(
        ScriptParameterDefinition definition,
        string? serializedValue,
        UserIdentity actingUser,
        DateTimeOffset updatedUtc)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(definition);
        definition.ValidateSerializedValue(serializedValue);
        ValidateMutation(actingUser, updatedUtc);
        var replacement = new JobParameter(
            definition.Name,
            serializedValue,
            definition.ParameterType,
            definition.IsSensitive);
        var existing = _parameters.SingleOrDefault(parameter =>
            string.Equals(parameter.Name, definition.Name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            _parameters.Remove(existing);
        }

        _parameters.Add(replacement);
        ApplyTouch(actingUser, updatedUtc);
    }

    public void RemoveParameter(string name, UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureDraft();
        ValidateMutation(actingUser, updatedUtc);
        var existing = _parameters.SingleOrDefault(parameter =>
            string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            throw new InvalidJobParameterException(name, "the job does not contain this parameter.");
        }

        _parameters.Remove(existing);
        ApplyTouch(actingUser, updatedUtc);
    }

    public void UpdateDescription(string? description, UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureDraft();
        var normalized = ValidateDescription(description);
        ValidateMutation(actingUser, updatedUtc);
        Description = normalized;
        ApplyTouch(actingUser, updatedUtc);
    }

    public void SetChangeReference(
        ChangeReference? changeReference,
        UserIdentity actingUser,
        DateTimeOffset updatedUtc)
    {
        EnsureDraft();
        ValidateMutation(actingUser, updatedUtc);
        ChangeReference = changeReference;
        ApplyTouch(actingUser, updatedUtc);
    }

    public void Submit(
        ScriptDefinition scriptDefinition,
        UserIdentity actingUser,
        DateTimeOffset submittedUtc)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(scriptDefinition);
        ValidateTransition(JobStatus.Submitted, actingUser, submittedUtc);
        if (scriptDefinition.Id != ScriptDefinitionId)
        {
            throw new DomainValidationException("The supplied script definition does not match the job.");
        }

        if (!scriptDefinition.IsEnabled)
        {
            throw new DomainValidationException("Disabled script definitions cannot be submitted.");
        }

        if (RequestedPhase is not (ExecutionPhase.Validation or ExecutionPhase.DryRun or ExecutionPhase.Execute))
        {
            throw new DomainValidationException(
                $"Requested phase {RequestedPhase} is not supported by the Phase 2 lifecycle.");
        }

        var version = scriptDefinition.GetVersion(ScriptVersionId);
        if (!version.SupportedPhases.Contains(RequestedPhase))
        {
            throw new DomainValidationException($"Script version does not support the {RequestedPhase} phase.");
        }

        if (_targets.Count == 0)
        {
            throw new DomainValidationException("At least one target is required before job submission.");
        }

        foreach (var definition in version.ParameterDefinitions)
        {
            var parameter = _parameters.SingleOrDefault(candidate =>
                string.Equals(candidate.Name, definition.Name, StringComparison.OrdinalIgnoreCase));
            definition.ValidateSerializedValue(parameter?.SerializedValue);
        }

        foreach (var parameter in _parameters)
        {
            version.GetParameterDefinition(parameter.Name).ValidateSerializedValue(parameter.SerializedValue);
        }

        var policySnapshot = JobPolicySnapshot.Capture(scriptDefinition, ScriptVersionId);
        PolicySnapshot = policySnapshot;
        SubmittedUtc = submittedUtc;
        ApplyValidatedTransition(JobStatus.Submitted, actingUser, submittedUtc);
    }

    public void MarkValidated(UserIdentity actingUser, DateTimeOffset updatedUtc) =>
        ApplyTransition(JobStatus.Validated, actingUser, updatedUtc);

    public void QueueDryRun(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureRequestedPhase(
            ExecutionPhase.DryRun,
            ExecutionPhase.Execute,
            "Only DryRun or Execute requests can queue dry-run work.");
        ApplyTransition(JobStatus.DryRunQueued, actingUser, updatedUtc);
    }

    public void StartDryRun(UserIdentity actingUser, DateTimeOffset updatedUtc) =>
        ApplyTransition(JobStatus.DryRunRunning, actingUser, updatedUtc);

    public void CompleteDryRun(UserIdentity actingUser, DateTimeOffset updatedUtc) =>
        ApplyTransition(JobStatus.DryRunCompleted, actingUser, updatedUtc);

    public void CompleteRequestedValidation(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureRequestedPhase(
            ExecutionPhase.Validation,
            "Only Validation requests can complete immediately after validation.");
        ApplyTransition(JobStatus.Completed, actingUser, updatedUtc);
    }

    public void CompleteRequestedDryRun(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureRequestedPhase(
            ExecutionPhase.DryRun,
            "Only DryRun requests can complete immediately after dry-run.");
        ApplyTransition(JobStatus.Completed, actingUser, updatedUtc);
    }

    public void RequireApproval(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureExecuteRequested("Only Execute requests can require approval.");
        ApplyTransition(JobStatus.AwaitingApproval, actingUser, updatedUtc);
    }

    public void QueueExecution(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureExecuteRequested("Only Execute requests can queue execution.");
        ApplyTransition(JobStatus.ExecutionQueued, actingUser, updatedUtc);
    }

    public void Claim(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureExecuteRequested("Only Execute requests can be claimed for execution.");
        ApplyTransition(JobStatus.Claimed, actingUser, updatedUtc);
    }

    public void BeginPostValidation(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureExecuteRequested("Only Execute requests can enter post-validation.");
        ApplyTransition(JobStatus.PostValidation, actingUser, updatedUtc);
    }

    public void Fail(UserIdentity actingUser, DateTimeOffset updatedUtc) =>
        ApplyTransition(JobStatus.Failed, actingUser, updatedUtc);

    public void Cancel(UserIdentity actingUser, DateTimeOffset updatedUtc) =>
        ApplyTransition(JobStatus.Cancelled, actingUser, updatedUtc);

    public void MarkTimedOut(UserIdentity actingUser, DateTimeOffset updatedUtc) =>
        ApplyTransition(JobStatus.TimedOut, actingUser, updatedUtc);

    public void Block(UserIdentity actingUser, DateTimeOffset updatedUtc) =>
        ApplyTransition(JobStatus.Blocked, actingUser, updatedUtc);

    public void MarkNotRun(UserIdentity actingUser, DateTimeOffset updatedUtc) =>
        ApplyTransition(JobStatus.NotRun, actingUser, updatedUtc);

    public void CompleteReadOnlyAfterDryRun(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        var policy = RequirePolicySnapshot();
        if (Status != JobStatus.DryRunCompleted ||
            policy.RiskLevel != RiskLevel.ReadOnly ||
            policy.SupportsExecutePhase)
        {
            throw new InvalidJobStateTransitionException(Status, JobStatus.Completed);
        }

        ApplyTransition(JobStatus.Completed, actingUser, updatedUtc);
    }

    public void RecordApproval(
        UserIdentity approver,
        string approvalFingerprint,
        string? comment,
        DateTimeOffset decisionUtc)
    {
        var policy = RequirePolicySnapshot();
        ValidateTransition(JobStatus.Approved, approver, decisionUtc);
        if (policy.RiskLevel is RiskLevel.Medium or RiskLevel.High or RiskLevel.Critical &&
            RequestedBy == approver)
        {
            throw new DomainValidationException(
                $"{policy.RiskLevel} jobs cannot be approved by their requester.");
        }

        var approval = new JobApproval(
            JobApprovalId.New(),
            ApprovalDecision.Approved,
            approver,
            decisionUtc,
            comment,
            approvalFingerprint);
        _approvals.Add(approval);
        ApplyValidatedTransition(JobStatus.Approved, approver, decisionUtc);
    }

    public void RecordRejection(
        UserIdentity approver,
        string approvalFingerprint,
        string? comment,
        DateTimeOffset decisionUtc)
    {
        _ = RequirePolicySnapshot();
        ValidateTransition(JobStatus.Rejected, approver, decisionUtc);
        var rejection = new JobApproval(
            JobApprovalId.New(),
            ApprovalDecision.Rejected,
            approver,
            decisionUtc,
            comment,
            approvalFingerprint);
        _approvals.Add(rejection);
        ApplyValidatedTransition(JobStatus.Rejected, approver, decisionUtc);
    }

    public JobExecution StartExecutionAttempt(
        WorkerNodeId? workerNodeId,
        UserIdentity actingUser,
        DateTimeOffset startedUtc)
    {
        EnsureExecuteRequested("Only Execute requests can start execution.");
        ValidateTransition(JobStatus.Executing, actingUser, startedUtc);
        var execution = new JobExecution(
            JobExecutionId.New(),
            _executions.Count + 1,
            workerNodeId,
            startedUtc);
        execution.Start(startedUtc);
        _executions.Add(execution);
        ApplyValidatedTransition(JobStatus.Executing, actingUser, startedUtc);
        return execution;
    }

    public void RecordTerminalExecutionOutcome(
        ExecutionOutcome outcome,
        int? exitCode,
        string? summary,
        UserIdentity actingUser,
        DateTimeOffset completedUtc)
    {
        EnsureExecuteRequested("Only Execute requests can record execution outcomes.");
        if (Status is not (JobStatus.Executing or JobStatus.PostValidation))
        {
            throw new DomainValidationException("A terminal outcome requires an executing or post-validation job.");
        }

        var execution = _executions.LastOrDefault()
            ?? throw new DomainValidationException("No execution attempt exists.");
        var status = outcome switch
        {
            ExecutionOutcome.Succeeded => JobStatus.Completed,
            ExecutionOutcome.SucceededWithWarnings => JobStatus.CompletedWithWarnings,
            ExecutionOutcome.Failed => JobStatus.Failed,
            ExecutionOutcome.Cancelled => JobStatus.Cancelled,
            ExecutionOutcome.TimedOut => JobStatus.TimedOut,
            ExecutionOutcome.Blocked => JobStatus.Blocked,
            ExecutionOutcome.NotRun => JobStatus.NotRun,
            _ => throw new DomainValidationException($"Unsupported outcome {outcome}."),
        };

        ValidateTransition(status, actingUser, completedUtc);
        execution.Complete(outcome, exitCode, summary, completedUtc);
        ApplyValidatedTransition(status, actingUser, completedUtc);
    }

    private void EnsureDraft()
    {
        if (Status != JobStatus.Draft)
        {
            throw new DomainValidationException("Draft job details cannot change after submission.");
        }
    }

    private JobPolicySnapshot RequirePolicySnapshot() =>
        PolicySnapshot ?? throw new DomainValidationException(
            "A trusted script policy snapshot is required after submission.");

    private void ApplyTransition(
        JobStatus newStatus,
        UserIdentity actingUser,
        DateTimeOffset updatedUtc)
    {
        ValidateTransition(newStatus, actingUser, updatedUtc);
        ApplyValidatedTransition(newStatus, actingUser, updatedUtc);
    }

    private void ValidateTransition(
        JobStatus newStatus,
        UserIdentity actingUser,
        DateTimeOffset updatedUtc)
    {
        JobStatusPolicy.EnsureAllowed(Status, newStatus);
        ValidateMutation(actingUser, updatedUtc);
    }

    private void ValidateMutation(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        if (actingUser is null)
        {
            throw new DomainValidationException("Acting user is required.");
        }

        if (updatedUtc < UpdatedUtc)
        {
            throw new DomainValidationException("Job timestamps cannot move backward.");
        }
    }

    private void EnsureExecuteRequested(string message) =>
        EnsureRequestedPhase(ExecutionPhase.Execute, message);

    private void EnsureRequestedPhase(ExecutionPhase expected, string message)
    {
        if (RequestedPhase != expected)
        {
            throw new DomainValidationException(message);
        }
    }

    private void EnsureRequestedPhase(ExecutionPhase first, ExecutionPhase second, string message)
    {
        if (RequestedPhase != first && RequestedPhase != second)
        {
            throw new DomainValidationException(message);
        }
    }

    private void ApplyValidatedTransition(
        JobStatus newStatus,
        UserIdentity actingUser,
        DateTimeOffset updatedUtc)
    {
        Status = newStatus;
        ApplyTouch(actingUser, updatedUtc);
    }

    private void ApplyTouch(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        LastActingUser = actingUser;
        UpdatedUtc = updatedUtc;
    }

    private static string? ValidateDescription(string? description)
    {
        var normalized = description?.Trim();
        if (normalized?.Length > 2000)
        {
            throw new DomainValidationException("Job description cannot exceed 2,000 characters.");
        }

        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
