# SQL Server persistence

`WindowsScriptRunner.Infrastructure` implements the Application persistence contracts with EF Core and SQL Server. The schema currently includes domain aggregates, audit records, durable worker leases, and immutable typed job reports.

## Configuration

Web and Worker read the database connection from `ConnectionStrings:WindowsScriptRunner`.

```json
{
  "Persistence": {
    "CommandTimeoutSeconds": 30,
    "RetryCount": 3,
    "RetryDelaySeconds": 5,
    "ApplyMigrationsOnStartup": false,
    "EnableDetailedErrors": false
  }
}
```

The connection string is intentionally absent from committed application settings. Supply it through a protected environment-specific configuration source such as environment variables, .NET user secrets, or an external secret/configuration provider. Windows integrated authentication and externally supplied SQL authentication are supported by the SQL Server provider. Detailed errors default to false, and sensitive-data logging is disabled.

Local-development-only LocalDB example:

```text
Server=(localdb)\MSSQLLocalDB;Database=WindowsScriptRunner_Dev;Integrated Security=true;Encrypt=false
```

Production-oriented encrypted example:

```text
Server=sql.example.internal;Database=WindowsScriptRunner;Encrypt=true;TrustServerCertificate=false;Integrated Security=true
```

Set either value without committing it:

```powershell
$env:ConnectionStrings__WindowsScriptRunner = '<connection string supplied externally>'
```

## Runtime behavior

One scoped `WindowsScriptRunnerDbContext` is shared by the repositories, `SqlAuditWriter`, and `SqlUnitOfWork`. Repositories load tracked aggregates with cancellation support and stage graph changes without saving. When an operation uses unchanged aggregate roots as validation dependencies, the unit of work rowversion-revalidates them and commits aggregate and audit changes together with one `SaveChangesAsync` call inside a serializable transaction. Other commits retain the normal `SaveChangesAsync` transaction. Validation dependencies are protected without unchanged writes or rowversion churn.

The reviewed local-host inventory completion stages the typed report, job lifecycle change, lease deletion, and audit event in the same unit of work. Report persistence has no update path. Query reconstruction requires one consistent typed detail row and fails closed on missing, duplicate, or mismatched data. Raw PowerShell stdout and stderr are never stored.

Mutable root rows have SQL Server rowversion tokens. Stale updates and changed validation dependencies become bounded concurrency exceptions. The serializable commit transaction runs inside the configured SQL Server execution strategy. Transient provider failures, including nested retry-exhaustion shapes, are translated without exposing connection strings, SQL text, parameter values, or raw provider messages.

Large aggregate loads use split queries to avoid cartesian result growth. EF parameterizes values rather than concatenating caller input.

Credential-reference duplicate detection hashes the external identifier with SHA-256 inside Infrastructure and indexes the provider/hash pair. A hash match is compared with the actual stored identifier: an exact match is a duplicate, while a different identifier is rejected as a collision. The identifier is never written to logs.

## Health

- `/health` and `/health/live` are liveness endpoints and do not require SQL Server.
- `/health/ready` requires SQL connectivity and an up-to-date migration history.

Startup migration is registered behind `Persistence:ApplyMigrationsOnStartup`, which defaults to `false`.

## SQL Server tests

Tests use `WINDOWSSCRIPTRUNNER_TEST_SQLSERVER` when supplied. Otherwise they use the installed `MSSQLLocalDB` instance. Every test creates a unique disposable database, applies migrations, and deletes the database afterward. SQLite and EF InMemory are not used as SQL Server evidence.

Durable polling, fenced claiming, lease recovery, the reviewed PowerShell package, and typed local-host inventory reporting are implemented. Production migration orchestration, backup/restore procedures, least-privilege database identities, and rollback runbooks remain Phase 9 deployment work.
