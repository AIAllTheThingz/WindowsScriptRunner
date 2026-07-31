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
    private JobLease? _lease;
    private JobDryRunEvidence? _acceptedDryRunEvidence;

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
        RequestedPhase = EnumGuard.RequireDefined(requestedPhase, nameof(RequestedPhase));
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
    public JobLease? Lease => _lease;
    public JobDryRunEvidence? AcceptedDryRunEvidence => _acceptedDryRunEvidence;

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

    internal static Job Rehydrate(
        JobId id,
        ScriptDefinitionId scriptDefinitionId,
        ScriptVersionId scriptVersionId,
        ExecutionPhase requestedPhase,
        JobStatus status,
        UserIdentity requestedBy,
        UserIdentity lastActingUser,
        DateTimeOffset createdUtc,
        DateTimeOffset updatedUtc,
        DateTimeOffset? submittedUtc,
        string? description,
        ChangeReference? changeReference,
        JobPolicySnapshot? policySnapshot,
        IEnumerable<JobTarget> targets,
        IEnumerable<JobParameter> parameters,
        IEnumerable<JobExecution> executions,
        IEnumerable<JobApproval> approvals,
        JobLease? lease = null,
        JobDryRunEvidence? acceptedDryRunEvidence = null)
    {
        status = EnumGuard.RequireDefined(status, nameof(JobStatus));
        if (updatedUtc < createdUtc)
        {
            throw new DomainValidationException("Job update timestamp cannot precede creation.");
        }

        if (submittedUtc is not null &&
            (submittedUtc < createdUtc || submittedUtc > updatedUtc))
        {
            throw new DomainValidationException(
                "Job submission timestamp must fall within the job lifetime.");
        }

        if (status == JobStatus.Draft)
        {
            if (submittedUtc is not null || policySnapshot is not null)
            {
                throw new DomainValidationException(
                    "Draft jobs cannot contain submission or policy snapshot state.");
            }
        }
        else if (submittedUtc is null || policySnapshot is null)
        {
            throw new DomainValidationException(
                "Submitted job state requires submission time and a policy snapshot.");
        }

        if (policySnapshot is not null &&
            (policySnapshot.ScriptDefinitionId != scriptDefinitionId ||
                policySnapshot.ScriptVersionId != scriptVersionId))
        {
            throw new DomainValidationException(
                "Persisted policy snapshot identifiers must match the pinned script identifiers.");
        }

        ValidateRehydratedPhaseState(requestedPhase, status, policySnapshot);

        var job = new Job(
            id,
            scriptDefinitionId,
            scriptVersionId,
            requestedPhase,
            requestedBy,
            createdUtc,
            description,
            changeReference)
        {
            Status = status,
            LastActingUser = lastActingUser ??
                throw new DomainValidationException("Last acting user is required."),
            UpdatedUtc = updatedUtc,
            SubmittedUtc = submittedUtc,
            PolicySnapshot = policySnapshot,
        };

        job.RestoreTargets(targets);
        job.RestoreParameters(parameters);
        job.RestoreExecutions(executions);
        job.RestoreApprovals(approvals);
        job._lease = lease;
        job._acceptedDryRunEvidence = acceptedDryRunEvidence;
        job.ValidateRehydratedExecutionState();
        job.ValidateRehydratedLeaseState();
        job.ValidateRehydratedDryRunEvidenceState();
        if (status != JobStatus.Draft && job._targets.Count == 0)
        {
            throw new DomainValidationException(
                "Submitted job state requires at least one target.");
        }

        return job;
    }

    private void ValidateRehydratedDryRunEvidenceState()
    {
        if (_acceptedDryRunEvidence is null)
        {
            return;
        }

        if (_acceptedDryRunEvidence.ExecutionWindowOpenedUtc < CreatedUtc ||
            _acceptedDryRunEvidence.CompletedUtc > UpdatedUtc ||
            Status is JobStatus.Draft or JobStatus.Submitted or JobStatus.Validated or
                JobStatus.DryRunQueued or JobStatus.DryRunRunning)
        {
            throw new DomainValidationException(
                "Persisted dry-run evidence is inconsistent with the job lifecycle.");
        }
    }

    private void ValidateRehydratedLeaseState()
    {
        if (_lease is null)
        {
            if (Status is JobStatus.Claimed or
                JobStatus.DryRunRunning or
                JobStatus.Executing or
                JobStatus.PostValidation)
            {
                throw new DomainValidationException(
                    "Persisted worker-controlled job state requires an active lease.");
            }

            return;
        }

        var validState = _lease.WorkKind switch
        {
            JobWorkKind.DryRun => Status is JobStatus.DryRunQueued or JobStatus.DryRunRunning,
            JobWorkKind.Execute => Status is JobStatus.Claimed or JobStatus.Executing or JobStatus.PostValidation,
            _ => false,
        };
        if (!validState)
        {
            throw new DomainValidationException(
                "Persisted job lease is inconsistent with the job status.");
        }

        if (HasActiveExecutionAttempt)
        {
            var execution = RequireSingleActiveExecutionAttempt();
            if (_lease.WorkKind != JobWorkKind.Execute ||
                execution.WorkerNodeId != _lease.WorkerNodeId)
            {
                throw new DomainValidationException(
                    "Persisted active execution must be owned by the current Execute lease worker.");
            }
        }
    }

    private void RestoreTargets(IEnumerable<JobTarget> targets)
    {
        foreach (var target in targets ?? throw new DomainValidationException("Job targets are required."))
        {
            ArgumentNullException.ThrowIfNull(target);
            if (target.AddedUtc < CreatedUtc || target.AddedUtc > UpdatedUtc)
            {
                throw new DomainValidationException(
                    "Persisted job target timestamp must fall within the job lifetime.");
            }

            if (_targets.Any(existing => existing.Name.Equals(target.Name)))
            {
                throw new DuplicateJobTargetException(target.Name.Value);
            }

            _targets.Add(target);
        }
    }

    private void RestoreParameters(IEnumerable<JobParameter> parameters)
    {
        foreach (var parameter in parameters ??
            throw new DomainValidationException("Job parameters are required."))
        {
            ArgumentNullException.ThrowIfNull(parameter);
            if (_parameters.Any(existing => string.Equals(
                existing.Name,
                parameter.Name,
                StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidJobParameterException(
                    parameter.Name,
                    "duplicate parameter bindings are not allowed.");
            }

            _parameters.Add(parameter);
        }
    }

    private void RestoreExecutions(IEnumerable<JobExecution> executions)
    {
        foreach (var execution in executions ??
            throw new DomainValidationException("Job executions are required."))
        {
            ArgumentNullException.ThrowIfNull(execution);
            if (_executions.Any(existing => existing.Id == execution.Id))
            {
                throw new DomainValidationException(
                    $"Execution identifier '{execution.Id}' is duplicated in persisted state.");
            }

            if (_executions.Any(existing => existing.AttemptNumber == execution.AttemptNumber))
            {
                throw new DomainValidationException(
                    $"Execution attempt {execution.AttemptNumber} is duplicated in persisted state.");
            }

            if (execution.CreatedUtc < CreatedUtc ||
                execution.CreatedUtc > UpdatedUtc ||
                execution.StartedUtc > UpdatedUtc ||
                execution.CompletedUtc > UpdatedUtc)
            {
                throw new DomainValidationException(
                    "Persisted execution timestamps must fall within the job lifetime.");
            }

            _executions.Add(execution);
        }

        var orderedAttempts = _executions
            .OrderBy(execution => execution.AttemptNumber)
            .Select(execution => execution.AttemptNumber)
            .ToArray();
        if (!orderedAttempts.SequenceEqual(Enumerable.Range(1, orderedAttempts.Length)))
        {
            throw new DomainValidationException(
                "Persisted execution attempt numbers must form a contiguous sequence.");
        }
    }

    private void RestoreApprovals(IEnumerable<JobApproval> approvals)
    {
        foreach (var approval in approvals ??
            throw new DomainValidationException("Job approvals are required."))
        {
            ArgumentNullException.ThrowIfNull(approval);
            if (_approvals.Any(existing => existing.Id == approval.Id))
            {
                throw new DomainValidationException(
                    $"Approval identifier '{approval.Id}' is duplicated in persisted state.");
            }

            if (approval.DecisionUtc < CreatedUtc || approval.DecisionUtc > UpdatedUtc)
            {
                throw new DomainValidationException(
                    "Persisted approval timestamp must fall within the job lifetime.");
            }

            _approvals.Add(approval);
        }
    }

    private void ValidateRehydratedExecutionState()
    {
        var activeCount = _executions.Count(execution => execution.IsActive);
        if (activeCount > 1)
        {
            throw new DomainValidationException(
                "Only one persisted active execution attempt is allowed.");
        }

        var requiresActiveAttempt = Status is JobStatus.Executing or JobStatus.PostValidation;
        if ((requiresActiveAttempt && activeCount != 1) ||
            (!requiresActiveAttempt && activeCount != 0))
        {
            throw new DomainValidationException(
                "Persisted execution state is inconsistent with the job status.");
        }
    }

    private static void ValidateRehydratedPhaseState(
        ExecutionPhase requestedPhase,
        JobStatus status,
        JobPolicySnapshot? policySnapshot)
    {
        if (status != JobStatus.Draft &&
            requestedPhase is not (ExecutionPhase.Validation or
                ExecutionPhase.DryRun or
                ExecutionPhase.Execute))
        {
            throw new DomainValidationException(
                "Submitted job state contains an unsupported requested phase.");
        }

        if ((status is JobStatus.DryRunQueued or
            JobStatus.DryRunRunning or
            JobStatus.DryRunCompleted) &&
            requestedPhase is not (ExecutionPhase.DryRun or ExecutionPhase.Execute))
        {
            throw new DomainValidationException(
                "Persisted dry-run state requires a DryRun or Execute request.");
        }

        if ((status is JobStatus.AwaitingApproval or
            JobStatus.Approved or
            JobStatus.ExecutionQueued or
            JobStatus.Claimed or
            JobStatus.Executing or
            JobStatus.PostValidation or
            JobStatus.CompletedWithWarnings or
            JobStatus.Rejected) &&
            requestedPhase != ExecutionPhase.Execute)
        {
            throw new DomainValidationException(
                "Persisted approval or execution state requires an Execute request.");
        }

        if (requestedPhase == ExecutionPhase.Execute &&
            status != JobStatus.Draft &&
            policySnapshot?.SupportsExecutePhase != true)
        {
            throw new DomainValidationException(
                "A submitted Execute request requires captured Execute support.");
        }

        if (status == JobStatus.PostValidation &&
            policySnapshot?.SupportsPostValidationPhase != true)
        {
            throw new DomainValidationException(
                "Persisted post-validation state requires captured PostValidation support.");
        }
    }

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

    public void SetParameterValue(
        string parameterName,
        string? serializedValue,
        UserIdentity actingUser,
        DateTimeOffset updatedUtc)
    {
        if (string.IsNullOrWhiteSpace(serializedValue))
        {
            _ = ClearParameterValue(parameterName, actingUser, updatedUtc);
            return;
        }

        EnsureDraft();
        var replacement = new JobParameter(parameterName, serializedValue);
        ValidateMutation(actingUser, updatedUtc);
        var existing = _parameters.SingleOrDefault(parameter =>
            string.Equals(parameter.Name, replacement.Name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            _parameters.Remove(existing);
        }

        _parameters.Add(replacement);
        ApplyTouch(actingUser, updatedUtc);
    }

    public bool ClearParameterValue(
        string parameterName,
        UserIdentity actingUser,
        DateTimeOffset updatedUtc)
    {
        EnsureDraft();
        var validatedName = JobParameter.ValidateName(parameterName);
        ValidateMutation(actingUser, updatedUtc);
        var existing = _parameters.SingleOrDefault(parameter =>
            string.Equals(parameter.Name, validatedName, StringComparison.OrdinalIgnoreCase));
        var bindingExisted = existing is not null;

        if (existing is not null)
        {
            _parameters.Remove(existing);
        }

        ApplyTouch(actingUser, updatedUtc);
        return bindingExisted;
    }

    public void RemoveParameter(string name, UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureDraft();
        var validatedName = JobParameter.ValidateName(name);
        ValidateMutation(actingUser, updatedUtc);
        var existing = _parameters.SingleOrDefault(parameter =>
            string.Equals(parameter.Name, validatedName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            throw new InvalidJobParameterException(validatedName, "the job does not contain this parameter.");
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

        if (RequestedPhase == ExecutionPhase.Execute &&
            !version.SupportedPhases.Contains(ExecutionPhase.DryRun))
        {
            throw new DomainValidationException("Execute requests require a script version that also supports DryRun.");
        }

        if (_targets.Count == 0)
        {
            throw new DomainValidationException("At least one target is required before job submission.");
        }

        ValidateParametersAgainst(version);

        var policySnapshot = JobPolicySnapshot.Capture(scriptDefinition, ScriptVersionId);
        PolicySnapshot = policySnapshot;
        SubmittedUtc = submittedUtc;
        ApplyValidatedTransition(JobStatus.Submitted, actingUser, submittedUtc);
    }

    private void ValidateParametersAgainst(ScriptVersion version)
    {
        var duplicate = _parameters
            .GroupBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidJobParameterException(
                duplicate.Key,
                "duplicate parameter bindings are not allowed.");
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

    internal void StartDryRun(UserIdentity actingUser, DateTimeOffset updatedUtc) =>
        ApplyTransition(JobStatus.DryRunRunning, actingUser, updatedUtc);

    internal void CompleteDryRun(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        ValidateTransition(JobStatus.DryRunCompleted, actingUser, updatedUtc);
        _acceptedDryRunEvidence = new JobDryRunEvidence(
            JobWorkKind.DryRun,
            JobDryRunEvidenceSource.InternalLifecycle,
            null,
            null,
            null,
            UpdatedUtc,
            updatedUtc);
        ApplyValidatedTransition(JobStatus.DryRunCompleted, actingUser, updatedUtc);
    }

    public void StartDryRun(
        JobLeaseCredentials credentials,
        UserIdentity actingUser,
        DateTimeOffset updatedUtc)
    {
        ValidateWorkLease(credentials, JobWorkKind.DryRun, updatedUtc);
        ApplyTransition(JobStatus.DryRunRunning, actingUser, updatedUtc);
    }

    public void CompleteDryRun(
        JobLeaseCredentials credentials,
        UserIdentity actingUser,
        DateTimeOffset updatedUtc)
    {
        ValidateWorkLease(credentials, JobWorkKind.DryRun, updatedUtc);
        ValidateTransition(JobStatus.DryRunCompleted, actingUser, updatedUtc);
        var lease = RequireLease();
        _acceptedDryRunEvidence = new JobDryRunEvidence(
            lease.WorkKind,
            JobDryRunEvidenceSource.LeasedWorker,
            lease.WorkerNodeId,
            lease.Id,
            lease.FencingToken,
            lease.AcquiredUtc,
            updatedUtc);
        ApplyValidatedTransition(JobStatus.DryRunCompleted, actingUser, updatedUtc);
        _lease = null;
    }

    public void TerminateDryRun(
        JobLeaseCredentials credentials,
        ExecutionOutcome outcome,
        UserIdentity actingUser,
        DateTimeOffset updatedUtc)
    {
        ValidateWorkLease(credentials, JobWorkKind.DryRun, updatedUtc);
        var terminalStatus = outcome switch
        {
            ExecutionOutcome.Failed => JobStatus.Failed,
            ExecutionOutcome.Cancelled => JobStatus.Cancelled,
            ExecutionOutcome.TimedOut => JobStatus.TimedOut,
            ExecutionOutcome.Blocked => JobStatus.Blocked,
            ExecutionOutcome.NotRun => JobStatus.NotRun,
            _ => throw new DomainValidationException(
                "Leased dry-run work requires a controlled terminal outcome."),
        };

        ApplyTransition(terminalStatus, actingUser, updatedUtc);
        _lease = null;
    }

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
        RequireAcceptedDryRunEvidence();
        ApplyTransition(JobStatus.AwaitingApproval, actingUser, updatedUtc);
    }

    public void QueueExecution(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureExecuteRequested("Only Execute requests can queue execution.");
        ApplyTransition(JobStatus.ExecutionQueued, actingUser, updatedUtc);
    }

    internal void Claim(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureExecuteRequested("Only Execute requests can be claimed for execution.");
        ApplyTransition(JobStatus.Claimed, actingUser, updatedUtc);
    }

    internal void BeginPostValidation(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureExecuteRequested("Only Execute requests can enter post-validation.");
        if (PolicySnapshot?.SupportsPostValidationPhase != true)
        {
            throw new DomainValidationException(
                "The pinned script version does not support post-validation.");
        }

        ApplyTransition(JobStatus.PostValidation, actingUser, updatedUtc);
    }

    public void BeginPostValidation(
        JobLeaseCredentials credentials,
        UserIdentity actingUser,
        DateTimeOffset updatedUtc)
    {
        ValidateWorkLease(credentials, JobWorkKind.Execute, updatedUtc);
        EnsureExecuteRequested("Only Execute requests can enter post-validation.");
        if (PolicySnapshot?.SupportsPostValidationPhase != true)
        {
            throw new DomainValidationException(
                "The pinned script version does not support post-validation.");
        }

        ApplyTransition(JobStatus.PostValidation, actingUser, updatedUtc);
    }

    public void Fail(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureNoActiveExecutionOutcomeRequired();
        ApplyTransition(JobStatus.Failed, actingUser, updatedUtc);
    }

    public void Cancel(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureNoActiveExecutionOutcomeRequired();
        ApplyTransition(JobStatus.Cancelled, actingUser, updatedUtc);
    }

    public void MarkTimedOut(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureNoActiveExecutionOutcomeRequired();
        ApplyTransition(JobStatus.TimedOut, actingUser, updatedUtc);
    }

    public void Block(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureNoActiveExecutionOutcomeRequired();
        ApplyTransition(JobStatus.Blocked, actingUser, updatedUtc);
    }

    public void MarkNotRun(UserIdentity actingUser, DateTimeOffset updatedUtc)
    {
        EnsureNoActiveExecutionOutcomeRequired();
        ApplyTransition(JobStatus.NotRun, actingUser, updatedUtc);
    }

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
        RequireAcceptedDryRunEvidence();
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
        RequireAcceptedDryRunEvidence();
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

    public JobLease AcquireWorkLease(
        JobLeaseId leaseId,
        WorkerNodeId workerNodeId,
        JobWorkKind workKind,
        long fencingToken,
        UserIdentity actingUser,
        DateTimeOffset acquiredUtc,
        DateTimeOffset expiresUtc)
    {
        if (_lease is not null)
        {
            throw new DomainValidationException("A job cannot acquire a second active lease.");
        }

        var lease = new JobLease(
            leaseId,
            workerNodeId,
            workKind,
            fencingToken,
            acquiredUtc,
            acquiredUtc,
            expiresUtc);
        ValidateMutation(actingUser, acquiredUtc);
        switch (workKind)
        {
            case JobWorkKind.DryRun when Status == JobStatus.DryRunQueued:
                break;
            case JobWorkKind.Execute when Status == JobStatus.ExecutionQueued:
                EnsureExecuteRequested("Only Execute requests can be leased for execution.");
                JobStatusPolicy.EnsureAllowed(Status, JobStatus.Claimed);
                break;
            default:
                throw new DomainValidationException(
                    $"Job status {Status} is not eligible for {workKind} work.");
        }

        _lease = lease;
        if (workKind == JobWorkKind.Execute)
        {
            Status = JobStatus.Claimed;
        }

        ApplyTouch(actingUser, acquiredUtc);
        return lease;
    }

    public void RenewWorkLease(
        JobLeaseCredentials credentials,
        DateTimeOffset renewedUtc,
        DateTimeOffset expiresUtc)
    {
        var lease = RequireLease();
        lease.ValidateCredentials(credentials);
        lease.Renew(renewedUtc, expiresUtc);
    }

    public void ValidateWorkLease(
        JobLeaseCredentials credentials,
        JobWorkKind workKind,
        DateTimeOffset now)
    {
        workKind = EnumGuard.RequireDefined(workKind, nameof(workKind));
        var lease = RequireLease();
        lease.ValidateCredentials(credentials);
        if (lease.WorkKind != workKind)
        {
            throw new DomainValidationException("Job lease work kind does not match the requested operation.");
        }

        if (now >= lease.ExpiresUtc)
        {
            throw new DomainValidationException("The job lease has expired.");
        }
    }

    public void ReleaseUnstartedWorkLease(
        JobLeaseCredentials credentials,
        UserIdentity actingUser,
        DateTimeOffset releasedUtc)
    {
        var lease = RequireLease();
        lease.ValidateCredentials(credentials);
        ValidateMutation(actingUser, releasedUtc);
        if (releasedUtc >= lease.ExpiresUtc)
        {
            throw new DomainValidationException(
                "Expired job leases must be handled through recovery.");
        }

        switch (lease.WorkKind)
        {
            case JobWorkKind.DryRun when Status == JobStatus.DryRunQueued:
                break;
            case JobWorkKind.Execute when
                Status == JobStatus.Claimed &&
                !HasActiveExecutionAttempt:
                break;
            default:
                throw new DomainValidationException(
                    "Only unstarted queued or claimed work can release its lease.");
        }

        if (lease.WorkKind == JobWorkKind.Execute)
        {
            Status = JobStatus.ExecutionQueued;
        }

        _lease = null;
        ApplyTouch(actingUser, releasedUtc);
    }

    public JobLeaseRecoveryDisposition RecoverExpiredWorkLease(
        JobLeaseCredentials credentials,
        UserIdentity actingUser,
        DateTimeOffset recoveredUtc)
    {
        var lease = RequireLease();
        lease.ValidateCredentials(credentials);
        ValidateMutation(actingUser, recoveredUtc);
        if (recoveredUtc < lease.ExpiresUtc)
        {
            throw new DomainValidationException("A job lease cannot be recovered before expiration.");
        }

        JobLeaseRecoveryDisposition disposition;
        switch (lease.WorkKind, Status)
        {
            case (JobWorkKind.DryRun, JobStatus.DryRunQueued):
                disposition = JobLeaseRecoveryDisposition.ReleasedQueuedDryRun;
                ApplyTouch(actingUser, recoveredUtc);
                break;
            case (JobWorkKind.Execute, JobStatus.Claimed) when !HasActiveExecutionAttempt:
                Status = JobStatus.ExecutionQueued;
                disposition = JobLeaseRecoveryDisposition.RequeuedUnstartedExecution;
                ApplyTouch(actingUser, recoveredUtc);
                break;
            case (JobWorkKind.DryRun, JobStatus.DryRunRunning):
                ValidateTransition(JobStatus.TimedOut, actingUser, recoveredUtc);
                ApplyValidatedTransition(JobStatus.TimedOut, actingUser, recoveredUtc);
                disposition = JobLeaseRecoveryDisposition.TimedOutDryRun;
                break;
            case (JobWorkKind.Execute, JobStatus.Executing):
            case (JobWorkKind.Execute, JobStatus.PostValidation):
                _ = RecordTerminalExecutionOutcome(
                    ExecutionOutcome.TimedOut,
                    null,
                    "The active execution lease expired.",
                    actingUser,
                    recoveredUtc);
                disposition = JobLeaseRecoveryDisposition.TimedOutExecution;
                break;
            default:
                throw new DomainValidationException(
                    "The current job state does not support expired-lease recovery.");
        }

        _lease = null;
        return disposition;
    }

    internal JobExecution StartExecutionAttempt(
        WorkerNodeId? workerNodeId,
        UserIdentity actingUser,
        DateTimeOffset startedUtc)
    {
        EnsureExecuteRequested("Only Execute requests can start execution.");
        if (HasActiveExecutionAttempt)
        {
            throw new DomainValidationException("A new execution attempt cannot start while another attempt is active.");
        }

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

    public JobExecution StartLeasedExecutionAttempt(
        JobLeaseCredentials credentials,
        UserIdentity actingUser,
        DateTimeOffset startedUtc)
    {
        ValidateWorkLease(credentials, JobWorkKind.Execute, startedUtc);
        return StartExecutionAttempt(credentials.WorkerNodeId, actingUser, startedUtc);
    }

    internal JobExecution RecordTerminalExecutionOutcome(
        ExecutionOutcome outcome,
        int? exitCode,
        string? summary,
        UserIdentity actingUser,
        DateTimeOffset completedUtc)
    {
        EnsureExecuteRequested("Only Execute requests can record execution outcomes.");
        outcome = EnumGuard.RequireDefined(outcome, nameof(ExecutionOutcome));
        if (Status is not (JobStatus.Executing or JobStatus.PostValidation))
        {
            throw new DomainValidationException("A terminal outcome requires an executing or post-validation job.");
        }

        var execution = RequireSingleActiveExecutionAttempt();
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
        return execution;
    }

    public JobExecution RecordTerminalExecutionOutcome(
        JobLeaseCredentials credentials,
        ExecutionOutcome outcome,
        int? exitCode,
        string? summary,
        UserIdentity actingUser,
        DateTimeOffset completedUtc)
    {
        ValidateWorkLease(credentials, JobWorkKind.Execute, completedUtc);
        var execution = RecordTerminalExecutionOutcome(
            outcome,
            exitCode,
            summary,
            actingUser,
            completedUtc);
        _lease = null;
        return execution;
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

    private JobLease RequireLease() =>
        _lease ?? throw new DomainValidationException("The job does not have an active lease.");

    public bool HasActiveExecutionAttempt => _executions.Any(execution => execution.IsActive);

    private JobExecution RequireSingleActiveExecutionAttempt()
    {
        var activeExecutions = _executions.Where(execution => execution.IsActive).ToArray();
        return activeExecutions.Length switch
        {
            1 => activeExecutions[0],
            0 => throw new DomainValidationException("No active execution attempt exists."),
            _ => throw new DomainValidationException("Only one active execution attempt is allowed."),
        };
    }

    private void EnsureNoActiveExecutionOutcomeRequired()
    {
        if (_lease is not null ||
            Status is JobStatus.Executing or JobStatus.PostValidation ||
            HasActiveExecutionAttempt)
        {
            throw new DomainValidationException(
                "Leased or active work must be completed through a lease-aware operation.");
        }
    }

    private void RequireAcceptedDryRunEvidence()
    {
        if (_acceptedDryRunEvidence is null ||
            _acceptedDryRunEvidence.WorkKind != JobWorkKind.DryRun)
        {
            throw new DomainValidationException(
                "Approval decisions require accepted DryRun evidence.");
        }
    }

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
