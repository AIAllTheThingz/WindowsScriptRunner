using Microsoft.Extensions.Options;

namespace WindowsScriptRunner.Worker;

public sealed class WorkerOptionsValidator : IValidateOptions<WorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.HeartbeatIntervalSeconds is > 0 and <= WorkerOptions.MaximumHeartbeatIntervalSeconds
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"HeartbeatIntervalSeconds must be between 1 and {WorkerOptions.MaximumHeartbeatIntervalSeconds}.");
    }
}
