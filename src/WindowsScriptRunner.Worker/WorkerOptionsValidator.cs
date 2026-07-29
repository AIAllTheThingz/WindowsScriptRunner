using Microsoft.Extensions.Options;

namespace WindowsScriptRunner.Worker;

public sealed class WorkerOptionsValidator : IValidateOptions<WorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (options.NodeId == Guid.Empty && !options.AllowEphemeralNodeId)
        {
            failures.Add("NodeId must be a non-empty GUID unless AllowEphemeralNodeId is true.");
        }

        if (string.IsNullOrWhiteSpace(options.Name) || options.Name.Trim().Length > 200)
        {
            failures.Add("Name must contain between 1 and 200 characters.");
        }

        RequireRange(
            options.HeartbeatIntervalSeconds,
            1,
            WorkerOptions.MaximumHeartbeatIntervalSeconds,
            nameof(options.HeartbeatIntervalSeconds),
            failures);
        RequireRange(
            options.WorkerStaleAfterSeconds,
            1,
            86400,
            nameof(options.WorkerStaleAfterSeconds),
            failures);
        if (options.WorkerStaleAfterSeconds <= options.HeartbeatIntervalSeconds * 2)
        {
            failures.Add("WorkerStaleAfterSeconds must be greater than two heartbeat intervals.");
        }

        RequireRange(
            options.QueuePollingIntervalMilliseconds,
            50,
            60000,
            nameof(options.QueuePollingIntervalMilliseconds),
            failures);
        RequireRange(
            options.EmptyQueueBackoffMaximumSeconds,
            1,
            3600,
            nameof(options.EmptyQueueBackoffMaximumSeconds),
            failures);
        RequireRange(
            options.PersistenceFailureBackoffMaximumSeconds,
            1,
            3600,
            nameof(options.PersistenceFailureBackoffMaximumSeconds),
            failures);
        if (options.PersistenceFailureBackoffMaximumSeconds <
            options.HeartbeatIntervalSeconds)
        {
            failures.Add(
                "PersistenceFailureBackoffMaximumSeconds must be at least HeartbeatIntervalSeconds.");
        }

        if (options.EmptyQueueBackoffMaximumSeconds * 1000L <
            options.QueuePollingIntervalMilliseconds)
        {
            failures.Add(
                "EmptyQueueBackoffMaximumSeconds must be at least QueuePollingIntervalMilliseconds.");
        }

        if (options.PersistenceFailureBackoffMaximumSeconds * 1000L <
            options.QueuePollingIntervalMilliseconds)
        {
            failures.Add(
                "PersistenceFailureBackoffMaximumSeconds must be at least QueuePollingIntervalMilliseconds.");
        }
        RequireRange(
            options.LeaseDurationSeconds,
            2,
            3600,
            nameof(options.LeaseDurationSeconds),
            failures);
        RequireRange(
            options.LeaseRenewalIntervalSeconds,
            1,
            1800,
            nameof(options.LeaseRenewalIntervalSeconds),
            failures);
        if (options.LeaseRenewalIntervalSeconds * 2 >= options.LeaseDurationSeconds)
        {
            failures.Add("LeaseRenewalIntervalSeconds must be less than half LeaseDurationSeconds.");
        }

        RequireRange(
            options.LeaseRecoveryIntervalSeconds,
            1,
            3600,
            nameof(options.LeaseRecoveryIntervalSeconds),
            failures);
        RequireRange(
            options.DrainTimeoutSeconds,
            1,
            3600,
            nameof(options.DrainTimeoutSeconds),
            failures);
        RequireRange(
            options.MaxConcurrentJobs,
            1,
            WorkerOptions.MaximumConcurrentJobs,
            nameof(options.MaxConcurrentJobs),
            failures);
        RequireRange(
            options.ClaimCandidateBatchSize,
            1,
            WorkerOptions.MaximumCandidateBatchSize,
            nameof(options.ClaimCandidateBatchSize),
            failures);

        options.Capabilities ??= [];
        for (var index = 0; index < options.Capabilities.Count; index++)
        {
            var capability = options.Capabilities[index];
            if (capability is null ||
                string.IsNullOrWhiteSpace(capability.Name) ||
                capability.Name.Trim().Length > 200 ||
                string.IsNullOrWhiteSpace(capability.Value) ||
                capability.Value.Trim().Length > 200)
            {
                failures.Add($"Capabilities[{index}] must have names and values between 1 and 200 characters.");
            }
        }

        var duplicate = options.Capabilities
            .Where(capability => capability is not null)
            .GroupBy(capability => capability.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            failures.Add($"Capability name '{duplicate.Key}' is duplicated.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void RequireRange(
        int value,
        int minimum,
        int maximum,
        string optionName,
        ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"{optionName} must be between {minimum} and {maximum}.");
        }
    }
}
