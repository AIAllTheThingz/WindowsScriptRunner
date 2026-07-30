namespace WindowsScriptRunner.PowerShell;

internal static class PowerShellProcessLifecycle
{
    public static async Task<Task> WaitAsync(
        Task exitTask,
        Task outputPumpsTask,
        Task timeoutTask,
        Task outputLimitTask,
        Task cancellationTask)
    {
        var completed = await Task.WhenAny(
                exitTask,
                timeoutTask,
                outputLimitTask,
                cancellationTask)
            .ConfigureAwait(false);
        if (completed != exitTask)
        {
            return completed;
        }

        return await Task.WhenAny(
                outputPumpsTask,
                timeoutTask,
                outputLimitTask,
                cancellationTask)
            .ConfigureAwait(false);
    }
}
