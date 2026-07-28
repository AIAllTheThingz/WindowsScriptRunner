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
        var now = clock.UtcNow;
        job.SetParameter(definition, command.SerializedValue, command.ActingUser, now);

        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            CreateDraftJobHandler.Audit(
                "JobParameterSet",
                job,
                command.ActingUser,
                now,
                "A draft job parameter was set.",
                new Dictionary<string, string>
                {
                    ["Parameter"] = definition.Name,
                    ["Value"] = definition.IsSensitive
                        ? "[REDACTED]"
                        : command.SerializedValue ?? "(null)",
                }),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    internal static async Task<ScriptDefinition> GetScriptAsync(
        IScriptDefinitionRepository repository,
        ScriptDefinitionId id,
        CancellationToken cancellationToken) =>
        await repository.GetByIdAsync(id, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(ScriptDefinition), id.ToString());
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

public sealed class GetJobHandler(IJobRepository jobRepository)
{
    public async Task<JobDetailResponse> HandleAsync(
        GetJobQuery query,
        CancellationToken cancellationToken)
    {
        var job = await AddJobTargetHandler.GetJobAsync(
            jobRepository,
            query.JobId,
            cancellationToken);
        return Map(job);
    }

    private static JobDetailResponse Map(Job job) =>
        new(
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
            job.Parameters.Select(parameter => new JobParameterResponse(
                parameter.Name,
                parameter.ParameterType.ToString(),
                parameter.GetSafeDisplayValue(),
                parameter.IsSensitive,
                parameter.IsSensitive)).ToArray(),
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
