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
            ObserveFault(exitTask);
            return completed;
        }

        await exitTask.ConfigureAwait(false);
        return await Task.WhenAny(
                outputPumpsTask,
                timeoutTask,
                outputLimitTask,
                cancellationTask)
            .ConfigureAwait(false);
    }

    private static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted |
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
