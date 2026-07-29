using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Infrastructure.Persistence;

namespace WindowsScriptRunner.SqlServerTests;

public sealed class RepositoryFailureTests
{
    [Fact]
    public async Task ReadQueriesTranslateUnavailableSqlServerFailures()
    {
        await using var database = await SqlServerDatabase.CreateAsync(
            applyMigrations: false,
            connectionTimeoutSeconds: 1);

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
}
