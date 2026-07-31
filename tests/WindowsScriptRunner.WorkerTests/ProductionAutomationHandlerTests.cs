using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WindowsScriptRunner.Application;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Automation;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Reports;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.PowerShell;
using WindowsScriptRunner.Reporting;

namespace WindowsScriptRunner.WorkerTests;

public sealed class ProductionAutomationHandlerTests
{
    [Fact]
    public void PackageIsDisabledByDefaultAndRegistersOnePinnedRouteWhenEnabled()
    {
        var disabled = new ServiceCollection();
        disabled.AddProductionAutomation(
            new ConfigurationBuilder().AddInMemoryCollection().Build());
        using var disabledProvider = disabled.BuildServiceProvider();
        Assert.Empty(disabledProvider.GetServices<IJobWorkHandler>());
        Assert.Null(disabledProvider.GetService<IPowerShellExecutionBoundary>());

        using var fixture = new AutomationHandlerFixture(
            SuccessfulResult());
        var handler = Assert.Single(
            fixture.Provider.GetServices<IJobWorkHandler>());
        Assert.Equal(
            LocalHostInventoryPackageMetadata.SupportedRoutes,
            handler.SupportedRoutes);

        var incompatibleConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Automation:LocalHostInventory:Enabled"] = "true",
                    ["PowerShellExecution:MinimumVersion"] = "7.3.0",
                })
            .Build();
        Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection()
                .AddProductionAutomation(incompatibleConfiguration));

        var insufficientTimeoutConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Automation:LocalHostInventory:Enabled"] = "true",
                    ["PowerShellExecution:MaximumTimeoutSeconds"] = "59",
                })
            .Build();
        Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection()
                .AddProductionAutomation(insufficientTimeoutConfiguration));
    }

    [Fact]
    public async Task SuccessfulReviewedExecutionCompletesAndResolvesLeaseWithoutPersistingOutput()
    {
        using var fixture = new AutomationHandlerFixture(
            SuccessfulResult(
                standardOutput: InventoryJson(
                    "inventory-value-that-must-not-be-persisted")));

        await fixture.Handler.HandleAsync(
            fixture.Work,
            CancellationToken.None);

        Assert.Equal(JobStatus.Completed, fixture.Job.Status);
        Assert.Null(fixture.Job.Lease);
        Assert.Equal(1, fixture.Boundary.CallCount);
        Assert.Equal(fixture.Job.Id.Value, fixture.Boundary.LastRequest!.ExecutionId.Value);
        Assert.Empty(fixture.Boundary.LastRequest.Arguments);
        Assert.True(fixture.ScopeFactory.ScopeCount >= 3);
        var report = Assert.IsType<JobReport>(fixture.Reports.Report);
        Assert.Equal(
            "inventory-value-that-must-not-be-persisted",
            report.Inventory.ComputerName);
        Assert.DoesNotContain(
            fixture.Audits.Events.SelectMany(audit =>
                audit.Properties.Values.Append(audit.Summary)),
            value => value.Contains(
                "inventory-value-that-must-not-be-persisted",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("truncated")]
    [InlineData("stderr")]
    public async Task UntrustedSuccessfulOutputFailsWithoutAReport(string failure)
    {
        var result = failure switch
        {
            "malformed" => SuccessfulResult(standardOutput: "{}"),
            "truncated" => SuccessfulResult() with
            {
                StandardOutputTruncated = true,
            },
            "stderr" => SuccessfulResult() with
            {
                StandardError = "unexpected error output",
            },
            _ => throw new InvalidOperationException(),
        };
        using var fixture = new AutomationHandlerFixture(result);

        await fixture.Handler.HandleAsync(
            fixture.Work,
            CancellationToken.None);

        Assert.Equal(JobStatus.Failed, fixture.Job.Status);
        Assert.Null(fixture.Job.Lease);
        Assert.Null(fixture.Reports.Report);
        Assert.DoesNotContain(
            fixture.Audits.Events.SelectMany(audit =>
                audit.Properties.Values.Append(audit.Summary)),
            value => value.Contains(
                "unexpected error output",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(PowerShellTerminationReason.Exited, 9, JobStatus.Failed)]
    [InlineData(PowerShellTerminationReason.TimedOut, null, JobStatus.TimedOut)]
    [InlineData(
        PowerShellTerminationReason.OutputLimitExceeded,
        null,
        JobStatus.Failed)]
    public async Task StructuredFailureResultsResolveLease(
        PowerShellTerminationReason reason,
        int? exitCode,
        JobStatus expectedStatus)
    {
        using var fixture = new AutomationHandlerFixture(
            SuccessfulResult(reason, exitCode));

        await fixture.Handler.HandleAsync(
            fixture.Work,
            CancellationToken.None);

        Assert.Equal(expectedStatus, fixture.Job.Status);
        Assert.Null(fixture.Job.Lease);
        Assert.Equal(1, fixture.Boundary.CallCount);
    }

    [Fact]
    public async Task ModifiedArtifactFailsClosedBeforeBoundaryInvocation()
    {
        using var fixture = new AutomationHandlerFixture(SuccessfulResult());
        File.AppendAllText(fixture.ScriptPath, Environment.NewLine + "# modified");

        await fixture.Handler.HandleAsync(
            fixture.Work,
            CancellationToken.None);

        Assert.Equal(JobStatus.Blocked, fixture.Job.Status);
        Assert.Null(fixture.Job.Lease);
        Assert.Equal(0, fixture.Boundary.CallCount);
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("startup")]
    public async Task RuntimeDiscoveryAndStartupFailuresBecomeNotRun(string failure)
    {
        Exception exception = failure == "runtime"
            ? new PowerShellRuntimeNotFoundException("Runtime unavailable.")
            : new PowerShellProcessStartException(
                "Process did not start.",
                new InvalidOperationException());
        using var fixture = new AutomationHandlerFixture(exception);

        await fixture.Handler.HandleAsync(
            fixture.Work,
            CancellationToken.None);

        Assert.Equal(JobStatus.NotRun, fixture.Job.Status);
        Assert.Null(fixture.Job.Lease);
    }

    [Fact]
    public async Task CallerCancellationCancelsExecutionAndResolvesCurrentLease()
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = new AutomationHandlerFixture(
            async (_, token) =>
            {
                cancellation.Cancel();
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                return SuccessfulResult();
            });

        await fixture.Handler.HandleAsync(fixture.Work, cancellation.Token);

        Assert.Equal(JobStatus.Cancelled, fixture.Job.Status);
        Assert.Null(fixture.Job.Lease);
    }

    [Fact]
    public async Task LeaseLossDuringExecutionPreventsStaleTerminalMutation()
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = new AutomationHandlerFixture(
            async (_, token) =>
            {
                fixtureClock!.Advance(TimeSpan.FromMinutes(10));
                fixtureJob!.RecoverExpiredWorkLease(
                    fixtureWork!.Credentials,
                    new UserIdentity("system:lease-recovery"),
                    fixtureClock.UtcNow);
                cancellation.Cancel();
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                return SuccessfulResult();
            });
        fixtureClock = fixture.Clock;
        fixtureJob = fixture.Job;
        fixtureWork = fixture.Work;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Handler.HandleAsync(
                fixture.Work,
                cancellation.Token));

        Assert.Equal(JobStatus.TimedOut, fixture.Job.Status);
        Assert.Null(fixture.Job.Lease);
    }

    [Fact]
    public async Task UncertainCommitIsRecognizedAsAnExactReplay()
    {
        using var fixture = new AutomationHandlerFixture(
            SuccessfulResult(),
            failCommitNumber: 2);

        await fixture.Handler.HandleAsync(
            fixture.Work,
            CancellationToken.None);

        Assert.Equal(1, fixture.Boundary.CallCount);
        Assert.Equal(JobStatus.Completed, fixture.Job.Status);
        Assert.NotNull(fixture.Reports.Report);
    }

    private MutableClock? fixtureClock;
    private Job? fixtureJob;
    private ClaimedJobWork? fixtureWork;

    private static PowerShellExecutionResult SuccessfulResult(
        PowerShellTerminationReason reason = PowerShellTerminationReason.Exited,
        int? exitCode = 0,
        string? standardOutput = null)
    {
        var startedUtc = new DateTimeOffset(
            2026,
            7,
            30,
            12,
            0,
            0,
            TimeSpan.Zero);
        standardOutput ??= InventoryJson();
        return new(
            PowerShellExecutionId.New(),
            new PowerShellRuntimeInfo(
                "omitted",
                new Version(7, 4),
                "Core",
                "Win32NT",
                "Windows",
                "X64",
                false),
            startedUtc,
            startedUtc.AddSeconds(1),
            TimeSpan.FromSeconds(1),
            exitCode,
            standardOutput,
            string.Empty,
            standardOutput.Length,
            0,
            false,
            false,
            reason);
    }

    private static string InventoryJson(string computerName = "WORKER-01") =>
        $$"""
        {"schemaVersion":"1.0","computerName":"{{computerName}}","os":{"description":"Microsoft Windows 11","version":"10.0.26100","architecture":"X64"},"powerShell":{"version":"7.4.0"},"collectedUtc":"2026-07-30T12:00:00.5000000+00:00"}
        """;

    private sealed class AutomationHandlerFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "WindowsScriptRunner.Phase6.WorkerTests",
            Guid.NewGuid().ToString("N"));

        internal AutomationHandlerFixture(
            PowerShellExecutionResult result,
            int? failCommitNumber = null)
            : this((_, _) => Task.FromResult(result), failCommitNumber)
        {
        }

        internal AutomationHandlerFixture(
            Exception exception,
            int? failCommitNumber = null)
            : this((_, _) => Task.FromException<PowerShellExecutionResult>(exception), failCommitNumber)
        {
        }

        internal AutomationHandlerFixture(
            Func<PowerShellExecutionRequest, CancellationToken, Task<PowerShellExecutionResult>>
                execute,
            int? failCommitNumber = null)
        {
            var allowedRoot = Path.Combine(_root, "allowed");
            var workingRoot = Path.Combine(_root, "working");
            ScriptPath = Path.Combine(
                allowedRoot,
                LocalHostInventoryPackageMetadata.RelativeScriptPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(ScriptPath)!);
            Directory.CreateDirectory(workingRoot);
            File.Copy(SourceScriptPath(), ScriptPath);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Automation:LocalHostInventory:Enabled"] = "true",
                        ["Automation:LocalHostInventory:RegisterOnStartup"] = "false",
                        ["PowerShellExecution:AllowedScriptRoot"] = allowedRoot,
                        ["PowerShellExecution:WorkingRoot"] = workingRoot,
                        ["PowerShellExecution:MinimumVersion"] = "7.4.0",
                        ["PowerShellExecution:DefaultTimeoutSeconds"] = "60",
                        ["PowerShellExecution:MaximumTimeoutSeconds"] = "60",
                    })
                .Build();
            Clock = new MutableClock(
                new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero));
            Jobs = new FakeJobRepository();
            Scripts = new FakeScriptRepository();
            Audits = new FakeAuditWriter();
            Reports = new FakeReportRepository();
            Boundary = new RecordingBoundary(execute);
            var services = new ServiceCollection();
            services.AddApplication();
            services.AddProductionAutomation(configuration);
            services.AddSingleton<IJobRepository>(Jobs);
            services.AddSingleton<IScriptDefinitionRepository>(Scripts);
            services.AddSingleton<IAuditWriter>(Audits);
            services.AddSingleton<IJobReportRepository>(Reports);
            services.AddSingleton<IUnitOfWork>(
                new FailingCommitUnitOfWork(failCommitNumber));
            services.AddSingleton<IWorkerCoordinationClock>(Clock);
            services.AddSingleton<IPowerShellExecutionBoundary>(Boundary);
            Provider = services.BuildServiceProvider();

            var definition = LocalHostInventoryPackageMetadata.CreateDefinition(
                Clock.UtcNow);
            Scripts.Definition = definition;
            var version = Assert.Single(definition.Versions);
            var requester = new UserIdentity("DOMAIN\\requester");
            Job = Job.CreateDraft(
                JobId.New(),
                definition.Id,
                version.Id,
                ExecutionPhase.DryRun,
                requester,
                Clock.UtcNow);
            Job.AddTarget(
                new TargetName("local-worker"),
                requester,
                Clock.UtcNow);
            Job.Submit(definition, requester, Clock.UtcNow);
            Job.MarkValidated(requester, Clock.UtcNow);
            Job.QueueDryRun(requester, Clock.UtcNow);
            var workerId = WorkerNodeId.New();
            var lease = Job.AcquireWorkLease(
                JobLeaseId.New(),
                workerId,
                JobWorkKind.DryRun,
                7,
                new UserIdentity($"worker:{workerId}"),
                Clock.UtcNow,
                Clock.UtcNow.AddMinutes(5));
            Jobs.Jobs[Job.Id] = Job;
            Work = new ClaimedJobWork(
                Job.Id,
                JobWorkKind.DryRun,
                version.Id,
                lease.Id,
                workerId,
                lease.FencingToken,
                lease.ExpiresUtc);
            ScopeFactory = new CountingScopeFactory(
                Provider.GetRequiredService<IServiceScopeFactory>());
            Handler = new LocalHostInventoryJobWorkHandler(
                ScopeFactory,
                Provider.GetRequiredService<LocalHostInventoryArtifactCatalog>(),
                Boundary,
                Provider.GetRequiredService<LocalHostInventoryReportParser>());
        }

        internal ServiceProvider Provider { get; }
        internal MutableClock Clock { get; }
        internal FakeJobRepository Jobs { get; }
        internal FakeScriptRepository Scripts { get; }
        internal FakeAuditWriter Audits { get; }
        internal FakeReportRepository Reports { get; }
        internal RecordingBoundary Boundary { get; }
        internal CountingScopeFactory ScopeFactory { get; }
        internal LocalHostInventoryJobWorkHandler Handler { get; }
        internal Job Job { get; }
        internal ClaimedJobWork Work { get; }
        internal string ScriptPath { get; }

        public void Dispose()
        {
            Provider.Dispose();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private static string SourceScriptPath()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "src",
                    "WindowsScriptRunner.Automation",
                    "Artifacts",
                    "windows.local-host-inventory",
                    "1.0.0",
                    "Collect-LocalHostInventory.ps1");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                "Could not locate the reviewed Phase 6 artifact.");
        }
    }

    private sealed class FakeReportRepository : IJobReportRepository
    {
        internal JobReport? Report { get; private set; }

        public Task<JobReport?> GetByIdAsync(
            JobReportId id,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Report?.Id == id ? Report : null);
        }

        public Task<JobReport?> GetByJobIdAsync(
            JobId jobId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Report?.JobId == jobId ? Report : null);
        }

        public Task AddAsync(
            JobReport report,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Report is not null)
            {
                throw new ApplicationConflictException(
                    "A report already exists.");
            }

            Report = report;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeScriptRepository : IScriptDefinitionRepository
    {
        internal ScriptDefinition? Definition { get; set; }

        public Task<ScriptDefinition?> GetByIdAsync(
            ScriptDefinitionId id,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                Definition?.Id == id
                    ? Definition
                    : null);
        }

        public Task AddAsync(
            ScriptDefinition definition,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateAsync(
            ScriptDefinition definition,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FailingCommitUnitOfWork(int? failCommitNumber) : IUnitOfWork
    {
        private int _commitCount;

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _commitCount) == failCommitNumber)
            {
                throw new ApplicationConflictException(
                    "Injected terminal persistence conflict.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingBoundary(
        Func<PowerShellExecutionRequest, CancellationToken, Task<PowerShellExecutionResult>>
            execute) : IPowerShellExecutionBoundary
    {
        internal int CallCount { get; private set; }
        internal PowerShellExecutionRequest? LastRequest { get; private set; }

        public async Task<PowerShellExecutionResult> ExecuteAsync(
            PowerShellExecutionRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            var result = await execute(request, cancellationToken);
            return result with
            {
                ExecutionId = request.ExecutionId,
            };
        }
    }
}
