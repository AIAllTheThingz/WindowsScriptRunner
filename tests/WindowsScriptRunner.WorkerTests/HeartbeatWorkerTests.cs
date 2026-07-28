using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsScriptRunner.Worker;

namespace WindowsScriptRunner.WorkerTests;

public sealed class HeartbeatWorkerTests
{
    [Fact]
    public async Task CancellationStopsHeartbeatLoop()
    {
        var timer = new BlockingHeartbeatTimer();
        var worker = CreateWorker(timer);

        await worker.StartAsync(CancellationToken.None);
        await timer.FirstWait.WaitAsync(TimeSpan.FromSeconds(1));
        await worker.StopAsync(CancellationToken.None);

        Assert.True(timer.Disposed);
    }

    [Fact]
    public async Task HeartbeatLoopDoesNotBusyLoop()
    {
        var timer = new BlockingHeartbeatTimer();
        var worker = CreateWorker(timer);

        await worker.StartAsync(CancellationToken.None);
        await timer.FirstWait.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, timer.WaitCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public void InvalidHeartbeatConfigurationIsRejected(int intervalSeconds)
    {
        var options = new WorkerOptions { HeartbeatIntervalSeconds = intervalSeconds };

        var result = new WorkerOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
    }

    private static HeartbeatWorker CreateWorker(IHeartbeatTimer timer)
    {
        var options = Options.Create(new WorkerOptions { HeartbeatIntervalSeconds = 30 });
        return new HeartbeatWorker(NullLogger<HeartbeatWorker>.Instance, options, timer);
    }

    private sealed class BlockingHeartbeatTimer : IHeartbeatTimer
    {
        private readonly TaskCompletionSource _firstWait =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstWait => _firstWait.Task;

        public int WaitCount { get; private set; }

        public bool Disposed { get; private set; }

        public async ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken)
        {
            WaitCount++;
            _firstWait.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return false;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
