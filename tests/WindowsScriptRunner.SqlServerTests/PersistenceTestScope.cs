using Microsoft.Extensions.Logging.Abstractions;
using WindowsScriptRunner.Infrastructure.Persistence;
using WindowsScriptRunner.Infrastructure.Persistence.Repositories;

namespace WindowsScriptRunner.SqlServerTests;

internal sealed class PersistenceTestScope : IAsyncDisposable
{
    public PersistenceTestScope(SqlServerDatabase database)
    {
        Context = database.CreateContext();
        Jobs = new SqlJobRepository(Context, NullLogger<SqlJobRepository>.Instance);
        Scripts = new SqlScriptDefinitionRepository(
            Context,
            NullLogger<SqlScriptDefinitionRepository>.Instance);
        Workers = new SqlWorkerNodeRepository(
            Context,
            NullLogger<SqlWorkerNodeRepository>.Instance);
        Credentials = new SqlCredentialReferenceRepository(
            Context,
            NullLogger<SqlCredentialReferenceRepository>.Instance);
        Audits = new SqlAuditWriter(Context, NullLogger<SqlAuditWriter>.Instance);
        UnitOfWork = new SqlUnitOfWork(Context, NullLogger<SqlUnitOfWork>.Instance);
    }

    public WindowsScriptRunnerDbContext Context { get; }
    public SqlJobRepository Jobs { get; }
    public SqlScriptDefinitionRepository Scripts { get; }
    public SqlWorkerNodeRepository Workers { get; }
    public SqlCredentialReferenceRepository Credentials { get; }
    public SqlAuditWriter Audits { get; }
    public SqlUnitOfWork UnitOfWork { get; }

    public ValueTask DisposeAsync() => Context.DisposeAsync();
}
