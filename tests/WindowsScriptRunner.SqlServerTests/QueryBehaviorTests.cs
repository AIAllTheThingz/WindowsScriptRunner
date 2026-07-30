using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Infrastructure;
using WindowsScriptRunner.Infrastructure.Persistence;
using WindowsScriptRunner.Infrastructure.Persistence.Queue;
using WindowsScriptRunner.Infrastructure.Persistence.Repositories;

namespace WindowsScriptRunner.SqlServerTests;

public sealed class QueryBehaviorTests
{
    [Fact]
    public async Task AggregateLoadsUseBoundedParameterizedQueries()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var firstParameter = SqlServerTestData.Parameter(
            "Mode",
            ScriptParameterType.Enum,
            allowedValues: ["Safe", "Force"]);
        var firstVersion = SqlServerTestData.Version([firstParameter]);
        var secondParameter = SqlServerTestData.Parameter("Name");
        var secondVersion = SqlServerTestData.Version(
            [secondParameter],
            version: "2.0.0");
        var script = SqlServerTestData.Script(firstVersion);
        script.AddVersion(secondVersion, script.UpdatedUtc.AddMinutes(1));
        var job = SqlServerTestData.CompleteExecuteJob(script, firstVersion);
        await using (var seed = new PersistenceTestScope(database))
        {
            await seed.Scripts.AddAsync(script, CancellationToken.None);
            await seed.Jobs.AddAsync(job, CancellationToken.None);
            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        var capture = new CommandCaptureInterceptor();
        await using var context = database.CreateContext(capture);
        var repository = new SqlScriptDefinitionRepository(
            context,
            NullLogger<SqlScriptDefinitionRepository>.Instance);

        var loaded = await repository.GetByIdAsync(script.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Versions.Count);
        Assert.InRange(capture.Commands.Count, 2, 6);
        Assert.All(
            capture.IsolationLevels,
            isolationLevel => Assert.Equal(IsolationLevel.Serializable, isolationLevel));
        Assert.All(
            capture.Commands,
            command => Assert.DoesNotContain(
                script.Id.ToString(),
                command,
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(capture.ParameterCounts, count => count > 0);

        capture.Commands.Clear();
        capture.ParameterCounts.Clear();
        capture.IsolationLevels.Clear();
        var jobRepository = new SqlJobRepository(
            context,
            NullLogger<SqlJobRepository>.Instance);

        var loadedJob = await jobRepository.GetByIdAsync(job.Id, CancellationToken.None);

        Assert.NotNull(loadedJob);
        Assert.Equal(5, capture.Commands.Count);
        Assert.All(
            capture.IsolationLevels,
            isolationLevel => Assert.Equal(IsolationLevel.Serializable, isolationLevel));
        Assert.All(
            capture.Commands,
            command => Assert.DoesNotContain(
                job.Id.ToString(),
                command,
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(capture.ParameterCounts, count => count > 0);
    }

    [Fact]
    public async Task ScriptAggregateLoadSupportsConfiguredRetryStrategy()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var version = SqlServerTestData.Version();
        var script = SqlServerTestData.Script(version);
        await using (var seed = new PersistenceTestScope(database))
        {
            await seed.Scripts.AddAsync(script, CancellationToken.None);
            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:WindowsScriptRunner"] = database.ConnectionString,
                    ["Persistence:RetryCount"] = "2",
                    ["Persistence:RetryDelaySeconds"] = "1",
                })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IScriptDefinitionRepository>();
        var context = scope.ServiceProvider
            .GetRequiredService<WindowsScriptRunnerDbContext>();
        var persistenceOptions = scope.ServiceProvider
            .GetRequiredService<IOptions<SqlServerPersistenceOptions>>()
            .Value;

        var loaded = await repository.GetByIdAsync(script.Id, CancellationToken.None);

        Assert.Equal(2, persistenceOptions.RetryCount);
        Assert.Equal(1, persistenceOptions.RetryDelaySeconds);
        Assert.True(context.Database.CreateExecutionStrategy().RetriesOnFailure);
        Assert.NotNull(loaded);
        Assert.Equal(script.Id, loaded.Id);
        Assert.Single(loaded.Versions);
    }

    [Fact]
    public async Task QueueCandidateQueryIsBoundedParameterizedAndLoadsOnlySafeMetadata()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var parameter = SqlServerTestData.Parameter("Mode");
        var version = SqlServerTestData.Version([parameter]);
        var script = SqlServerTestData.Script(version);
        var job = SqlServerTestData.DryRunQueuedJob(script, version);
        await using (var seed = new PersistenceTestScope(database))
        {
            await seed.Scripts.AddAsync(script, CancellationToken.None);
            await seed.Jobs.AddAsync(job, CancellationToken.None);
            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        var capture = new CommandCaptureInterceptor();
        await using var context = database.CreateContext(capture);
        var source = new SqlJobQueueCandidateSource(
            context,
            NullLogger<SqlJobQueueCandidateSource>.Instance);

        var candidates = await source.FindCandidatesAsync(
            SqlServerTestData.Routes(version, JobWorkKind.DryRun),
            5,
            SqlServerTestData.Time.AddDays(1),
            CancellationToken.None);

        Assert.Single(candidates);
        var command = Assert.Single(capture.Commands);
        Assert.Contains("TOP(", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UpdatedUtc", command, StringComparison.Ordinal);
        Assert.Contains("CreatedUtc", command, StringComparison.Ordinal);
        Assert.Contains("JobLeases", command, StringComparison.Ordinal);
        Assert.DoesNotContain("SerializedValue", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Credential", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(job.Id.ToString(), command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(capture.ParameterCounts, count => count >= 2);

        capture.Commands.Clear();
        capture.ParameterCounts.Clear();
        var none = await source.FindCandidatesAsync(
            new HashSet<JobWorkRoute>(),
            5,
            SqlServerTestData.Time.AddDays(1),
            CancellationToken.None);
        Assert.Empty(none);
        Assert.Empty(capture.Commands);
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();
        public ConcurrentQueue<int> ParameterCounts { get; } = new();
        public ConcurrentQueue<IsolationLevel?> IsolationLevels { get; } = new();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            ParameterCounts.Enqueue(command.Parameters.Count);
            IsolationLevels.Enqueue(command.Transaction?.IsolationLevel);
            return base.ReaderExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }
    }
}
