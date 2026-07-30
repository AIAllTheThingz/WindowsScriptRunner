using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WindowsScriptRunner.Application;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Queue;
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

public sealed class Phase6EndToEndTests
{
    [Fact]
    public async Task QueuedReviewedPackageExecutesThroughFencedLeaseAndResolvesTerminally()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var root = Path.Combine(
            Path.GetTempPath(),
            "WindowsScriptRunner.Phase6.EndToEnd",
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

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:WindowsScriptRunner"] =
                            database.ConnectionString,
                        ["Automation:LocalHostInventory:Enabled"] = "true",
                        ["Automation:LocalHostInventory:RegisterOnStartup"] = "false",
                        ["PowerShellExecution:AllowedScriptRoot"] = allowedRoot,
                        ["PowerShellExecution:WorkingRoot"] = workingRoot,
                        ["PowerShellExecution:MinimumVersion"] = "7.4.0",
                        ["PowerShellExecution:DefaultTimeoutSeconds"] = "60",
                        ["PowerShellExecution:MaximumTimeoutSeconds"] = "60",
                    })
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
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
                var clock = seed.ServiceProvider
                    .GetRequiredService<IWorkerCoordinationClock>();
                now = await clock.GetUtcNowAsync(CancellationToken.None);
                var scripts = seed.ServiceProvider
                    .GetRequiredService<IScriptDefinitionRepository>();
                var definition = Assert.IsType<ScriptDefinition>(
                    await scripts.GetByIdAsync(
                        LocalHostInventoryPackageMetadata.DefinitionId,
                        CancellationToken.None));
                var version = Assert.Single(definition.Versions);
                var worker = new WorkerNode(workerId, "phase6-e2e-worker", now);
                worker.RecordHeartbeat(now);
                await seed.ServiceProvider
                    .GetRequiredService<IWorkerNodeRepository>()
                    .AddAsync(worker, CancellationToken.None);

                var requester = new UserIdentity("DOMAIN\\phase6-e2e");
                job = Job.CreateDraft(
                    JobId.New(),
                    definition.Id,
                    version.Id,
                    ExecutionPhase.DryRun,
                    requester,
                    now);
                job.AddTarget(new TargetName("local-worker"), requester, now);
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

            var handler = Assert.Single(provider.GetServices<IJobWorkHandler>());
            await handler.HandleAsync(claimed, CancellationToken.None);

            await using var verification = provider.CreateAsyncScope();
            var persisted = Assert.IsType<Job>(
                await verification.ServiceProvider
                    .GetRequiredService<IJobRepository>()
                    .GetByIdAsync(job.Id, CancellationToken.None));
            Assert.Equal(JobStatus.Completed, persisted.Status);
            Assert.Null(persisted.Lease);
            Assert.Empty(persisted.Executions);
            var machineName = Environment.MachineName;
            var auditValues = verification.ServiceProvider
                .GetRequiredService<WindowsScriptRunnerDbContext>()
                .AuditEvents
                .Select(audit => audit.Summary)
                .ToArray();
            Assert.DoesNotContain(
                auditValues,
                value => value.Contains(machineName, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
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
