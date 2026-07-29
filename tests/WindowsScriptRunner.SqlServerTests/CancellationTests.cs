using Microsoft.EntityFrameworkCore;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Identifiers;

namespace WindowsScriptRunner.SqlServerTests;

public sealed class CancellationTests
{
    [Fact]
    public async Task PersistencePathsPropagateCancellation()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var scope = new PersistenceTestScope(database);
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();
        var token = cancellationSource.Token;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scope.Jobs.GetByIdAsync(JobId.New(), token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scope.Jobs.ExistsAsync(JobId.New(), token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scope.Credentials.GetByIdAsync(CredentialReferenceId.New(), token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scope.Context.Jobs.AnyAsync(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scope.UnitOfWork.CommitAsync(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scope.Audits.WriteAsync(
                new AuditEvent(
                    AuditEventId.New(),
                    "Cancelled",
                    "Test",
                    "test",
                    SqlServerTestData.Requester,
                    SqlServerTestData.Time,
                    "Cancelled"),
                token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SqlServerDatabase.CreateAsync(cancellationToken: token));
    }
}
