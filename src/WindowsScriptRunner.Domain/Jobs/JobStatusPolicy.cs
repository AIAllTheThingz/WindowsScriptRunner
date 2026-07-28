using WindowsScriptRunner.Domain.Exceptions;

namespace WindowsScriptRunner.Domain.Jobs;

internal static class JobStatusPolicy
{
    private static readonly IReadOnlyDictionary<JobStatus, IReadOnlySet<JobStatus>> NormalTransitions =
        new Dictionary<JobStatus, IReadOnlySet<JobStatus>>
        {
            [JobStatus.Draft] = Set(JobStatus.Submitted),
            [JobStatus.Submitted] = Set(JobStatus.Validated),
            [JobStatus.Validated] = Set(JobStatus.DryRunQueued),
            [JobStatus.DryRunQueued] = Set(JobStatus.DryRunRunning),
            [JobStatus.DryRunRunning] = Set(JobStatus.DryRunCompleted),
            [JobStatus.DryRunCompleted] = Set(JobStatus.AwaitingApproval, JobStatus.Completed),
            [JobStatus.AwaitingApproval] = Set(JobStatus.Approved, JobStatus.Rejected),
            [JobStatus.Approved] = Set(JobStatus.ExecutionQueued),
            [JobStatus.ExecutionQueued] = Set(JobStatus.Claimed),
            [JobStatus.Claimed] = Set(JobStatus.Executing),
            [JobStatus.Executing] = Set(
                JobStatus.PostValidation,
                JobStatus.Completed,
                JobStatus.CompletedWithWarnings),
            [JobStatus.PostValidation] = Set(JobStatus.Completed, JobStatus.CompletedWithWarnings),
        };

    private static readonly IReadOnlySet<JobStatus> TerminalStates = Set(
        JobStatus.Completed,
        JobStatus.CompletedWithWarnings,
        JobStatus.Failed,
        JobStatus.Rejected,
        JobStatus.Cancelled,
        JobStatus.TimedOut,
        JobStatus.Blocked,
        JobStatus.NotRun);

    private static readonly IReadOnlySet<JobStatus> ControlledTerminalStates = Set(
        JobStatus.Failed,
        JobStatus.Cancelled,
        JobStatus.TimedOut,
        JobStatus.Blocked,
        JobStatus.NotRun);

    internal static void EnsureAllowed(JobStatus current, JobStatus requested)
    {
        if (current == requested ||
            TerminalStates.Contains(current) ||
            (!IsNormalTransition(current, requested) && !IsControlledTerminalTransition(current, requested)))
        {
            throw new InvalidJobStateTransitionException(current, requested);
        }
    }

    private static bool IsNormalTransition(JobStatus current, JobStatus requested) =>
        NormalTransitions.TryGetValue(current, out var allowed) && allowed.Contains(requested);

    private static bool IsControlledTerminalTransition(JobStatus current, JobStatus requested)
    {
        if (!ControlledTerminalStates.Contains(requested))
        {
            return false;
        }

        return requested switch
        {
            JobStatus.Failed or JobStatus.TimedOut => current is not JobStatus.Draft,
            JobStatus.Cancelled or JobStatus.Blocked or JobStatus.NotRun => !TerminalStates.Contains(current),
            _ => false,
        };
    }

    private static IReadOnlySet<JobStatus> Set(params JobStatus[] values) => new HashSet<JobStatus>(values);
}
