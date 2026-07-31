using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Contracts.Jobs;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Credentials;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Domain.Workers;

namespace WindowsScriptRunner.Application.Jobs;

public sealed class CreateDraftJobHandler(
    IScriptDefinitionRepository scriptRepository,
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task<JobId> HandleAsync(
        CreateDraftJobCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var script = await scriptRepository.GetByIdAsync(
            command.ScriptDefinitionId,
            cancellationToken)
            ?? throw new EntityNotFoundException(
                nameof(ScriptDefinition),
                command.ScriptDefinitionId.ToString());
        var version = script.GetVersion(command.ScriptVersionId);
        ValidateRequestedPhase(version, command.RequestedPhase);

        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        var job = Job.CreateDraft(
            JobId.New(),
            command.ScriptDefinitionId,
            command.ScriptVersionId,
            command.RequestedPhase,
            command.RequestedBy,
            now,
            command.Description,
            command.ChangeReference);

        await jobRepository.AddAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            CreateAudit(
                "JobDraftCreated",
                job,
                command.RequestedBy,
                now,
                "A draft job was created."),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return job.Id;
    }

    private static void ValidateRequestedPhase(
        ScriptVersion version,
        Domain.ExecutionPhase requestedPhase)
    {
        if (!Enum.IsDefined(requestedPhase) ||
            requestedPhase is not (
                Domain.ExecutionPhase.Validation or
                Domain.ExecutionPhase.DryRun or
                Domain.ExecutionPhase.Execute))
        {
            throw new ApplicationValidationException(
                $"Requested phase {requestedPhase} is not supported by the Phase 2 lifecycle.");
        }

        if (!version.SupportedPhases.Contains(requestedPhase))
        {
            throw new ApplicationValidationException(
                $"Script version does not support the {requestedPhase} phase.");
        }

        if (requestedPhase == Domain.ExecutionPhase.Execute &&
            !version.SupportedPhases.Contains(Domain.ExecutionPhase.DryRun))
        {
            throw new ApplicationValidationException(
                "Execute requests require a script version that also supports DryRun.");
        }
    }

    private static AuditEvent CreateAudit(
        string eventType,
        Job job,
        UserIdentity actor,
        DateTimeOffset occurredUtc,
        string summary,
        IReadOnlyDictionary<string, string>? properties = null) =>
        new(
            AuditEventId.New(),
            eventType,
            nameof(Job),
            job.Id.ToString(),
            actor,
            occurredUtc,
            summary,
            properties);

    internal static AuditEvent Audit(
        string eventType,
        Job job,
        UserIdentity actor,
        DateTimeOffset occurredUtc,
        string summary,
        IReadOnlyDictionary<string, string>? properties = null) =>
        CreateAudit(eventType, job, actor, occurredUtc, summary, properties);
}

public sealed class AddJobTargetHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task HandleAsync(AddJobTargetCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await GetJobAsync(jobRepository, command.JobId, cancellationToken);
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        job.AddTarget(command.TargetName, command.ActingUser, now);
        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            CreateDraftJobHandler.Audit(
                "JobTargetAdded",
                job,
                command.ActingUser,
                now,
                "A target was added to the draft job.",
                new Dictionary<string, string> { ["Target"] = command.TargetName.Value }),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    internal static async Task<Job> GetJobAsync(
        IJobRepository repository,
        JobId id,
        CancellationToken cancellationToken) =>
        await repository.GetByIdAsync(id, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(Job), id.ToString());
}

public sealed class SetJobParameterHandler(
    IJobRepository jobRepository,
    IScriptDefinitionRepository scriptRepository,
    ICredentialReferenceRepository credentialRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task HandleAsync(
        SetJobParameterCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var script = await GetScriptAsync(
            scriptRepository,
            job.ScriptDefinitionId,
            cancellationToken);
        var definition = script.GetVersion(job.ScriptVersionId)
            .GetParameterDefinition(command.ParameterName);
        var suppliedValue = command.SerializedValue;
        var isAbsent = string.IsNullOrWhiteSpace(suppliedValue);
        definition.ValidateSerializedValue(suppliedValue);
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);

        AuditEvent audit;
        if (isAbsent)
        {
            var bindingExisted = job.ClearParameterValue(
                definition.Name,
                command.ActingUser,
                now);
            audit = CreateDraftJobHandler.Audit(
                "JobParameterCleared",
                job,
                command.ActingUser,
                now,
                "An explicit draft job parameter binding was cleared.",
                CreateParameterClearedAuditProperties(definition, bindingExisted));
        }
        else
        {
            var serializedValue = suppliedValue;
            if (definition.ParameterType == Domain.ScriptParameterType.SecureReference)
            {
                var credentialReference = await ResolveCredentialReferenceAsync(
                    credentialRepository,
                    suppliedValue,
                    cancellationToken);
                serializedValue = credentialReference.Id.ToString();
            }

            definition.ValidateSerializedValue(serializedValue);
            job.SetParameterValue(definition.Name, serializedValue, command.ActingUser, now);
            audit = CreateDraftJobHandler.Audit(
                "JobParameterSet",
                job,
                command.ActingUser,
                now,
                "A draft job parameter was set.",
                CreateParameterAuditProperties(definition, serializedValue));
        }

        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(audit, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    internal static async Task<ScriptDefinition> GetScriptAsync(
        IScriptDefinitionRepository repository,
        ScriptDefinitionId id,
        CancellationToken cancellationToken) =>
        await repository.GetByIdAsync(id, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(ScriptDefinition), id.ToString());

    private static async Task<CredentialReference> ResolveCredentialReferenceAsync(
        ICredentialReferenceRepository credentialRepository,
        string? serializedValue,
        CancellationToken cancellationToken)
    {
        if (!CredentialReferenceId.TryParse(serializedValue, out var credentialReferenceId))
        {
            throw new ApplicationValidationException("SecureReference value must be a valid credential reference identifier.");
        }

        var credentialReference = await credentialRepository.GetByIdAsync(
            credentialReferenceId!,
            cancellationToken);
        if (credentialReference is null)
        {
            throw new EntityNotFoundException("Credential reference", "[REDACTED]");
        }

        if (!credentialReference.IsEnabled)
        {
            throw new ApplicationValidationException("Credential reference is disabled.");
        }

        return credentialReference;
    }

    private static IReadOnlyDictionary<string, string> CreateParameterAuditProperties(
        ScriptParameterDefinition definition,
        string? serializedValue) =>
        new Dictionary<string, string>
        {
            ["Parameter"] = definition.Name,
            ["ParameterType"] = definition.ParameterType.ToString(),
            ["IsSensitive"] = definition.IsSensitive.ToString(),
            ["ValueProvided"] = (!string.IsNullOrWhiteSpace(serializedValue)).ToString(),
            ["SerializedLength"] = (definition.IsSensitive ||
                definition.ParameterType == Domain.ScriptParameterType.SecureReference
                    ? 0
                    : serializedValue?.Length ?? 0).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["ReferenceSupplied"] = (definition.ParameterType == Domain.ScriptParameterType.SecureReference &&
                !string.IsNullOrWhiteSpace(serializedValue)).ToString(),
            ["Value"] = definition.ParameterType == Domain.ScriptParameterType.SecureReference
                ? "[REDACTED]"
                : "[OMITTED]",
        };

    private static IReadOnlyDictionary<string, string> CreateParameterClearedAuditProperties(
        ScriptParameterDefinition definition,
        bool bindingExisted) =>
        new Dictionary<string, string>
        {
            ["Parameter"] = definition.Name,
            ["ParameterType"] = definition.ParameterType.ToString(),
            ["IsSensitive"] = definition.IsSensitive.ToString(),
            ["BindingExisted"] = bindingExisted.ToString(),
            ["ValueProvided"] = false.ToString(),
            ["ReferenceSupplied"] = false.ToString(),
        };
}

public sealed class SubmitJobHandler(
    IJobRepository jobRepository,
    IScriptDefinitionRepository scriptRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task HandleAsync(SubmitJobCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var script = await SetJobParameterHandler.GetScriptAsync(
            scriptRepository,
            job.ScriptDefinitionId,
            cancellationToken);
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        job.Submit(script, command.ActingUser, now);

        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            CreateDraftJobHandler.Audit(
                "JobSubmitted",
                job,
                command.ActingUser,
                now,
                "The job was submitted for validation."),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}

public sealed class TransitionJobHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task HandleAsync(
        TransitionJobCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var previous = job.Status;
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        ApplyOperationalTransition(job, command, now);

        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            CreateDraftJobHandler.Audit(
                "JobStatusChanged",
                job,
                command.ActingUser,
                now,
                $"The job status changed from {previous} to {command.NewStatus}.",
                new Dictionary<string, string>
                {
                    ["PreviousStatus"] = previous.ToString(),
                    ["NewStatus"] = command.NewStatus.ToString(),
                }),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    private static void ApplyOperationalTransition(
        Job job,
        TransitionJobCommand command,
        DateTimeOffset now)
    {
        if (RequiresExecutionOutcome(job, command.NewStatus))
        {
            throw new ApplicationValidationException(
                "Active execution attempts must be completed through the execution outcome operation.");
        }

        switch (command.NewStatus)
        {
            case Domain.JobStatus.Validated:
                job.MarkValidated(command.ActingUser, now);
                break;
            case Domain.JobStatus.DryRunQueued:
                job.QueueDryRun(command.ActingUser, now);
                break;
            case Domain.JobStatus.AwaitingApproval:
                job.RequireApproval(command.ActingUser, now);
                break;
            case Domain.JobStatus.ExecutionQueued:
                job.QueueExecution(command.ActingUser, now);
                break;
            case Domain.JobStatus.Failed:
                job.Fail(command.ActingUser, now);
                break;
            case Domain.JobStatus.Cancelled:
                job.Cancel(command.ActingUser, now);
                break;
            case Domain.JobStatus.TimedOut:
                job.MarkTimedOut(command.ActingUser, now);
                break;
            case Domain.JobStatus.Blocked:
                job.Block(command.ActingUser, now);
                break;
            case Domain.JobStatus.NotRun:
                job.MarkNotRun(command.ActingUser, now);
                break;
            default:
                throw new ApplicationValidationException(
                    $"Status {command.NewStatus} requires a dedicated application operation.");
        }
    }

    private static bool RequiresExecutionOutcome(Job job, Domain.JobStatus newStatus)
    {
        if (newStatus is not (Domain.JobStatus.Failed or
            Domain.JobStatus.Cancelled or
            Domain.JobStatus.TimedOut or
            Domain.JobStatus.Blocked or
            Domain.JobStatus.NotRun))
        {
            return false;
        }

        return job.Lease is not null ||
            job.Status is Domain.JobStatus.Executing or Domain.JobStatus.PostValidation ||
            job.HasActiveExecutionAttempt;
    }
}

public sealed class ApproveJobHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock,
    IJobFingerprintService fingerprintService,
    ICurrentUser currentUser)
{
    public async Task HandleAsync(ApproveJobCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var fingerprint = await fingerprintService.CreateFingerprintAsync(job, cancellationToken);
        if (!fingerprintService.IsExpectedFingerprintCurrent(
                command.ExpectedFingerprint,
                fingerprint))
        {
            throw new ApplicationConflictException(
                "The approval review is stale or invalid.");
        }

        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        try
        {
            job.RecordApproval(
                currentUser.User,
                fingerprint,
                command.Comment,
                now);
        }
        catch (Domain.Exceptions.DomainException exception)
        {
            throw new ApplicationConflictException(
                "The approval decision is no longer valid for the current job state.",
                exception);
        }
        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            CreateDraftJobHandler.Audit(
                "JobApproved",
                job,
                currentUser.User,
                now,
                "Approval evidence was recorded and the job was approved."),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}

public sealed class RejectJobHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock,
    IJobFingerprintService fingerprintService,
    ICurrentUser currentUser)
{
    public async Task HandleAsync(RejectJobCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var fingerprint = await fingerprintService.CreateFingerprintAsync(job, cancellationToken);
        if (!fingerprintService.IsExpectedFingerprintCurrent(
                command.ExpectedFingerprint,
                fingerprint))
        {
            throw new ApplicationConflictException(
                "The approval review is stale or invalid.");
        }

        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        try
        {
            job.RecordRejection(
                currentUser.User,
                fingerprint,
                command.Comment,
                now);
        }
        catch (Domain.Exceptions.DomainException exception)
        {
            throw new ApplicationConflictException(
                "The rejection decision is no longer valid for the current job state.",
                exception);
        }
        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            CreateDraftJobHandler.Audit(
                "JobRejected",
                job,
                currentUser.User,
                now,
                "Rejection evidence was recorded and the job was rejected."),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}

public sealed class CompleteReadOnlyJobHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task HandleAsync(
        CompleteReadOnlyJobCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        job.CompleteReadOnlyAfterDryRun(command.ActingUser, now);
        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            CreateDraftJobHandler.Audit(
                "ReadOnlyJobCompleted",
                job,
                command.ActingUser,
                now,
                "The trusted read-only, non-Execute job completed after dry-run."),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}

public sealed class CompleteValidationJobHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task HandleAsync(
        CompleteValidationJobCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        job.CompleteRequestedValidation(command.ActingUser, now);
        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            CreateDraftJobHandler.Audit(
                "ValidationJobCompleted",
                job,
                command.ActingUser,
                now,
                "The validation-only job completed after validation."),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}

public sealed class CompleteDryRunJobHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task HandleAsync(
        CompleteDryRunJobCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        job.CompleteRequestedDryRun(command.ActingUser, now);
        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            CreateDraftJobHandler.Audit(
                "DryRunJobCompleted",
                job,
                command.ActingUser,
                now,
                "The dry-run-only job completed after dry-run."),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}

public sealed class StartExecutionAttemptHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task HandleAsync(
        StartExecutionAttemptCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        var execution = job.StartLeasedExecutionAttempt(
            command.LeaseCredentials,
            command.ActingUser,
            now);
        var audit = CreateDraftJobHandler.Audit(
            "ExecutionAttemptStarted",
            job,
            command.ActingUser,
            now,
            "A job execution attempt was started.",
            new Dictionary<string, string>
            {
                ["AttemptNumber"] = execution.AttemptNumber.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ["WorkerNodeIdPresent"] = (execution.WorkerNodeId is not null).ToString(),
            });

        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(audit, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}

public sealed class RecordExecutionOutcomeHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task HandleAsync(
        RecordExecutionOutcomeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        var execution = job.RecordTerminalExecutionOutcome(
            command.LeaseCredentials,
            command.Outcome,
            command.ExitCode,
            command.Summary,
            command.ActingUser,
            now);
        await QueueHandlerSupport.CommitTerminalJobAuditAsync(
            jobRepository,
            auditWriter,
            unitOfWork,
            job,
            command.LeaseCredentials,
            "ExecutionOutcomeRecorded",
            command.ActingUser,
            now,
            "The active execution attempt was completed.",
            CreateExecutionOutcomeAuditProperties(command, execution),
            cancellationToken);
    }

    private static IReadOnlyDictionary<string, string> CreateExecutionOutcomeAuditProperties(
        RecordExecutionOutcomeCommand command,
        JobExecution execution) =>
        new Dictionary<string, string>
        {
            ["Outcome"] = command.Outcome.ToString(),
            ["ExitCodePresent"] = command.ExitCode.HasValue.ToString(),
            ["ExitCode"] = command.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(null)",
            ["SummaryProvided"] = (!string.IsNullOrWhiteSpace(command.Summary)).ToString(),
            ["SummaryLength"] = (command.Summary?.Length ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["AttemptNumber"] = execution.AttemptNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["WorkerNodeIdPresent"] = (execution.WorkerNodeId is not null).ToString(),
        };
}

public sealed class GetJobHandler(
    IJobRepository jobRepository,
    IScriptDefinitionRepository scriptRepository)
{
    public async Task<JobDetailResponse> HandleAsync(
        GetJobQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            query.JobId,
            cancellationToken);
        var script = await SetJobParameterHandler.GetScriptAsync(
            scriptRepository,
            job.ScriptDefinitionId,
            cancellationToken);
        return Map(job, script);
    }

    internal static JobDetailResponse Map(Job job, ScriptDefinition script)
    {
        try
        {
            var version = script.GetVersion(job.ScriptVersionId);
            var parameters = job.Parameters
                .Select(parameter => MapParameter(parameter, version))
                .ToArray();

            return new(
            job.Id.Value,
            job.ScriptDefinitionId.Value,
            job.ScriptVersionId.Value,
            job.RequestedPhase.ToString(),
            job.Status.ToString(),
            job.RequestedBy.Value,
            job.CreatedUtc,
            job.UpdatedUtc,
            job.SubmittedUtc,
            job.Description,
            job.ChangeReference?.Value,
            job.Targets.Select(target => new JobTargetResponse(
                target.Name.Value,
                target.AddedUtc,
                target.AddedBy.Value)).ToArray(),
            parameters,
            job.Executions.Select(execution => new JobExecutionResponse(
                execution.Id.Value,
                execution.AttemptNumber,
                execution.WorkerNodeId?.Value,
                execution.CreatedUtc,
                execution.StartedUtc,
                execution.CompletedUtc,
                execution.Outcome?.ToString(),
                execution.ExitCode,
                execution.Summary)).ToArray(),
            job.Approvals.Select(approval => new JobApprovalResponse(
                approval.Id.Value,
                approval.Decision.ToString(),
                approval.Approver.Value,
                approval.DecisionUtc,
                approval.Comment)).ToArray());
        }
        catch (Domain.Exceptions.DomainException exception)
        {
            throw new ApplicationConflictException(
                "Job parameter bindings are inconsistent with the pinned script version.",
                exception);
        }
    }

    private static JobParameterResponse MapParameter(JobParameter parameter, ScriptVersion version)
    {
        var definition = version.GetParameterDefinition(parameter.Name);
        definition.ValidateSerializedValue(parameter.SerializedValue);
        var isSensitive = definition.IsSensitive ||
            definition.ParameterType == Domain.ScriptParameterType.SecureReference;

        return new JobParameterResponse(
            definition.Name,
            definition.ParameterType.ToString(),
            isSensitive ? "[REDACTED]" : parameter.SerializedValue ?? "(null)",
            isSensitive,
            isSensitive);
    }
}

public sealed class ListAwaitingApprovalJobsHandler(
    IJobRepository jobRepository,
    IScriptDefinitionRepository scriptRepository)
{
    private const int MaximumQueueSize = 100;

    public async Task<IReadOnlyList<JobDetailResponse>> HandleAsync(
        ListAwaitingApprovalJobsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.MaximumCount is < 1 or > MaximumQueueSize)
        {
            throw new ApplicationValidationException(
                $"Approval queue size must be between 1 and {MaximumQueueSize}.");
        }

        var jobs = await jobRepository.ListAwaitingApprovalAsync(
            query.MaximumCount,
            cancellationToken);
        var results = new List<JobDetailResponse>(jobs.Count);
        var scripts = new Dictionary<ScriptDefinitionId, ScriptDefinition>();
        foreach (var job in jobs)
        {
            if (!scripts.TryGetValue(job.ScriptDefinitionId, out var script))
            {
                script = await SetJobParameterHandler.GetScriptAsync(
                    scriptRepository,
                    job.ScriptDefinitionId,
                    cancellationToken);
                scripts.Add(job.ScriptDefinitionId, script);
            }

            results.Add(GetJobHandler.Map(job, script));
        }

        return results;
    }
}

public sealed record ListJobAuthorizationResourcesQuery(
    IReadOnlyCollection<JobId> JobIds);

public sealed class ListJobAuthorizationResourcesHandler(
    IJobAuthorizationResourceReader authorizationResourceReader)
{
    private const int MaximumResourceCount = 100;

    public async Task<IReadOnlyList<JobAuthorizationResourceResponse>> HandleAsync(
        ListJobAuthorizationResourcesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.JobIds);
        var jobIds = query.JobIds.Distinct().ToArray();
        if (jobIds.Length is < 1 or > MaximumResourceCount)
        {
            throw new ApplicationValidationException(
                $"Job authorization resource count must be between 1 and {MaximumResourceCount}.");
        }

        return await authorizationResourceReader.ListAsync(jobIds, cancellationToken);
    }
}

public sealed class GetApprovalReviewHandler(
    IJobRepository jobRepository,
    IScriptDefinitionRepository scriptRepository,
    IJobFingerprintService fingerprintService)
{
    public async Task<ApprovalReviewResponse> HandleAsync(
        GetApprovalReviewQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            query.JobId,
            cancellationToken);
        var script = await SetJobParameterHandler.GetScriptAsync(
            scriptRepository,
            job.ScriptDefinitionId,
            cancellationToken);
        var expectedFingerprint = await fingerprintService.CreateFingerprintAsync(
            job,
            cancellationToken);
        return new ApprovalReviewResponse(GetJobHandler.Map(job, script), expectedFingerprint);
    }
}
