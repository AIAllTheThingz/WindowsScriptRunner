using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsScriptRunner.Worker;

namespace WindowsScriptRunner.WorkerTests;

public sealed class HeartbeatWorkerTests
{
    [Fact]
    public async Task CancellationStopsHeartbeatLoopCleanly()
    {
        var timer = new BlockingHeartbeatTimer();
        var logger = new RecordingLogger<HeartbeatWorker>();
        var worker = CreateWorker(timer, logger);

        await worker.StartAsync(CancellationToken.None);
        await timer.FirstWait.WaitAsync(TimeSpan.FromSeconds(1));
        await worker.StopAsync(CancellationToken.None);

        Assert.True(timer.Disposed);
        Assert.Contains(logger.Messages, message => message.Contains("cancellation requested", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("stopped cleanly", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NaturalTimerCompletionStopsCleanly()
    {
        var timer = new CompletedHeartbeatTimer();
        var logger = new RecordingLogger<HeartbeatWorker>();
        var worker = CreateWorker(timer, logger);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        Assert.True(timer.Disposed);
        Assert.Contains(logger.Messages, message => message.Contains("stopped cleanly", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnexpectedTimerExceptionIsLoggedRethrownAndDisposed()
    {
        var timer = new ThrowingHeartbeatTimer();
        var logger = new RecordingLogger<HeartbeatWorker>();
        var worker = CreateWorker(timer, logger);

        await worker.StartAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.ExecuteTask!);

        Assert.True(timer.Disposed);
        Assert.Contains(logger.Levels, level => level == LogLevel.Error);
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("stopped cleanly", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HeartbeatLoopDoesNotBusyLoop()
    {
        var timer = new BlockingHeartbeatTimer();
        var worker = CreateWorker(timer, new RecordingLogger<HeartbeatWorker>());

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

    private static HeartbeatWorker CreateWorker(
        IHeartbeatTimer timer,
        ILogger<HeartbeatWorker> logger)
    {
        var options = Options.Create(new WorkerOptions { HeartbeatIntervalSeconds = 30 });
        return new HeartbeatWorker(logger, options, timer);
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

    private sealed class CompletedHeartbeatTimer : IHeartbeatTimer
    {
        public bool Disposed { get; private set; }
        public ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingHeartbeatTimer : IHeartbeatTimer
    {
        public bool Disposed { get; private set; }
        public ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<bool>(new InvalidOperationException("Timer failure."));

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Levels.Add(logLevel);
            Messages.Add(formatter(state, exception));
        }
    }
}
