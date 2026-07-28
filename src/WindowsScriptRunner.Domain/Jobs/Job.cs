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
        Id = id;
        ScriptDefinitionId = scriptDefinitionId;
        ScriptVersionId = scriptVersionId;
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
        if (_targets.Any(target => target.Name.Equals(targetName)))
        {
            throw new DuplicateJobTargetException(targetName.Value);
        }

        _targets.Add(new JobTarget(targetName, addedUtc, actingUser));
        Touch(actingUser, addedUtc);
    }

    public void RemoveTarget(TargetName targetName, UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureDraft();
        var target = _targets.SingleOrDefault(existing => existing.Name.Equals(targetName));
        if (target is null)
        {
            throw new DomainValidationException($"Target '{targetName}' is not part of the job.");
        }

        _targets.Remove(target);
        Touch(actingUser, updatedUtc);
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

        var existing = _parameters.SingleOrDefault(parameter =>
            string.Equals(parameter.Name, definition.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            _parameters.Remove(existing);
        }

        _parameters.Add(
            new JobParameter(
                definition.Name,
                serializedValue,
                definition.ParameterType,
                definition.IsSensitive));
        Touch(actingUser, updatedUtc);
    }

    public void RemoveParameter(string name, UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureDraft();
        var existing = _parameters.SingleOrDefault(parameter =>
            string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            throw new InvalidJobParameterException(name, "the job does not contain this parameter.");
        }

        _parameters.Remove(existing);
        Touch(actingUser, updatedUtc);
    }

    public void UpdateDescription(string? description, UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureDraft();
        Description = ValidateDescription(description);
        Touch(actingUser, updatedUtc);
    }

    public void SetChangeReference(
        ChangeReference? changeReference,
        UserIdentity actingUser,
        DateTimeOffset updatedUtc)
    {
        EnsureDraft();
        ChangeReference = changeReference;
        Touch(actingUser, updatedUtc);
    }

    public void Submit(ScriptVersion version, UserIdentity actingUser, DateTimeOffset submittedUtc)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(version);
        if (version.Id != ScriptVersionId)
        {
            throw new DomainValidationException("The supplied script version does not match the job.");
        }

        if (!version.IsPublished)
        {
            throw new DomainValidationException("Only a published script version can be submitted.");
        }

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

        TransitionTo(JobStatus.Submitted, actingUser, submittedUtc);
        SubmittedUtc = submittedUtc;
    }

    public void TransitionTo(JobStatus newStatus, UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        JobStatusPolicy.EnsureAllowed(Status, newStatus);
        Status = newStatus;
        Touch(actingUser, updatedUtc);
    }

    public void CompleteReadOnlyAfterDryRun(
        RiskLevel riskLevel,
        bool supportsExecutePhase,
        UserIdentity actingUser,
        DateTimeOffset updatedUtc)
    {
        if (Status != JobStatus.DryRunCompleted ||
            riskLevel != RiskLevel.ReadOnly ||
            supportsExecutePhase)
        {
            throw new InvalidJobStateTransitionException(Status, JobStatus.Completed);
        }

        Status = JobStatus.Completed;
        Touch(actingUser, updatedUtc);
    }

    public void RecordApproval(
        RiskLevel riskLevel,
        UserIdentity approver,
        string approvalFingerprint,
        string? comment,
        DateTimeOffset decisionUtc)
    {
        if (Status != JobStatus.AwaitingApproval)
        {
            throw new InvalidJobStateTransitionException(Status, JobStatus.Approved);
        }

        if (riskLevel is RiskLevel.Medium or RiskLevel.High or RiskLevel.Critical &&
            RequestedBy == approver)
        {
            throw new DomainValidationException(
                $"{riskLevel} jobs cannot be approved by their requester.");
        }

        _approvals.Add(
            new JobApproval(
                JobApprovalId.New(),
                ApprovalDecision.Approved,
                approver,
                decisionUtc,
                comment,
                approvalFingerprint));
        TransitionTo(JobStatus.Approved, approver, decisionUtc);
    }

    public void RecordRejection(
        UserIdentity approver,
        string approvalFingerprint,
        string? comment,
        DateTimeOffset decisionUtc)
    {
        if (Status != JobStatus.AwaitingApproval)
        {
            throw new InvalidJobStateTransitionException(Status, JobStatus.Rejected);
        }

        _approvals.Add(
            new JobApproval(
                JobApprovalId.New(),
                ApprovalDecision.Rejected,
                approver,
                decisionUtc,
                comment,
                approvalFingerprint));
        TransitionTo(JobStatus.Rejected, approver, decisionUtc);
    }

    public JobExecution StartExecutionAttempt(
        WorkerNodeId? workerNodeId,
        UserIdentity actingUser,
        DateTimeOffset startedUtc)
    {
        if (Status != JobStatus.Claimed)
        {
            throw new InvalidJobStateTransitionException(Status, JobStatus.Executing);
        }

        var execution = new JobExecution(
            JobExecutionId.New(),
            _executions.Count + 1,
            workerNodeId,
            startedUtc);
        execution.Start(startedUtc);
        _executions.Add(execution);
        TransitionTo(JobStatus.Executing, actingUser, startedUtc);
        return execution;
    }

    public void RecordTerminalExecutionOutcome(
        ExecutionOutcome outcome,
        int? exitCode,
        string? summary,
        UserIdentity actingUser,
        DateTimeOffset completedUtc)
    {
        if (Status is not (JobStatus.Executing or JobStatus.PostValidation))
        {
            throw new DomainValidationException("A terminal outcome requires an executing or post-validation job.");
        }

        var execution = _executions.LastOrDefault()
            ?? throw new DomainValidationException("No execution attempt exists.");
        execution.Complete(outcome, exitCode, summary, completedUtc);
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
        TransitionTo(status, actingUser, completedUtc);
    }

    private void EnsureDraft()
    {
        if (Status != JobStatus.Draft)
        {
            throw new DomainValidationException("Draft job details cannot change after submission.");
        }
    }

    private void Touch(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        ArgumentNullException.ThrowIfNull(actingUser);
        if (updatedUtc < UpdatedUtc)
        {
            throw new DomainValidationException("Job timestamps cannot move backward.");
        }

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
