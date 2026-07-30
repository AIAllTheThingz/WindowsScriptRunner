using WindowsScriptRunner.PowerShell;

namespace WindowsScriptRunner.PowerShellTests;

public sealed class PowerShellProcessLifecycleTests
{
    [Fact]
    public async Task RootExitDoesNotCompleteLifecycleWhileOutputPumpsRemainOpen()
    {
        var outputPumps = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var timeout = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var outputLimit = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lifecycle = PowerShellProcessLifecycle.WaitAsync(
            Task.CompletedTask,
            outputPumps.Task,
            timeout.Task,
            outputLimit.Task,
            cancellation.Task);

        await Task.Yield();
        Assert.False(lifecycle.IsCompleted);

        timeout.SetResult();

        Assert.Same(timeout.Task, await lifecycle);
    }
}
