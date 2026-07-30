using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsScriptRunner.Application;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Application.Workers;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Domain.Workers;
using WindowsScriptRunner.Infrastructure.Persistence;
using WindowsScriptRunner.Worker;

namespace WindowsScriptRunner.WorkerTests;

public sealed class WorkerFoundationTests
{
    [Fact]
    public void RecommendedDefaultsAreStableAndProductionIdentityIsRequired()
    {
        var options = new WorkerOptions();

        Assert.Equal(30, options.HeartbeatIntervalSeconds);
        Assert.Equal(1000, options.QueuePollingIntervalMilliseconds);
        Assert.Equal(120, options.LeaseDurationSeconds);
        Assert.Equal(30, options.LeaseRenewalIntervalSeconds);
        Assert.Equal(30, options.LeaseRecoveryIntervalSeconds);
        Assert.Equal(60, options.DrainTimeoutSeconds);
        Assert.Equal(1, options.MaxConcurrentJobs);
        Assert.Equal(10, options.ClaimCandidateBatchSize);
        Assert.True(options.QueueProcessingEnabled);
        Assert.False(options.AllowEphemeralNodeId);
        Assert.False(Validator(Environments.Production).Validate(null, options).Succeeded);
    }

    [Fact]
    public void ValidOptionsAndExplicitEphemeralDevelopmentIdentityAreAccepted()
    {
        var valid = WorkerTestSupport.Options();
        Assert.True(Validator(Environments.Production).Validate(null, valid).Succeeded);

        valid.NodeId = Guid.Empty;
        valid.AllowEphemeralNodeId = true;
        Assert.True(Validator(Environments.Development).Validate(null, valid).Succeeded);
        var first = new WorkerIdentity(Options.Create(valid));
        var second = new WorkerIdentity(Options.Create(valid));
        Assert.NotEqual(first.NodeId, second.NodeId);
    }

    [Fact]
    public void EphemeralIdentityIsRejectedOutsideDevelopment()
    {
        var options = WorkerTestSupport.Options();
        options.NodeId = Guid.Empty;
        options.AllowEphemeralNodeId = true;

        var result = Validator(Environments.Production).Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failures);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("Development", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, 30, 90, 1, 10)]
    [InlineData(30, 60, 90, 1, 10)]
    [InlineData(30, 30, 60, 1, 10)]
    [InlineData(30, 30, 90, 0, 10)]
    [InlineData(30, 30, 90, 33, 10)]
    [InlineData(30, 30, 90, 1, 0)]
    [InlineData(30, 30, 90, 1, 101)]
    public void UnsafeTimingAndBoundsAreRejected(
        int heartbeat,
        int renewal,
        int stale,
        int concurrency,
        int batch)
    {
        var options = WorkerTestSupport.Options();
        options.HeartbeatIntervalSeconds = heartbeat;
        options.LeaseRenewalIntervalSeconds = renewal;
        options.WorkerStaleAfterSeconds = stale;
        options.MaxConcurrentJobs = concurrency;
        options.ClaimCandidateBatchSize = batch;

        Assert.False(Validator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void CapabilityNamesMustBeUniqueCaseInsensitively()
    {
        var options = WorkerTestSupport.Options();
        options.Capabilities =
        [
            new WorkerCapabilityOptions { Name = "OS", Value = "Windows" },
            new WorkerCapabilityOptions { Name = "os", Value = "Server" },
        ];

        Assert.False(Validator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void MissingCapabilityNameReturnsValidationFailure()
    {
        var options = WorkerTestSupport.Options();
        options.Capabilities =
        [
            new WorkerCapabilityOptions { Name = null!, Value = "Windows" },
        ];

        var result = Validator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failures);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("Capabilities[0]", StringComparison.Ordinal));
    }

    [Fact]
    public void BackoffMaximumsMustCoverTheInitialPollingDelay()
    {
        var options = WorkerTestSupport.Options();
        options.QueuePollingIntervalMilliseconds = 60000;
        options.EmptyQueueBackoffMaximumSeconds = 30;
        options.PersistenceFailureBackoffMaximumSeconds = 30;

        Assert.False(Validator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void HandlerRegistryRejectsDuplicatesAndExposesOnlyRegisteredKinds()
    {
        var dryRun = new ReturningHandler(JobWorkKind.DryRun);
        var registry = new JobWorkHandlerRegistry([dryRun]);

        Assert.Equal([JobWorkKind.DryRun], registry.SupportedWorkKinds);
        Assert.Same(dryRun, registry.GetRequired(JobWorkKind.DryRun));
        Assert.Throws<InvalidOperationException>(
            () => new JobWorkHandlerRegistry(
                [dryRun, new ReturningHandler(JobWorkKind.DryRun)]));
        Assert.Throws<InvalidOperationException>(
            () => registry.GetRequired(JobWorkKind.Execute));
    }

    [Fact]
    public void ProductionWorkerRegistrationContainsNoExecutableWorkHandler()
    {
        var services = new ServiceCollection();
        services.AddWorkerServices(new ConfigurationBuilder().Build());

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IJobWorkHandler));
    }

    private static WorkerOptionsValidator Validator(
        string environmentName = "Production") =>
        new(new TestHostEnvironment(environmentName));

    [Fact]
    public void BackoffIncreasesResetsAndKeepsJitterBounded()
    {
        var backoff = new WorkerBackoff(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(8),
            new SequenceRandom(0, 1, 0.5));

        Assert.InRange(
            backoff.Next(),
            TimeSpan.FromMilliseconds(900),
            TimeSpan.FromMilliseconds(1100));
        Assert.InRange(
            backoff.Next(),
            TimeSpan.FromMilliseconds(1800),
            TimeSpan.FromMilliseconds(2200));
        Assert.InRange(
            backoff.Next(),
            TimeSpan.FromMilliseconds(3600),
            TimeSpan.FromMilliseconds(4400));
        backoff.Reset();
        Assert.InRange(
            backoff.Next(),
            TimeSpan.FromMilliseconds(900),
            TimeSpan.FromMilliseconds(1100));
        Assert.InRange(
            new WorkerBackoff(
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(8),
                new SequenceRandom(1))
                .Next(),
            TimeSpan.FromSeconds(7.2),
            TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task RegistrationCreatesWorkerSynchronizesCapabilitiesAndCommitsOnce()
    {
        var fixture = new WorkerServiceFixture();
        fixture.Options.Capabilities =
        [
            new WorkerCapabilityOptions { Name = "OS", Value = "Windows" },
        ];
        var service = fixture.RegistrationService();

        await service.StartAsync(CancellationToken.None);

        Assert.True(fixture.State.Registered);
        Assert.True(fixture.State.HeartbeatHealthy);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
        Assert.Equal(
            ["OS|Windows"],
            fixture.Workers.Worker!.Capabilities.Select(
                capability => $"{capability.Name}|{capability.Value}"));
        Assert.Single(fixture.Audits.Events);
        Assert.Equal("WorkerRegistered", fixture.Audits.Events[0].EventType);
    }

    [Fact]
    public async Task RegistrationRejectsDisabledPersistedIdentityWithoutCommit()
    {
        var fixture = new WorkerServiceFixture();
        var worker = new WorkerNode(
            fixture.Identity.NodeId,
            fixture.Identity.Name,
            fixture.Clock.UtcNow.AddMinutes(-1));
        worker.Disable();
        fixture.Workers.Worker = worker;

        await Assert.ThrowsAsync<Application.Exceptions.ApplicationValidationException>(
            () => fixture.RegistrationService().StartAsync(CancellationToken.None));
        Assert.False(fixture.State.Registered);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task HeartbeatUsesFreshScopePersistsWithoutAuditAndStopsCleanly()
    {
        var fixture = new WorkerServiceFixture();
        await fixture.RegistrationService().StartAsync(CancellationToken.None);
        var initialAuditCount = fixture.Audits.Events.Count;
        var delay = new AdvancingDelay(fixture.Clock, maximumCompletions: 1);
        var service = fixture.HeartbeatService(delay);

        await service.StartAsync(CancellationToken.None);
        await fixture.UnitOfWork.WaitForCommitsAsync(2);
        await service.StopAsync(CancellationToken.None);

        Assert.True(fixture.State.HeartbeatHealthy);
        Assert.Equal(initialAuditCount, fixture.Audits.Events.Count);
        Assert.True(fixture.Workers.Worker!.LastHeartbeatUtc > fixture.Clock.InitialUtc);
        Assert.True(fixture.ScopeFactory.ScopeCount >= 2);
    }

    [Fact]
    public async Task TransientHeartbeatFailureImmediatelyPausesLivenessAndThenRecovers()
    {
        var fixture = new WorkerServiceFixture();
        await fixture.RegistrationService().StartAsync(CancellationToken.None);
        fixture.UnitOfWork.FailNextCommit = true;
        var delay = new GatedAdvancingDelay(fixture.Clock);
        var service = fixture.HeartbeatService(delay);

        await service.StartAsync(CancellationToken.None);
        await fixture.UnitOfWork.WaitForFailuresAsync(1);
        await delay.Paused.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(fixture.State.HeartbeatHealthy);
        delay.Resume();
        await fixture.UnitOfWork.WaitForCommitsAsync(2);
        await service.StopAsync(CancellationToken.None);

        Assert.True(fixture.State.HeartbeatHealthy);
        Assert.Contains(
            fixture.HeartbeatLogger.Levels,
            level => level == LogLevel.Warning);
    }

    [Fact]
    public async Task ProlongedHeartbeatFailureFailsServiceAndIsNotLoggedAsClean()
    {
        var fixture = new WorkerServiceFixture();
        await fixture.RegistrationService().StartAsync(CancellationToken.None);
        fixture.UnitOfWork.AlwaysFail = true;
        var delay = new AdvancingDelay(fixture.Clock, maximumCompletions: 20);
        var service = fixture.HeartbeatService(delay);

        await service.StartAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteTask!);

        Assert.Equal(
            TimeSpan.FromSeconds(fixture.Options.WorkerStaleAfterSeconds),
            delay.CompletedDuration);
        Assert.False(fixture.State.HeartbeatHealthy);
        Assert.Contains(fixture.HeartbeatLogger.Levels, level => level == LogLevel.Error);
        Assert.DoesNotContain(
            fixture.HeartbeatLogger.Messages,
            message => message.Contains("stopped cleanly", StringComparison.Ordinal));
    }

    private sealed class ReturningHandler(JobWorkKind workKind) : IJobWorkHandler
    {
        public JobWorkKind WorkKind { get; } = workKind;
        public Task HandleAsync(ClaimedJobWork work, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}

internal sealed class WorkerServiceFixture : IDisposable
{
    private readonly ServiceProvider _provider;

    public WorkerServiceFixture()
    {
        Options = WorkerTestSupport.Options();
        Clock = new MutableClock(new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero));
        Workers = new FakeWorkerRepository();
        Jobs = new FakeJobRepository();
        Audits = new FakeAuditWriter();
        UnitOfWork = new FakeUnitOfWork();
        Candidates = new FakeCandidateSource(Jobs);
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<IWorkerCoordinationClock>(Clock);
        services.AddSingleton<IWorkerNodeRepository>(Workers);
        services.AddSingleton<IJobRepository>(Jobs);
        services.AddSingleton<IAuditWriter>(Audits);
        services.AddSingleton<IUnitOfWork>(UnitOfWork);
        services.AddSingleton<IFencingTokenSource, FakeFencingTokenSource>();
        services.AddSingleton<IJobQueueCandidateSource>(Candidates);
        _provider = services.BuildServiceProvider();
        ScopeFactory = new CountingScopeFactory(
            _provider.GetRequiredService<IServiceScopeFactory>());
        Identity = new WorkerIdentity(Microsoft.Extensions.Options.Options.Create(Options));
        State = new WorkerRuntimeState();
        Metrics = new WorkerMetrics(State);
    }

    public WorkerOptions Options { get; }
    public MutableClock Clock { get; }
    public FakeWorkerRepository Workers { get; }
    public FakeJobRepository Jobs { get; }
    public FakeAuditWriter Audits { get; }
    public FakeUnitOfWork UnitOfWork { get; }
    public FakeCandidateSource Candidates { get; }
    public CountingScopeFactory ScopeFactory { get; }
    public WorkerIdentity Identity { get; }
    public WorkerRuntimeState State { get; }
    public WorkerMetrics Metrics { get; }
    public RecordingLogger<WorkerHeartbeatService> HeartbeatLogger { get; } = new();
    public RecordingLogger<JobQueueWorker> QueueLogger { get; } = new();

    public WorkerRegistrationHostedService RegistrationService() =>
        new(
            ScopeFactory,
            Identity,
            State,
            Microsoft.Extensions.Options.Options.Create(Options),
            new RecordingLogger<WorkerRegistrationHostedService>());

    public WorkerHeartbeatService HeartbeatService(IWorkerDelay delay) =>
        new(
            ScopeFactory,
            Identity,
            State,
            Metrics,
            delay,
            new SequenceRandom(0.5),
            Clock,
            Microsoft.Extensions.Options.Options.Create(Options),
            HeartbeatLogger);

    public JobQueueWorker QueueService(
        IWorkerDelay delay,
        params IJobWorkHandler[] handlers) =>
        new(
            ScopeFactory,
            new JobWorkHandlerRegistry(handlers),
            Identity,
            State,
            Metrics,
            delay,
            new SequenceRandom(0.5),
            Clock,
            Microsoft.Extensions.Options.Options.Create(Options),
            QueueLogger);

    public void Dispose()
    {
        Metrics.Dispose();
        _provider.Dispose();
    }
}

internal static class WorkerTestSupport
{
    internal static WorkerOptions Options() =>
        new()
        {
            NodeId = Guid.Parse("46ca2650-e59f-49b9-8b87-092f33d2de8e"),
            Name = "worker-tests",
            HeartbeatIntervalSeconds = 30,
            WorkerStaleAfterSeconds = 90,
            QueuePollingIntervalMilliseconds = 50,
            EmptyQueueBackoffMaximumSeconds = 2,
            PersistenceFailureBackoffMaximumSeconds = 30,
            LeaseDurationSeconds = 10,
            LeaseRenewalIntervalSeconds = 2,
            LeaseRecoveryIntervalSeconds = 30,
            DrainTimeoutSeconds = 1,
            MaxConcurrentJobs = 1,
            ClaimCandidateBatchSize = 10,
        };

    internal static Job DryRunQueuedJob(DateTimeOffset createdUtc)
    {
        var user = new UserIdentity("DOMAIN\\requester");
        var worker = new UserIdentity("worker:test");
        var version = new ScriptVersion(
            ScriptVersionId.New(),
            ScriptVersionNumber.Parse("1.0.0"),
            "scripts/Test.ps1",
            new string('a', 64),
            "abcdef1",
            "7.4",
            30,
            [ExecutionPhase.DryRun],
            [ReportFormat.Json],
            createdUtc,
            user);
        version.Publish();
        var script = ScriptDefinition.Create(
            ScriptDefinitionId.New(),
            new ScriptName($"test.{Guid.NewGuid():N}"),
            "Test Script",
            "Test description",
            RiskLevel.Low,
            user,
            createdUtc);
        script.AddVersion(version, createdUtc);
        var job = Job.CreateDraft(
            JobId.New(),
            script.Id,
            version.Id,
            ExecutionPhase.DryRun,
            user,
            createdUtc);
        job.AddTarget(new TargetName("server-01"), user, createdUtc.AddMinutes(1));
        job.Submit(script, user, createdUtc.AddMinutes(2));
        job.MarkValidated(worker, createdUtc.AddMinutes(3));
        job.QueueDryRun(worker, createdUtc.AddMinutes(4));
        return job;
    }
}

internal sealed class MutableClock(DateTimeOffset utcNow) :
    IClock,
    IWorkerCoordinationClock
{
    private readonly object _sync = new();
    private DateTimeOffset _utcNow = utcNow;

    public DateTimeOffset InitialUtc { get; } = utcNow;
    public DateTimeOffset UtcNow { get { lock (_sync) { return _utcNow; } } }

    public Task<DateTimeOffset> GetUtcNowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(UtcNow);
    }

    public void Advance(TimeSpan amount)
    {
        lock (_sync)
        {
            _utcNow += amount;
        }
    }
}

internal sealed class AdvancingDelay(MutableClock clock, int maximumCompletions) : IWorkerDelay
{
    private int _completionCount;
    private long _completedTicks;
    public TimeSpan CompletedDuration =>
        TimeSpan.FromTicks(Interlocked.Read(ref _completedTicks));

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref _completionCount) <= maximumCompletions)
        {
            Interlocked.Add(ref _completedTicks, delay.Ticks);
            clock.Advance(delay);
            return Task.CompletedTask;
        }

        return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}

internal sealed class GatedAdvancingDelay(MutableClock clock) : IWorkerDelay
{
    private readonly TaskCompletionSource _paused =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _resume =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _callCount;

    public Task Paused => _paused.Task;
    public void Resume() => _resume.TrySetResult();

    public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _callCount);
        if (call == 2)
        {
            _paused.TrySetResult();
            await _resume.Task.WaitAsync(cancellationToken);
            clock.Advance(delay);
            return;
        }

        if (call <= 3)
        {
            clock.Advance(delay);
            return;
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}

internal sealed class SequenceRandom(params double[] values) : IWorkerRandom
{
    private int _index;
    public double NextDouble() =>
        values[Math.Min(Interlocked.Increment(ref _index) - 1, values.Length - 1)];
}

internal sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
{
    private int _scopeCount;
    public int ScopeCount => Volatile.Read(ref _scopeCount);

    public IServiceScope CreateScope()
    {
        Interlocked.Increment(ref _scopeCount);
        return inner.CreateScope();
    }
}

internal sealed class FakeWorkerRepository : IWorkerNodeRepository
{
    public WorkerNode? Worker { get; set; }

    public Task<WorkerNode?> GetByIdAsync(
        WorkerNodeId id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Worker?.Id == id ? Worker : null);
    }

    public Task AddAsync(WorkerNode workerNode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Worker = workerNode;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(WorkerNode workerNode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Worker = workerNode;
        return Task.CompletedTask;
    }
}

internal sealed class FakeJobRepository : IJobRepository
{
    public ConcurrentDictionary<JobId, Job> Jobs { get; } = new();

    public Task<Job?> GetByIdAsync(JobId id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Jobs.GetValueOrDefault(id));
    }

    public Task<bool> ExistsAsync(JobId id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Jobs.ContainsKey(id));
    }

    public Task AddAsync(Job job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Jobs.TryAdd(job.Id, job))
        {
            throw new InvalidOperationException($"Job {job.Id} already exists.");
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(Job job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Jobs[job.Id] = job;
        return Task.CompletedTask;
    }
}

internal sealed class FakeAuditWriter : IAuditWriter
{
    public List<AuditEvent> Events { get; } = [];

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Events)
        {
            Events.Add(auditEvent);
        }

        return Task.CompletedTask;
    }
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    private int _commitCount;
    private int _failureCount;
    public int CommitCount => Volatile.Read(ref _commitCount);
    public int FailureCount => Volatile.Read(ref _failureCount);
    public bool FailNextCommit { get; set; }
    public bool AlwaysFail { get; set; }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (AlwaysFail || FailNextCommit)
        {
            FailNextCommit = false;
            Interlocked.Increment(ref _failureCount);
            throw new PersistenceUnavailableException(
                "Test persistence unavailable.",
                new TimeoutException());
        }

        Interlocked.Increment(ref _commitCount);
        return Task.CompletedTask;
    }

    public async Task WaitForCommitsAsync(int count)
    {
        await WaitUntilAsync(() => CommitCount >= count);
    }

    public async Task WaitForFailuresAsync(int count)
    {
        await WaitUntilAsync(() => FailureCount >= count);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }
}

internal sealed class FakeFencingTokenSource : IFencingTokenSource
{
    private long _next;
    public Task<long> GetNextAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Interlocked.Increment(ref _next));
}

internal sealed class FakeCandidateSource(FakeJobRepository jobs) : IJobQueueCandidateSource
{
    private readonly object _sync = new();
    private readonly TaskCompletionSource _blocked =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _callCount;
    public int CallCount => Volatile.Read(ref _callCount);
    public int BlockStartingAtCall { get; set; } = int.MaxValue;
    public Task Blocked => _blocked.Task;
    public int FailuresRemaining { get; set; }
    public Queue<bool> ResponsePlan { get; } = new();
    public List<IReadOnlySet<JobWorkKind>> RequestedKinds { get; } = [];

    public Task<IReadOnlyList<JobQueueCandidate>> FindCandidatesAsync(
        IReadOnlySet<JobWorkKind> supportedWorkKinds,
        int maximumCount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var call = Interlocked.Increment(ref _callCount);
        if (call >= BlockStartingAtCall)
        {
            return BlockAsync(cancellationToken);
        }

        lock (_sync)
        {
            RequestedKinds.Add(supportedWorkKinds.ToHashSet());
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new PersistenceUnavailableException(
                    "Test candidate persistence unavailable.",
                    new TimeoutException());
            }

            if (ResponsePlan.Count > 0 && !ResponsePlan.Dequeue())
            {
                return Task.FromResult<IReadOnlyList<JobQueueCandidate>>([]);
            }

            var candidates = jobs.Jobs.Values
                .ToArray()
                .Where(job =>
                    job.Lease is null &&
                    job.Status == JobStatus.DryRunQueued &&
                    supportedWorkKinds.Contains(JobWorkKind.DryRun))
                .OrderBy(job => job.UpdatedUtc)
                .ThenBy(job => job.CreatedUtc)
                .ThenBy(job => job.Id.Value)
                .Take(maximumCount)
                .Select(job => new JobQueueCandidate(
                    job.Id,
                    JobWorkKind.DryRun,
                    job.CreatedUtc,
                    job.UpdatedUtc))
                .ToArray();
            return Task.FromResult<IReadOnlyList<JobQueueCandidate>>(candidates);
        }
    }

    private async Task<IReadOnlyList<JobQueueCandidate>> BlockAsync(
        CancellationToken cancellationToken)
    {
        _blocked.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return [];
    }
}

internal sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "WindowsScriptRunner.WorkerTests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];
    public List<LogLevel> Levels { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (Messages)
        {
            Levels.Add(logLevel);
            Messages.Add(formatter(state, exception));
        }
    }
}
