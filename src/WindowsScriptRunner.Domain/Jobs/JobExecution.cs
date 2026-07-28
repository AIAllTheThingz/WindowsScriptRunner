using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Domain.Jobs;

public sealed class JobExecution
{
    public JobExecution(JobExecutionId id, int attemptNumber, WorkerNodeId? workerNodeId, DateTimeOffset createdUtc)
    {
        if (attemptNumber < 1)
        {
            throw new DomainValidationException("Execution attempt number must be positive.");
        }

        Id = id ?? throw new DomainValidationException("Job execution identifier is required.");
        AttemptNumber = attemptNumber;
        WorkerNodeId = workerNodeId;
        CreatedUtc = createdUtc;
    }

    public JobExecutionId Id { get; }
    public int AttemptNumber { get; }
    public WorkerNodeId? WorkerNodeId { get; }
    public DateTimeOffset CreatedUtc { get; }
    public DateTimeOffset? StartedUtc { get; private set; }
    public DateTimeOffset? CompletedUtc { get; private set; }
    public ExecutionOutcome? Outcome { get; private set; }
    public int? ExitCode { get; private set; }
    public string? Summary { get; private set; }
    public bool IsActive => StartedUtc is not null && CompletedUtc is null;

    internal void Start(DateTimeOffset startedUtc)
    {
        if (StartedUtc is not null)
        {
            throw new DomainValidationException("An execution attempt cannot be started twice.");
        }

        if (startedUtc < CreatedUtc)
        {
            throw new DomainValidationException("Execution start cannot precede creation.");
        }

        StartedUtc = startedUtc;
    }

    internal void Complete(
        ExecutionOutcome outcome,
        int? exitCode,
        string? summary,
        DateTimeOffset completedUtc)
    {
        outcome = EnumGuard.RequireDefined(outcome, nameof(ExecutionOutcome));
        if (StartedUtc is null)
        {
            throw new DomainValidationException("An execution attempt must start before completion.");
        }

        if (CompletedUtc is not null)
        {
            throw new DomainValidationException("An execution attempt cannot be completed twice.");
        }

        if (completedUtc < StartedUtc)
        {
            throw new DomainValidationException("Execution completion cannot precede its start.");
        }

        if (exitCode is null &&
            outcome is not (ExecutionOutcome.Blocked or
                ExecutionOutcome.Cancelled or
                ExecutionOutcome.TimedOut or
                ExecutionOutcome.NotRun))
        {
            throw new DomainValidationException($"Outcome {outcome} requires an exit code.");
        }

        var normalizedSummary = summary?.Trim();
        if (normalizedSummary?.Length > 2000)
        {
            throw new DomainValidationException("Execution summary cannot exceed 2,000 characters.");
        }

        Outcome = outcome;
        ExitCode = exitCode;
        Summary = normalizedSummary;
        CompletedUtc = completedUtc;
    }
}
