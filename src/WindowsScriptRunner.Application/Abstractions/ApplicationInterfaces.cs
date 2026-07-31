using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Credentials;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Reports;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Domain.Workers;

namespace WindowsScriptRunner.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IWorkerCoordinationClock
{
    Task<DateTimeOffset> GetUtcNowAsync(CancellationToken cancellationToken);
}

public interface ICurrentUser
{
    UserIdentity User { get; }
}

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(JobId id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Job>> ListAwaitingApprovalAsync(
        int maximumCount,
        CancellationToken cancellationToken);
    Task<bool> ExistsAsync(JobId id, CancellationToken cancellationToken);
    Task AddAsync(Job job, CancellationToken cancellationToken);
    Task UpdateAsync(Job job, CancellationToken cancellationToken);
    Task UpdateLeaseAsync(Job job, CancellationToken cancellationToken);
    Task<bool> TryRefreshLeaseAsync(
        JobId jobId,
        JobLeaseCredentials credentials,
        CancellationToken cancellationToken);
}

public interface IScriptDefinitionRepository
{
    Task<ScriptDefinition?> GetByIdAsync(
        ScriptDefinitionId id,
        CancellationToken cancellationToken);
    Task AddAsync(ScriptDefinition definition, CancellationToken cancellationToken);
    Task UpdateAsync(ScriptDefinition definition, CancellationToken cancellationToken);
}

public interface IWorkerNodeRepository
{
    Task<WorkerNode?> GetByIdAsync(WorkerNodeId id, CancellationToken cancellationToken);
    Task AddAsync(WorkerNode workerNode, CancellationToken cancellationToken);
    Task UpdateAsync(WorkerNode workerNode, CancellationToken cancellationToken);
}

public interface ICredentialReferenceRepository
{
    Task<CredentialReference?> GetByIdAsync(
        CredentialReferenceId id,
        CancellationToken cancellationToken);
    Task AddAsync(CredentialReference credentialReference, CancellationToken cancellationToken);
    Task UpdateAsync(CredentialReference credentialReference, CancellationToken cancellationToken);
}

public interface IJobReportRepository
{
    Task<JobReport?> GetByIdAsync(
        JobReportId id,
        CancellationToken cancellationToken);
    Task<JobReport?> GetByJobIdAsync(
        JobId jobId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<JobReport>> ListLocalHostInventoryAsync(
        int maximumCount,
        CancellationToken cancellationToken);
    Task AddAsync(JobReport report, CancellationToken cancellationToken);
}

public interface IAuditWriter
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IJobFingerprintService
{
    Task<string> CreateFingerprintAsync(Job job, CancellationToken cancellationToken);
}

public interface IJobQueueCandidateSource
{
    Task<IReadOnlyList<JobQueueCandidate>> FindCandidatesAsync(
        IReadOnlySet<JobWorkRoute> supportedRoutes,
        int maximumCount,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IExpiredJobLeaseCandidateSource
{
    Task<IReadOnlyList<ExpiredJobLeaseCandidate>> FindExpiredAsync(
        int maximumCount,
        CancellationToken cancellationToken);
}

public interface IFencingTokenSource
{
    Task<long> GetNextAsync(CancellationToken cancellationToken);
}
