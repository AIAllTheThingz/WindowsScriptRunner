using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Worker;

namespace WindowsScriptRunner.WorkerTests;

public sealed class JobQueueWorkerTests
{
    [Fact]
    public async Task NoHandlersMeansNoCandidateQueryOrClaim()
    {
        using var fixture = await RegisteredFixtureAsync();
        AddJobs(fixture, 1);
        var delay = new BlockingSignalDelay();
        var worker = fixture.QueueService(delay);

        await worker.StartAsync(CancellationToken.None);
        await delay.Called.WaitAsync(TimeSpan.FromSeconds(1));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, fixture.Candidates.CallCount);
        Assert.All(fixture.Jobs.Jobs.Values, job => Assert.Null(job.Lease));
    }

    [Theory]
    [InlineData(JobWorkKind.DryRun)]
    [InlineData(JobWorkKind.Execute)]
    public async Task CandidateDiscoveryUsesOnlyRegisteredHandlerKinds(JobWorkKind workKind)
    {
        using var fixture = await RegisteredFixtureAsync();
        var delay = new BlockingSignalDelay();
        var worker = fixture.QueueService(delay, new ReturningHandler(workKind));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => fixture.Candidates.CallCount > 0);
        await worker.StopAsync(CancellationToken.None);

        Assert.All(
            fixture.Candidates.RequestedRoutes,
            routes => Assert.Equal(
                WorkerTestSupport.Route(workKind),
                routes));
    }

    [Fact]
    public async Task EmptyQueueBackoffIncreasesAndHonorsCancellation()
    {
        using var fixture = await RegisteredFixtureAsync();
        var delay = new FastRecordingDelay(completionsBeforeBlocking: 3);
        var worker = fixture.QueueService(delay, new ReturningHandler(JobWorkKind.DryRun));

        await worker.StartAsync(CancellationToken.None);
        await delay.Blocked.WaitAsync(TimeSpan.FromSeconds(1));
        await worker.StopAsync(CancellationToken.None);

        Assert.True(fixture.Candidates.CallCount >= 4);
        Assert.Equal(
            [50, 100, 200],
            delay.Delays.Take(3).Select(value => (int)value.TotalMilliseconds));
    }

    [Fact]
    public async Task PersistenceFailureBackoffIsSeparateFromEmptyQueueBackoff()
    {
        using var fixture = await RegisteredFixtureAsync();
        fixture.Candidates.FailuresRemaining = 2;
        var delay = new FastRecordingDelay(completionsBeforeBlocking: 3);
        var worker = fixture.QueueService(delay, new ReturningHandler(JobWorkKind.DryRun));

        await worker.StartAsync(CancellationToken.None);
        await delay.Blocked.WaitAsync(TimeSpan.FromSeconds(1));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(
            [50, 100, 50],
            delay.Delays.Take(3).Select(value => (int)value.TotalMilliseconds));
        Assert.True(fixture.State.QueuePollingHealthy);
    }

    [Fact]
    public async Task WorkFoundResetsEmptyQueueBackoff()
    {
        using var fixture = await RegisteredFixtureAsync();
        AddJobs(fixture, 1);
        fixture.Candidates.ResponsePlan.Enqueue(false);
        fixture.Candidates.ResponsePlan.Enqueue(true);
        fixture.Candidates.ResponsePlan.Enqueue(false);
        var handler = new CompletingDryRunHandler(fixture);
        var delay = new FastRecordingDelay(completionsBeforeBlocking: 3);
        var worker = fixture.QueueService(delay, handler);

        await worker.StartAsync(CancellationToken.None);
        await handler.Completed.WaitAsync(TimeSpan.FromSeconds(1));
        await delay.Blocked.WaitAsync(TimeSpan.FromSeconds(1));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(50, (int)delay.Delays[0].TotalMilliseconds);
        Assert.Equal(50, (int)delay.Delays[1].TotalMilliseconds);
    }

    [Fact]
    public async Task MaximumConcurrencyIsHonoredAndShutdownStopsNewClaims()
    {
        using var fixture = await RegisteredFixtureAsync();
        fixture.Options.MaxConcurrentJobs = 2;
        AddJobs(fixture, 3);
        var handler = new BlockingHandler(JobWorkKind.DryRun, expectedStarts: 2);
        var worker = fixture.QueueService(new SystemWorkerDelay(), handler);

        await worker.StartAsync(CancellationToken.None);
        await handler.ExpectedStartsReached.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, handler.StartCount);
        Assert.Equal(2, fixture.State.ActiveDispatchCount);

        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, handler.StartCount);
        Assert.Equal(0, fixture.State.ActiveDispatchCount);
        Assert.Equal(2, fixture.Audits.Events.Count(
            audit => audit.EventType == "JobLeaseReleased"));
    }

    [Fact]
    public async Task CompletedDispatchFreesSlotAndExplicitLifecycleResolutionSucceeds()
    {
        using var fixture = await RegisteredFixtureAsync();
        AddJobs(fixture, 2);
        var handler = new CompletingDryRunHandler(fixture, expectedCompletions: 2);
        var worker = fixture.QueueService(new SystemWorkerDelay(), handler);

        await worker.StartAsync(CancellationToken.None);
        await handler.Completed.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => fixture.State.ActiveDispatchCount == 0);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, handler.Count);
        Assert.All(
            fixture.Jobs.Jobs.Values,
            job =>
            {
                Assert.Equal(JobStatus.DryRunCompleted, job.Status);
                Assert.Null(job.Lease);
            });
    }

    [Fact]
    public async Task HandlerReturnWithActiveLeaseTriggersInvariantReleaseAndFailureLog()
    {
        using var fixture = await RegisteredFixtureAsync();
        AddJobs(fixture, 1);
        var handler = new SignalingReturningHandler(JobWorkKind.DryRun);
        var worker = fixture.QueueService(new SystemWorkerDelay(), handler);

        await worker.StartAsync(CancellationToken.None);
        await handler.Returned.WaitAsync(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() =>
            fixture.Jobs.Jobs.Values.Single().Lease is null);
        await worker.StopAsync(CancellationToken.None);

        Assert.Contains(
            fixture.QueueLogger.Messages,
            message => message.Contains(
                "remained active",
                StringComparison.Ordinal));
        Assert.Contains(
            fixture.Audits.Events,
            audit => audit.EventType == "JobLeaseReleased");
    }

    [Fact]
    public async Task HandlerExceptionIsObservedAndSafelyReleasesUnstartedLease()
    {
        using var fixture = await RegisteredFixtureAsync();
        AddJobs(fixture, 1);
        var handler = new ThrowingHandler();
        var worker = fixture.QueueService(new SystemWorkerDelay(), handler);

        await worker.StartAsync(CancellationToken.None);
        await handler.Invoked.WaitAsync(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() =>
            fixture.Jobs.Jobs.Values.Single().Lease is null);
        await worker.StopAsync(CancellationToken.None);

        Assert.Contains(
            fixture.QueueLogger.Messages,
            message => message.Contains("failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FaultedDispatchIsObservedAndDoesNotStopQueuePolling()
    {
        using var fixture = await RegisteredFixtureAsync();
        fixture.Options.LeaseRenewalIntervalSeconds = 1;
        AddJobs(fixture, 1);
        var delay = new FaultingRenewalDelay();
        var handler = new RenewalFaultAwaitingHandler(delay);
        var worker = fixture.QueueService(delay, handler);

        await worker.StartAsync(CancellationToken.None);
        await delay.FaultInjected.WaitAsync(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => fixture.State.ActiveDispatchCount == 0);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(fixture.State.QueuePollingHealthy);
        Assert.Contains(
            fixture.QueueLogger.Messages,
            message =>
                message.Contains("Dispatch task for lease", StringComparison.Ordinal) &&
                message.Contains("faulted", StringComparison.Ordinal));
        Assert.DoesNotContain(
            fixture.QueueLogger.Messages,
            message => message.Contains(
                "queue loop failed unexpectedly",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task LostLeaseCancelsHandlerAndStaleWorkDoesNotComplete()
    {
        using var fixture = await RegisteredFixtureAsync();
        fixture.Options.LeaseRenewalIntervalSeconds = 1;
        AddJobs(fixture, 1);
        var handler = new BlockingHandler(JobWorkKind.DryRun, expectedStarts: 1);
        var worker = fixture.QueueService(new SystemWorkerDelay(), handler);

        await worker.StartAsync(CancellationToken.None);
        await handler.ExpectedStartsReached.WaitAsync(TimeSpan.FromSeconds(1));
        var work = Assert.IsType<ClaimedJobWork>(handler.LastWork);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));
        fixture.Jobs.Jobs[work.JobId].ReleaseUnstartedWorkLease(
            work.Credentials,
            new UserIdentity("worker:test"),
            fixture.Clock.UtcNow);

        await handler.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        Assert.Null(fixture.Jobs.Jobs[work.JobId].Lease);
        Assert.Contains(
            fixture.QueueLogger.Messages,
            message => message.Contains("was lost", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LongDispatchRenewsWithSameLeaseAndFencingToken()
    {
        using var fixture = await RegisteredFixtureAsync();
        fixture.Options.LeaseRenewalIntervalSeconds = 1;
        AddJobs(fixture, 1);
        var handler = new RenewingCompletingHandler(fixture);
        var worker = fixture.QueueService(
            new ScaledAdvancingDelay(fixture.Clock),
            handler);

        await worker.StartAsync(CancellationToken.None);
        var work = await handler.Started.WaitAsync(TimeSpan.FromSeconds(1));
        var lease = Assert.IsType<JobLease>(fixture.Jobs.Jobs[work.JobId].Lease);
        var acquiredUtc = lease.AcquiredUtc;
        var fencingToken = lease.FencingToken;
        await WaitUntilAsync(() =>
            fixture.Jobs.Jobs[work.JobId].Lease!.LastRenewedUtc > acquiredUtc);
        handler.Complete();
        await handler.Finished.WaitAsync(TimeSpan.FromSeconds(1));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(fencingToken, work.FencingToken);
        Assert.Equal(JobStatus.DryRunCompleted, fixture.Jobs.Jobs[work.JobId].Status);
        Assert.Null(fixture.Jobs.Jobs[work.JobId].Lease);
    }

    [Fact]
    public async Task PersistenceRenewalRetriesSkipTheNextScheduledInterval()
    {
        using var fixture = await RegisteredFixtureAsync();
        fixture.Options.MaxConcurrentJobs = 2;
        fixture.Options.LeaseRenewalIntervalSeconds = 2;
        fixture.Candidates.BlockStartingAtCall = 2;
        AddJobs(fixture, 1);
        var handler = new BlockingHandler(JobWorkKind.DryRun, expectedStarts: 1);
        var delay = new RenewalRetryTrackingDelay(
            fixture.Clock,
            TimeSpan.FromSeconds(fixture.Options.LeaseRenewalIntervalSeconds));
        var worker = fixture.QueueService(delay, handler);

        await worker.StartAsync(CancellationToken.None);
        await handler.ExpectedStartsReached.WaitAsync(TimeSpan.FromSeconds(1));
        await delay.ScheduledRenewalReached.WaitAsync(TimeSpan.FromSeconds(1));
        await fixture.Candidates.Blocked.WaitAsync(TimeSpan.FromSeconds(1));
        fixture.UnitOfWork.AlwaysFail = true;
        delay.ReleaseScheduledRenewal();

        await fixture.UnitOfWork.WaitForFailuresAsync(2);

        Assert.Equal(1, delay.ScheduledRenewalCount);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ShutdownSignalsCancellationAndWaitsForDrainCompletion()
    {
        using var fixture = await RegisteredFixtureAsync();
        fixture.Options.DrainTimeoutSeconds = 5;
        AddJobs(fixture, 1);
        var handler = new DrainGateHandler();
        var worker = fixture.QueueService(new SystemWorkerDelay(), handler);
        await worker.StartAsync(CancellationToken.None);
        await handler.Started.WaitAsync(TimeSpan.FromSeconds(1));

        var stop = worker.StopAsync(CancellationToken.None);
        await handler.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(stop.IsCompleted);
        handler.Release();
        await stop.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, fixture.State.ActiveDispatchCount);
        Assert.Contains(
            fixture.QueueLogger.Messages,
            message => message.Contains(
                "drain completed",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShutdownWaitsForConfiguredDrainTimeoutBeforeLeavingLeaseForRecovery()
    {
        using var fixture = await RegisteredFixtureAsync();
        fixture.Options.DrainTimeoutSeconds = 1;
        AddJobs(fixture, 1);
        var handler = new DrainGateHandler();
        var worker = fixture.QueueService(new SystemWorkerDelay(), handler);
        await worker.StartAsync(CancellationToken.None);
        await handler.Started.WaitAsync(TimeSpan.FromSeconds(1));

        var started = System.Diagnostics.Stopwatch.StartNew();
        await worker.StopAsync(CancellationToken.None);
        started.Stop();

        Assert.True(started.Elapsed >= TimeSpan.FromMilliseconds(900));
        Assert.NotNull(fixture.Jobs.Jobs.Values.Single().Lease);
        Assert.Contains(
            fixture.QueueLogger.Messages,
            message => message.Contains(
                "drain timed out",
                StringComparison.Ordinal));
        handler.Release();
        await WaitUntilAsync(() => fixture.Jobs.Jobs.Values.Single().Lease is null);
        await Task.Delay(50);
    }

    private static async Task<WorkerServiceFixture> RegisteredFixtureAsync()
    {
        var fixture = new WorkerServiceFixture();
        await fixture.RegistrationService().StartAsync(CancellationToken.None);
        return fixture;
    }

    private static void AddJobs(WorkerServiceFixture fixture, int count)
    {
        foreach (var offset in Enumerable.Range(0, count))
        {
            var job = WorkerTestSupport.DryRunQueuedJob(
                fixture.Clock.UtcNow.AddMinutes(-10).AddSeconds(offset));
            Assert.True(fixture.Jobs.Jobs.TryAdd(job.Id, job));
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class ReturningHandler(JobWorkKind kind) : IJobWorkHandler
    {
        public IReadOnlySet<JobWorkRoute> SupportedRoutes { get; } =
            WorkerTestSupport.Route(kind);
        public Task HandleAsync(ClaimedJobWork work, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class SignalingReturningHandler(JobWorkKind kind) : IJobWorkHandler
    {
        private readonly TaskCompletionSource _returned =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlySet<JobWorkRoute> SupportedRoutes { get; } =
            WorkerTestSupport.Route(kind);
        public Task Returned => _returned.Task;

        public Task HandleAsync(ClaimedJobWork work, CancellationToken cancellationToken)
        {
            _returned.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingHandler(
        JobWorkKind kind,
        int expectedStarts) : IJobWorkHandler
    {
        private readonly TaskCompletionSource _expected =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancelled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _startCount;

        public IReadOnlySet<JobWorkRoute> SupportedRoutes { get; } =
            WorkerTestSupport.Route(kind);
        public int StartCount => Volatile.Read(ref _startCount);
        public Task ExpectedStartsReached => _expected.Task;
        public Task CancellationObserved => _cancelled.Task;
        public ClaimedJobWork? LastWork { get; private set; }

        public async Task HandleAsync(
            ClaimedJobWork work,
            CancellationToken cancellationToken)
        {
            LastWork = work;
            if (Interlocked.Increment(ref _startCount) >= expectedStarts)
            {
                _expected.TrySetResult();
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                _cancelled.TrySetResult();
                throw;
            }
        }
    }

    private sealed class CompletingDryRunHandler(
        WorkerServiceFixture fixture,
        int expectedCompletions = 1) : IJobWorkHandler
    {
        private readonly TaskCompletionSource _completed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        public IReadOnlySet<JobWorkRoute> SupportedRoutes =>
            WorkerTestSupport.Route(JobWorkKind.DryRun);
        public int Count => Volatile.Read(ref _count);
        public Task Completed => _completed.Task;

        public Task HandleAsync(
            ClaimedJobWork work,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actor = new UserIdentity($"worker:{work.WorkerNodeId}");
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));
            var job = fixture.Jobs.Jobs[work.JobId];
            job.StartDryRun(work.Credentials, actor, fixture.Clock.UtcNow);
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));
            job.CompleteDryRun(work.Credentials, actor, fixture.Clock.UtcNow);
            if (Interlocked.Increment(ref _count) >= expectedCompletions)
            {
                _completed.TrySetResult();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IJobWorkHandler
    {
        private readonly TaskCompletionSource _invoked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlySet<JobWorkRoute> SupportedRoutes =>
            WorkerTestSupport.Route(JobWorkKind.DryRun);
        public Task Invoked => _invoked.Task;

        public Task HandleAsync(ClaimedJobWork work, CancellationToken cancellationToken)
        {
            _invoked.TrySetResult();
            throw new InvalidOperationException("Deterministic fake handler failure.");
        }
    }

    private sealed class RenewalFaultAwaitingHandler(FaultingRenewalDelay delay) :
        IJobWorkHandler
    {
        public IReadOnlySet<JobWorkRoute> SupportedRoutes =>
            WorkerTestSupport.Route(JobWorkKind.DryRun);

        public async Task HandleAsync(
            ClaimedJobWork work,
            CancellationToken cancellationToken)
        {
            await delay.FaultInjected.WaitAsync(cancellationToken);
        }
    }

    private sealed class RenewingCompletingHandler(WorkerServiceFixture fixture) :
        IJobWorkHandler
    {
        private readonly TaskCompletionSource<ClaimedJobWork> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _complete =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _finished =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlySet<JobWorkRoute> SupportedRoutes =>
            WorkerTestSupport.Route(JobWorkKind.DryRun);
        public Task<ClaimedJobWork> Started => _started.Task;
        public Task Finished => _finished.Task;
        public void Complete() => _complete.TrySetResult();

        public async Task HandleAsync(
            ClaimedJobWork work,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult(work);
            await _complete.Task.WaitAsync(cancellationToken);
            var actor = new UserIdentity($"worker:{work.WorkerNodeId}");
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));
            var job = fixture.Jobs.Jobs[work.JobId];
            job.StartDryRun(work.Credentials, actor, fixture.Clock.UtcNow);
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));
            job.CompleteDryRun(work.Credentials, actor, fixture.Clock.UtcNow);
            _finished.TrySetResult();
        }
    }

    private sealed class DrainGateHandler : IJobWorkHandler
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancelled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlySet<JobWorkRoute> SupportedRoutes =>
            WorkerTestSupport.Route(JobWorkKind.DryRun);
        public Task Started => _started.Task;
        public Task CancellationObserved => _cancelled.Task;
        public void Release() => _release.TrySetResult();

        public async Task HandleAsync(
            ClaimedJobWork work,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            using var registration = cancellationToken.Register(
                () => _cancelled.TrySetResult());
            await _release.Task;
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}

internal sealed class FaultingRenewalDelay : IWorkerDelay
{
    private readonly TaskCompletionSource _faultInjected =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task FaultInjected => _faultInjected.Task;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay == TimeSpan.FromSeconds(1))
        {
            _faultInjected.TrySetResult();
            return Task.FromException(
                new InvalidOperationException("Deterministic renewal delay failure."));
        }

        return Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
    }
}

internal sealed class RenewalRetryTrackingDelay(
    MutableClock clock,
    TimeSpan renewalInterval) : IWorkerDelay
{
    private readonly TaskCompletionSource _scheduledRenewalReached =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseScheduledRenewal =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _scheduledRenewalCount;
    private int _retryDelayCount;

    public int ScheduledRenewalCount => Volatile.Read(ref _scheduledRenewalCount);
    public Task ScheduledRenewalReached => _scheduledRenewalReached.Task;
    public void ReleaseScheduledRenewal() => _releaseScheduledRenewal.TrySetResult();

    public async Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (delay == renewalInterval)
        {
            Interlocked.Increment(ref _scheduledRenewalCount);
            _scheduledRenewalReached.TrySetResult();
            await _releaseScheduledRenewal.Task.WaitAsync(cancellationToken);
            clock.Advance(delay);
            return;
        }

        clock.Advance(delay);
        if (Interlocked.Increment(ref _retryDelayCount) >= 2)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}

internal sealed class BlockingSignalDelay : IWorkerDelay
{
    private readonly TaskCompletionSource _called =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Called => _called.Task;

    public async Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        _called.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}

internal sealed class FastRecordingDelay(int completionsBeforeBlocking) : IWorkerDelay
{
    private readonly TaskCompletionSource _blocked =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calls;
    public List<TimeSpan> Delays { get; } = [];
    public Task Blocked => _blocked.Task;

    public async Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        lock (Delays)
        {
            Delays.Add(delay);
        }

        if (Interlocked.Increment(ref _calls) <= completionsBeforeBlocking)
        {
            await Task.Yield();
            return;
        }

        _blocked.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}

internal sealed class ScaledAdvancingDelay(MutableClock clock) : IWorkerDelay
{
    public async Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        clock.Advance(delay);
        await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
    }
}
