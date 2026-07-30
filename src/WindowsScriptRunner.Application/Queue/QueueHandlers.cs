using System.Globalization;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Workers;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Domain.Workers;

namespace WindowsScriptRunner.Application.Queue;

public sealed class AcquireJobLeaseHandler(
    IJobRepository jobRepository,
    IWorkerNodeRepository workerRepository,
    IFencingTokenSource fencingTokenSource,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task<ClaimedJobWork> HandleAsync(
        AcquireJobLeaseCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.LeaseDuration <= TimeSpan.Zero)
        {
            throw new ApplicationValidationException("Lease duration must be positive.");
        }

        if (command.WorkerStaleAfter <= TimeSpan.Zero)
        {
            throw new ApplicationValidationException("Worker staleness duration must be positive.");
        }

        if (!Enum.IsDefined(command.WorkKind))
        {
            throw new ApplicationValidationException("Job work kind must be defined.");
        }

        var job = await QueueHandlerSupport.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        if (job.ScriptVersionId != command.ScriptVersionId)
        {
            throw new ApplicationConflictException(
                "The queue candidate script version no longer matches the requested route.");
        }

        var worker = await workerRepository.GetByIdAsync(
            command.WorkerNodeId,
            cancellationToken)
            ?? throw new EntityNotFoundException(
                nameof(WorkerNode),
                command.WorkerNodeId.ToString());
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        if (!worker.IsLive(now, command.WorkerStaleAfter))
        {
            throw new ApplicationValidationException(
                "The worker node is disabled or its heartbeat is stale.");
        }

        var fencingToken = await fencingTokenSource.GetNextAsync(cancellationToken);
        JobLease lease;
        try
        {
            lease = job.AcquireWorkLease(
                JobLeaseId.New(),
                command.WorkerNodeId,
                command.WorkKind,
                fencingToken,
                RegisterWorkerHandler.WorkerActor(command.WorkerNodeId),
                now,
                now + command.LeaseDuration);
        }
        catch (DomainException exception)
        {
            throw new ApplicationConflictException(
                "The queue candidate is no longer eligible for acquisition.",
                exception);
        }
        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            QueueHandlerSupport.LeaseAudit(
                "JobLeaseAcquired",
                job,
                lease,
                RegisterWorkerHandler.WorkerActor(command.WorkerNodeId),
                now,
                "A worker acquired the job lease."),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return QueueHandlerSupport.ToClaimedWork(job, lease);
    }
}

public sealed class RenewJobLeaseHandler(
    IJobRepository jobRepository,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task<DateTimeOffset> HandleAsync(
        RenewJobLeaseCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.LeaseDuration <= TimeSpan.Zero)
        {
            throw new ApplicationValidationException("Lease duration must be positive.");
        }

        var job = await QueueHandlerSupport.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        var expiration = now + command.LeaseDuration;
        try
        {
            job.RenewWorkLease(command.Credentials, now, expiration);
        }
        catch (DomainException exception)
        {
            throw new ApplicationConflictException(
                "The job lease could not be renewed because ownership changed.",
                exception);
        }
        await jobRepository.UpdateLeaseAsync(job, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return expiration;
    }
}

public sealed class ReleaseUnstartedJobLeaseHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task HandleAsync(
        ReleaseUnstartedJobLeaseCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await QueueHandlerSupport.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var lease = job.Lease ??
            throw new ApplicationConflictException("The job no longer has an active lease.");
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        var actor = RegisterWorkerHandler.WorkerActor(command.Credentials.WorkerNodeId);
        try
        {
            job.ReleaseUnstartedWorkLease(command.Credentials, actor, now);
        }
        catch (DomainException exception)
        {
            throw new ApplicationConflictException(
                "The job lease could not be released because ownership or state changed.",
                exception);
        }
        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            QueueHandlerSupport.LeaseAudit(
                "JobLeaseReleased",
                job,
                lease,
                actor,
                now,
                "An unstarted job lease was safely released."),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}

public sealed class RecoverExpiredJobLeaseHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task<JobLeaseRecoveryDisposition> HandleAsync(
        RecoverExpiredJobLeaseCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Candidate);
        var job = await QueueHandlerSupport.GetJobAsync(
            jobRepository,
            command.Candidate.JobId,
            cancellationToken);
        var lease = job.Lease ??
            throw new ApplicationConflictException("The job lease was already recovered.");
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        var actor = QueueHandlerSupport.LeaseRecoveryActor;
        JobLeaseRecoveryDisposition disposition;
        try
        {
            disposition = job.RecoverExpiredWorkLease(
                command.Candidate.Credentials,
                actor,
                now);
        }
        catch (DomainException exception)
        {
            throw new ApplicationConflictException(
                "The expired job lease changed before recovery could commit.",
                exception);
        }
        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            QueueHandlerSupport.LeaseAudit(
                "JobLeaseExpired",
                job,
                lease,
                actor,
                now,
                "An expired job lease was detected."),
            cancellationToken);
        await auditWriter.WriteAsync(
            QueueHandlerSupport.LeaseAudit(
                "JobLeaseRecovered",
                job,
                lease,
                actor,
                now,
                "The expired job lease was recovered.",
                disposition),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return disposition;
    }
}

public sealed class InspectJobLeaseHandler(IJobRepository jobRepository)
{
    public async Task<JobLeaseInspection> HandleAsync(
        InspectJobLeaseQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var job = await QueueHandlerSupport.GetJobAsync(
            jobRepository,
            query.JobId,
            cancellationToken);
        var lease = job.Lease;
        var isCurrent = lease is not null &&
            lease.Id == query.Credentials.LeaseId &&
            lease.WorkerNodeId == query.Credentials.WorkerNodeId &&
            lease.FencingToken == query.Credentials.FencingToken;
        return new JobLeaseInspection(isCurrent, job.Status);
    }
}

public sealed class StartLeasedDryRunHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task HandleAsync(
        StartLeasedDryRunCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await QueueHandlerSupport.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        job.StartDryRun(command.Credentials, command.ActingUser, now);
        await QueueHandlerSupport.CommitJobAuditAsync(
            jobRepository,
            auditWriter,
            unitOfWork,
            job,
            "DryRunStarted",
            command.ActingUser,
            now,
            "The leased dry-run work started.",
            cancellationToken);
    }
}

public sealed class CompleteLeasedDryRunHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task HandleAsync(
        CompleteLeasedDryRunCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await QueueHandlerSupport.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        job.CompleteDryRun(command.Credentials, command.ActingUser, now);
        await QueueHandlerSupport.CommitTerminalJobAuditAsync(
            jobRepository,
            auditWriter,
            unitOfWork,
            job,
            command.Credentials,
            "DryRunCompleted",
            command.ActingUser,
            now,
            "The leased dry-run work completed and resolved its lease.",
            null,
            cancellationToken);
    }
}

public sealed class CompleteLeasedReadOnlyDryRunHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task HandleAsync(
        CompleteLeasedReadOnlyDryRunCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await QueueHandlerSupport.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        job.CompleteDryRun(command.Credentials, command.ActingUser, now);
        job.CompleteReadOnlyAfterDryRun(command.ActingUser, now);
        await QueueHandlerSupport.CommitTerminalJobAuditAsync(
            jobRepository,
            auditWriter,
            unitOfWork,
            job,
            command.Credentials,
            "ReadOnlyDryRunCompleted",
            command.ActingUser,
            now,
            "The leased read-only dry-run completed and resolved its lease.",
            null,
            cancellationToken);
    }
}

public sealed class TerminateLeasedDryRunHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task HandleAsync(
        TerminateLeasedDryRunCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await QueueHandlerSupport.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        job.TerminateDryRun(
            command.Credentials,
            command.Outcome,
            command.ActingUser,
            now);
        await QueueHandlerSupport.CommitTerminalJobAuditAsync(
            jobRepository,
            auditWriter,
            unitOfWork,
            job,
            command.Credentials,
            "DryRunTerminated",
            command.ActingUser,
            now,
            "The leased dry-run work reached a controlled terminal state and resolved its lease.",
            new Dictionary<string, string>
            {
                ["Outcome"] = command.Outcome.ToString(),
            },
            cancellationToken);
    }
}

public sealed class StartLeasedExecutionHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task<JobExecution> HandleAsync(
        StartLeasedExecutionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await QueueHandlerSupport.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        var execution = job.StartLeasedExecutionAttempt(
            command.Credentials,
            command.ActingUser,
            now);
        await QueueHandlerSupport.CommitJobAuditAsync(
            jobRepository,
            auditWriter,
            unitOfWork,
            job,
            "ExecutionAttemptStarted",
            command.ActingUser,
            now,
            "The leased execution attempt started.",
            cancellationToken);
        return execution;
    }
}

public sealed class BeginLeasedPostValidationHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task HandleAsync(
        BeginLeasedPostValidationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await QueueHandlerSupport.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        job.BeginPostValidation(command.Credentials, command.ActingUser, now);
        await QueueHandlerSupport.CommitJobAuditAsync(
            jobRepository,
            auditWriter,
            unitOfWork,
            job,
            "PostValidationStarted",
            command.ActingUser,
            now,
            "The leased execution entered post-validation.",
            cancellationToken);
    }
}

public sealed class RecordLeasedExecutionOutcomeHandler(
    IJobRepository jobRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    public async Task<JobExecution> HandleAsync(
        RecordLeasedExecutionOutcomeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var job = await QueueHandlerSupport.GetJobAsync(
            jobRepository,
            command.JobId,
            cancellationToken);
        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        var execution = job.RecordTerminalExecutionOutcome(
            command.Credentials,
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
            command.Credentials,
            "ExecutionOutcomeRecorded",
            command.ActingUser,
            now,
            "The leased execution attempt completed and resolved its lease.",
            null,
            cancellationToken);
        return execution;
    }
}

internal static class QueueHandlerSupport
{
    internal static UserIdentity LeaseRecoveryActor { get; } =
        new("system:lease-recovery");

    internal static async Task<Job> GetJobAsync(
        IJobRepository repository,
        JobId jobId,
        CancellationToken cancellationToken) =>
        await repository.GetByIdAsync(jobId, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(Job), jobId.ToString());

    internal static ClaimedJobWork ToClaimedWork(Job job, JobLease lease) =>
        new(
            job.Id,
            lease.WorkKind,
            job.ScriptVersionId,
            lease.Id,
            lease.WorkerNodeId,
            lease.FencingToken,
            lease.ExpiresUtc);

    internal static AuditEvent LeaseAudit(
        string eventType,
        Job job,
        JobLease lease,
        UserIdentity actor,
        DateTimeOffset occurredUtc,
        string summary,
        JobLeaseRecoveryDisposition? disposition = null)
    {
        var properties = new Dictionary<string, string>
        {
            ["WorkKind"] = lease.WorkKind.ToString(),
            ["WorkerNodeId"] = lease.WorkerNodeId.ToString(),
            ["LeaseId"] = lease.Id.ToString(),
            ["FencingToken"] = lease.FencingToken.ToString(CultureInfo.InvariantCulture),
            ["ExpiresUtc"] = lease.ExpiresUtc.ToString("O", CultureInfo.InvariantCulture),
        };
        if (disposition is not null)
        {
            properties["RecoveryDisposition"] = disposition.Value.ToString();
        }

        return new AuditEvent(
            AuditEventId.New(),
            eventType,
            nameof(Job),
            job.Id.ToString(),
            actor,
            occurredUtc,
            summary,
            properties);
    }

    internal static async Task CommitJobAuditAsync(
        IJobRepository jobRepository,
        IAuditWriter auditWriter,
        IUnitOfWork unitOfWork,
        Job job,
        string eventType,
        UserIdentity actor,
        DateTimeOffset occurredUtc,
        string summary,
        CancellationToken cancellationToken)
    {
        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            new AuditEvent(
                AuditEventId.New(),
                eventType,
                nameof(Job),
                job.Id.ToString(),
                actor,
                occurredUtc,
                summary),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    internal static async Task CommitTerminalJobAuditAsync(
        IJobRepository jobRepository,
        IAuditWriter auditWriter,
        IUnitOfWork unitOfWork,
        Job job,
        JobLeaseCredentials credentials,
        string eventType,
        UserIdentity actor,
        DateTimeOffset occurredUtc,
        string summary,
        IReadOnlyDictionary<string, string>? properties,
        CancellationToken cancellationToken)
    {
        const int maximumCommitAttempts = 3;
        await jobRepository.UpdateAsync(job, cancellationToken);
        await auditWriter.WriteAsync(
            new AuditEvent(
                AuditEventId.New(),
                eventType,
                nameof(Job),
                job.Id.ToString(),
                actor,
                occurredUtc,
                summary,
                properties),
            cancellationToken);

        for (var attempt = 1; attempt <= maximumCommitAttempts; attempt++)
        {
            try
            {
                await unitOfWork.CommitAsync(cancellationToken);
                return;
            }
            catch (ApplicationConflictException)
                when (attempt < maximumCommitAttempts)
            {
                var refreshed = await jobRepository.TryRefreshLeaseAsync(
                    job.Id,
                    credentials,
                    cancellationToken);
                if (!refreshed)
                {
                    throw;
                }
            }
        }
    }
}
