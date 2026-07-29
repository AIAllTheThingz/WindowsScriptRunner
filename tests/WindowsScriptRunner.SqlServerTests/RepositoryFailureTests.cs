using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Infrastructure;
using WindowsScriptRunner.Infrastructure.Persistence;

namespace WindowsScriptRunner.SqlServerTests;

public sealed class RepositoryFailureTests
{
    [Fact]
    public async Task ReadQueriesTranslateUnavailableSqlServerFailures()
    {
        await using var database = await SqlServerDatabase.CreateAsync(
            applyMigrations: false,
            connectionTimeoutSeconds: 1,
            baseConnectionString:
                "Server=tcp:192.0.2.1,1433;Integrated Security=true;Encrypt=false",
            ownsDatabase: false);

        await using (var scope = new PersistenceTestScope(database))
        {
            await Assert.ThrowsAsync<PersistenceUnavailableException>(
                () => scope.Jobs.GetByIdAsync(JobId.New(), CancellationToken.None));
        }

        await using (var scope = new PersistenceTestScope(database))
        {
            await Assert.ThrowsAsync<PersistenceUnavailableException>(
                () => scope.Jobs.ExistsAsync(JobId.New(), CancellationToken.None));
        }

        await using (var scope = new PersistenceTestScope(database))
        {
            await Assert.ThrowsAsync<PersistenceUnavailableException>(
                () => scope.Scripts.GetByIdAsync(
                    ScriptDefinitionId.New(),
                    CancellationToken.None));
        }

        await using (var scope = new PersistenceTestScope(database))
        {
            await Assert.ThrowsAsync<PersistenceUnavailableException>(
                () => scope.Workers.GetByIdAsync(
                    WorkerNodeId.New(),
                    CancellationToken.None));
        }

        await using (var scope = new PersistenceTestScope(database))
        {
            await Assert.ThrowsAsync<PersistenceUnavailableException>(
                () => scope.Credentials.GetByIdAsync(
                    CredentialReferenceId.New(),
                    CancellationToken.None));
        }

        await using (var scope = new PersistenceTestScope(database))
        {
            await Assert.ThrowsAsync<PersistenceUnavailableException>(
                () => scope.Credentials.AddAsync(
                    SqlServerTestData.Credential(),
                    CancellationToken.None));
        }

        await using (var scope = new PersistenceTestScope(database))
        {
            await scope.Audits.WriteAsync(
                new AuditEvent(
                    AuditEventId.New(),
                    "Unavailable",
                    "Test",
                    "unavailable",
                    SqlServerTestData.Requester,
                    SqlServerTestData.Time,
                    "Unavailable"),
                CancellationToken.None);
            await Assert.ThrowsAsync<PersistenceUnavailableException>(
                () => scope.UnitOfWork.CommitAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task CommitTranslatesSqlExceptionNestedInsideRetryExhaustion()
    {
        await using var unavailableDatabase = await SqlServerDatabase.CreateAsync(
            applyMigrations: false,
            connectionTimeoutSeconds: 1,
            baseConnectionString:
                "Server=tcp:192.0.2.1,1433;Integrated Security=true;Encrypt=false",
            ownsDatabase: false);
        await using var unavailableConnection = new SqlConnection(
            unavailableDatabase.ConnectionString);
        var sqlException = await Assert.ThrowsAsync<SqlException>(
            () => unavailableConnection.OpenAsync(CancellationToken.None));

        await using var database = await SqlServerDatabase.CreateAsync();
        var retryException = new RetryLimitExceededException(
            "Retry limit exceeded.",
            new DbUpdateException("Save failed.", sqlException));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:WindowsScriptRunner"] = database.ConnectionString,
                    ["Persistence:RetryCount"] = "1",
                    ["Persistence:RetryDelaySeconds"] = "0",
                })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        services.AddDbContext<WindowsScriptRunnerDbContext>(
            options => options.AddInterceptors(
                new RetryExhaustionSaveChangesInterceptor(retryException)));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider
            .GetRequiredService<WindowsScriptRunnerDbContext>();
        var auditWriter = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        Assert.True(context.Database.CreateExecutionStrategy().RetriesOnFailure);
        await auditWriter.WriteAsync(
            new AuditEvent(
                AuditEventId.New(),
                "RetryExhausted",
                "Test",
                "retry-exhausted",
                SqlServerTestData.Requester,
                SqlServerTestData.Time,
                "Retry exhausted"),
            CancellationToken.None);

        var translated = await Assert.ThrowsAsync<PersistenceUnavailableException>(
            () => unitOfWork.CommitAsync(CancellationToken.None));

        Assert.Same(retryException, translated.InnerException);
        Assert.IsType<DbUpdateException>(translated.InnerException!.InnerException);
    }

    private sealed class RetryExhaustionSaveChangesInterceptor(
        RetryLimitExceededException exception) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<int>>(exception);
    }
}
