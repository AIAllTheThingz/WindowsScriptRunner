using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WindowsScriptRunner.Application;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Application.Reports;
using WindowsScriptRunner.Automation;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Domain.Workers;
using WindowsScriptRunner.Infrastructure;
using WindowsScriptRunner.Infrastructure.Persistence;

namespace WindowsScriptRunner.SqlServerTests;

public sealed class Phase7EndToEndTests
{
    [Fact]
    public async Task RealPackageExecutionCreatesOneTypedDurableReportAtomically()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var root = Path.Combine(
            Path.GetTempPath(),
            "WindowsScriptRunner.Phase7.EndToEnd",
            Guid.NewGuid().ToString("N"));
        var allowedRoot = Path.Combine(root, "allowed");
        var workingRoot = Path.Combine(root, "working");
        var scriptPath = Path.Combine(
            allowedRoot,
            LocalHostInventoryPackageMetadata.RelativeScriptPath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        Directory.CreateDirectory(workingRoot);
        File.Copy(SourceScriptPath(), scriptPath);
        var logs = new RecordingLoggerProvider();

        try
        {
            var configuration = Configuration(
                database.ConnectionString,
                allowedRoot,
                workingRoot);
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddProvider(logs));
            services.AddApplication();
            services.AddInfrastructure(configuration);
            services.AddProductionAutomation(configuration);
            await using var provider = services.BuildServiceProvider();

            await using (var registration = provider.CreateAsyncScope())
            {
                Assert.True(
                    await registration.ServiceProvider
                        .GetRequiredService<LocalHostInventoryPackageRegistrar>()
                        .RegisterAsync(CancellationToken.None));
            }

            var workerId = WorkerNodeId.New();
            Job job;
            DateTimeOffset now;
            await using (var seed = provider.CreateAsyncScope())
            {
                now = await seed.ServiceProvider
                    .GetRequiredService<IWorkerCoordinationClock>()
                    .GetUtcNowAsync(CancellationToken.None);
                var definition = Assert.IsType<ScriptDefinition>(
                    await seed.ServiceProvider
                        .GetRequiredService<IScriptDefinitionRepository>()
                        .GetByIdAsync(
                            LocalHostInventoryPackageMetadata.DefinitionId,
                            CancellationToken.None));
                var version = Assert.Single(definition.Versions);
                var worker = new WorkerNode(
                    workerId,
                    "phase7-e2e-worker",
                    now);
                worker.RecordHeartbeat(now);
                await seed.ServiceProvider
                    .GetRequiredService<IWorkerNodeRepository>()
                    .AddAsync(worker, CancellationToken.None);

                var requester = new UserIdentity("DOMAIN\\phase7-e2e");
                job = Job.CreateDraft(
                    JobId.New(),
                    definition.Id,
                    version.Id,
                    ExecutionPhase.DryRun,
                    requester,
                    now);
                job.AddTarget(
                    new TargetName("local-worker"),
                    requester,
                    now);
                job.Submit(definition, requester, now);
                job.MarkValidated(requester, now);
                job.QueueDryRun(requester, now);
                await seed.ServiceProvider
                    .GetRequiredService<IJobRepository>()
                    .AddAsync(job, CancellationToken.None);
                await seed.ServiceProvider
                    .GetRequiredService<IUnitOfWork>()
                    .CommitAsync(CancellationToken.None);
            }

            JobQueueCandidate candidate;
            await using (var discovery = provider.CreateAsyncScope())
            {
                candidate = Assert.Single(
                    await discovery.ServiceProvider
                        .GetRequiredService<IJobQueueCandidateSource>()
                        .FindCandidatesAsync(
                            LocalHostInventoryPackageMetadata.SupportedRoutes,
                            10,
                            now,
                            CancellationToken.None));
            }

            ClaimedJobWork claimed;
            await using (var acquisition = provider.CreateAsyncScope())
            {
                claimed = await acquisition.ServiceProvider
                    .GetRequiredService<AcquireJobLeaseHandler>()
                    .HandleAsync(
                        new AcquireJobLeaseCommand(
                            candidate.JobId,
                            candidate.WorkKind,
                            candidate.ScriptVersionId,
                            workerId,
                            TimeSpan.FromMinutes(2),
                            TimeSpan.FromHours(1)),
                        CancellationToken.None);
            }

            Assert.Equal(JobWorkKind.DryRun, claimed.WorkKind);
            var handler = Assert.Single(
                provider.GetServices<IJobWorkHandler>());
            await handler.HandleAsync(claimed, CancellationToken.None);

            await using var verification = provider.CreateAsyncScope();
            var persisted = Assert.IsType<Job>(
                await verification.ServiceProvider
                    .GetRequiredService<IJobRepository>()
                    .GetByIdAsync(job.Id, CancellationToken.None));
            var report = await verification.ServiceProvider
                .GetRequiredService<GetLocalHostInventoryReportHandler>()
                .HandleAsync(
                    new GetLocalHostInventoryReportByJobIdQuery(job.Id),
                    CancellationToken.None);
            var context = verification.ServiceProvider
                .GetRequiredService<WindowsScriptRunnerDbContext>();

            Assert.Equal(JobStatus.Completed, persisted.Status);
            Assert.Null(persisted.Lease);
            Assert.Equal(Environment.MachineName, report.ComputerName);
            Assert.Equal("1.0", report.SchemaVersion);
            Assert.Equal("Json", report.Format);
            Assert.Equal(workerId.Value, report.WorkerNodeId);
            Assert.Equal(claimed.LeaseId.Value, report.LeaseId);
            Assert.Equal(claimed.FencingToken, report.FencingToken);
            Assert.Equal(job.Id.Value, report.PowerShellExecutionId);
            Assert.Matches("^[0-9a-f]{64}$", report.Sha256);
            Assert.Equal(1, await context.JobReports.CountAsync());
            Assert.Equal(
                1,
                await context.LocalHostInventoryReports.CountAsync());
            Assert.Equal(0, await context.JobLeases.CountAsync());
            Assert.DoesNotContain(
                await context.AuditEvents
                    .SelectMany(audit => audit.Properties)
                    .Select(property => property.Value)
                    .ToArrayAsync(),
                value => value.Contains(
                    report.ComputerName,
                    StringComparison.OrdinalIgnoreCase) ||
                    value.Contains(
                        report.OsDescription,
                        StringComparison.OrdinalIgnoreCase) ||
                    value.Contains(
                        report.PowerShellVersion,
                        StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                logs.Messages,
                message => message.Contains(
                    report.ComputerName,
                    StringComparison.OrdinalIgnoreCase) ||
                    message.Contains(
                        report.OsDescription,
                        StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            logs.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static IConfiguration Configuration(
        string connectionString,
        string allowedRoot,
        string workingRoot) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:WindowsScriptRunner"] =
                        connectionString,
                    ["Automation:LocalHostInventory:Enabled"] = "true",
                    ["Automation:LocalHostInventory:RegisterOnStartup"] = "false",
                    ["PowerShellExecution:AllowedScriptRoot"] = allowedRoot,
                    ["PowerShellExecution:WorkingRoot"] = workingRoot,
                    ["PowerShellExecution:MinimumVersion"] = "7.4.0",
                    ["PowerShellExecution:DefaultTimeoutSeconds"] = "60",
                    ["PowerShellExecution:MaximumTimeoutSeconds"] = "60",
                })
            .Build();

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

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];
        private readonly object _sync = new();

        internal IReadOnlyList<string> Messages
        {
            get
            {
                lock (_sync)
                {
                    return _messages.ToArray();
                }
            }
        }

        public ILogger CreateLogger(string categoryName) =>
            new RecordingLogger(this);

        public void Dispose()
        {
        }

        private void Add(string message)
        {
            lock (_sync)
            {
                _messages.Add(message);
            }
        }

        private sealed class RecordingLogger(RecordingLoggerProvider provider) :
            ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull =>
                null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                provider.Add(formatter(state, exception));
        }
    }
}
