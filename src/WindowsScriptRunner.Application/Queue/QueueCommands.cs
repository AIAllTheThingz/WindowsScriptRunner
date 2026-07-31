using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Application.Queue;

public sealed record AcquireJobLeaseCommand(
    JobId JobId,
    JobWorkKind WorkKind,
    ScriptVersionId ScriptVersionId,
    WorkerNodeId WorkerNodeId,
    TimeSpan LeaseDuration,
    TimeSpan WorkerStaleAfter);

public sealed record RenewJobLeaseCommand(
    JobId JobId,
    JobLeaseCredentials Credentials,
    TimeSpan LeaseDuration);

public sealed record ReleaseUnstartedJobLeaseCommand(
    JobId JobId,
    JobLeaseCredentials Credentials);

public sealed record RecoverExpiredJobLeaseCommand(ExpiredJobLeaseCandidate Candidate);

public sealed record InspectJobLeaseQuery(
    JobId JobId,
    JobLeaseCredentials Credentials);

public sealed record StartLeasedDryRunCommand(
    JobId JobId,
    JobLeaseCredentials Credentials,
    UserIdentity ActingUser);

public sealed record CompleteLeasedDryRunCommand(
    JobId JobId,
    JobLeaseCredentials Credentials,
    UserIdentity ActingUser);

public sealed record CompleteLeasedReadOnlyDryRunCommand(
    JobId JobId,
    JobLeaseCredentials Credentials,
    UserIdentity ActingUser);

public sealed record TerminateLeasedDryRunCommand(
    JobId JobId,
    JobLeaseCredentials Credentials,
    ExecutionOutcome Outcome,
    UserIdentity ActingUser);

public sealed record StartLeasedExecutionCommand(
    JobId JobId,
    JobLeaseCredentials Credentials,
    UserIdentity ActingUser);

public sealed record BeginLeasedPostValidationCommand(
    JobId JobId,
    JobLeaseCredentials Credentials,
    UserIdentity ActingUser);

public sealed record RecordLeasedExecutionOutcomeCommand(
    JobId JobId,
    JobLeaseCredentials Credentials,
    ExecutionOutcome Outcome,
    int? ExitCode,
    string? Summary,
    UserIdentity ActingUser);
