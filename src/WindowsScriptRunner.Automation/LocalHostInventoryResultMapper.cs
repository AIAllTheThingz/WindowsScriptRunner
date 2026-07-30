using WindowsScriptRunner.Domain;
using WindowsScriptRunner.PowerShell;

namespace WindowsScriptRunner.Automation;

internal sealed record LocalHostInventoryResultMapping(
    bool Succeeded,
    ExecutionOutcome? Outcome);

internal static class LocalHostInventoryResultMapper
{
    internal static LocalHostInventoryResultMapping Map(PowerShellExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.TerminationReason switch
        {
            PowerShellTerminationReason.Exited when result.ExitCode == 0 =>
                new(true, null),
            PowerShellTerminationReason.Exited =>
                new(false, ExecutionOutcome.Failed),
            PowerShellTerminationReason.TimedOut =>
                new(false, ExecutionOutcome.TimedOut),
            PowerShellTerminationReason.OutputLimitExceeded =>
                new(false, ExecutionOutcome.Failed),
            _ => throw new AutomationPackageTrustException(
                "The PowerShell result contains an unsupported termination reason."),
        };
    }
}
