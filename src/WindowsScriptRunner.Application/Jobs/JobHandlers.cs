using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Contracts.Jobs;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Application.Jobs;

public sealed class CreateDraftJobHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<JobId> HandleAsync(
        CreateDraftJobCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = clock.UtcNow;
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
    IClock clock)
{
    public async Task HandleAsync(AddJobTargetCommand command, CancellationToken cancellationToken)
    {
        var job = await GetJobAsync(jobRepository, command.JobId, cancellationToken);
        var now = clock.UtcNow;
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
    IClock clock)
{
    public async Task HandleAsync(
        SetJobParameterCommand command,
        CancellationToken cancellationToken)
    {
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
        var now = clock.UtcNow;

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
                serializedValue = await ResolveCredentialReferenceAsync(
                    credentialRepository,
                    suppliedValue,
                    cancellationToken);
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

    private static async Task<string> ResolveCredentialReferenceAsync(
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

        return credentialReference.Id.ToString();
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
            ["SerializedLength"] = (serializedValue?.Length ?? 0).ToString(
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
    IClock clock)
{
    public async Task HandleAsync(SubmitJobCommand command, CancellationToken cancellationToken)
    {
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var script = await SetJobParameterHandler.GetScriptAsync(
            scriptRepository,
            job.ScriptDefinitionId,
            cancellationToken);
        var now = clock.UtcNow;
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
    IClock clock)
{
    public async Task HandleAsync(
        TransitionJobCommand command,
        CancellationToken cancellationToken)
    {
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var previous = job.Status;
        var now = clock.UtcNow;
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
            case Domain.JobStatus.DryRunRunning:
                job.StartDryRun(command.ActingUser, now);
                break;
            case Domain.JobStatus.DryRunCompleted:
                job.CompleteDryRun(command.ActingUser, now);
                break;
            case Domain.JobStatus.AwaitingApproval:
                job.RequireApproval(command.ActingUser, now);
                break;
            case Domain.JobStatus.ExecutionQueued:
                job.QueueExecution(command.ActingUser, now);
                break;
            case Domain.JobStatus.Claimed:
                job.Claim(command.ActingUser, now);
                break;
            case Domain.JobStatus.PostValidation:
                job.BeginPostValidation(command.ActingUser, now);
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

        return job.Status is Domain.JobStatus.Executing or Domain.JobStatus.PostValidation ||
            job.HasActiveExecutionAttempt;
    }
}

public sealed class ApproveJobHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task HandleAsync(ApproveJobCommand command, CancellationToken cancellationToken)
    {
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = clock.UtcNow;
        job.RecordApproval(
            command.ActingUser,
            command.ApprovalFingerprint,
            command.Comment,
            now);
        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            CreateDraftJobHandler.Audit(
                "JobApproved",
                job,
                command.ActingUser,
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
    IClock clock)
{
    public async Task HandleAsync(RejectJobCommand command, CancellationToken cancellationToken)
    {
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = clock.UtcNow;
        job.RecordRejection(
            command.ActingUser,
            command.ApprovalFingerprint,
            command.Comment,
            now);
        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            CreateDraftJobHandler.Audit(
                "JobRejected",
                job,
                command.ActingUser,
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
    IClock clock)
{
    public async Task HandleAsync(
        CompleteReadOnlyJobCommand command,
        CancellationToken cancellationToken)
    {
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = clock.UtcNow;
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
    IClock clock)
{
    public async Task HandleAsync(
        CompleteValidationJobCommand command,
        CancellationToken cancellationToken)
    {
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = clock.UtcNow;
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
    IClock clock)
{
    public async Task HandleAsync(
        CompleteDryRunJobCommand command,
        CancellationToken cancellationToken)
    {
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = clock.UtcNow;
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

public sealed class RecordExecutionOutcomeHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task HandleAsync(
        RecordExecutionOutcomeCommand command,
        CancellationToken cancellationToken)
    {
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = clock.UtcNow;
        var execution = job.RecordTerminalExecutionOutcome(
            command.Outcome,
            command.ExitCode,
            command.Summary,
            command.ActingUser,
            now);
        var audit = CreateDraftJobHandler.Audit(
            "ExecutionOutcomeRecorded",
            job,
            command.ActingUser,
            now,
            "The active execution attempt was completed.",
            CreateExecutionOutcomeAuditProperties(command, execution));

        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(audit, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
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

    private static JobDetailResponse Map(Job job, ScriptDefinition script)
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
