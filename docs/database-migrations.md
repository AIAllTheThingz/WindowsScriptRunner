# Database migrations

Restore the repository-local EF tool before using migration commands:

```powershell
dotnet tool restore
```

The Infrastructure project contains the design-time context factory and migrations. A local development connection string can be supplied with `ConnectionStrings__WindowsScriptRunner`.

```powershell
dotnet ef migrations list `
  --project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj `
  --startup-project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj

dotnet ef database update `
  --project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj `
  --startup-project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj

dotnet ef migrations has-pending-model-changes `
  --project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj `
  --startup-project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj
```

`InitialSqlServerPersistence` is the first migration. Generate the deployment artifact at `artifacts/sql/WindowsScriptRunner-idempotent.sql`. The repository policy ignores generated `artifacts/`, so the SQL file is intentionally not committed. Tests generate the same idempotent SQL from the migration assembly, apply it twice to real SQL Server, and verify that it contains no environment-specific connection string or credential.

Create a future migration only after the EF model is stable:

```powershell
dotnet ef migrations add <MigrationName> `
  --project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj `
  --startup-project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj `
  --context WindowsScriptRunnerDbContext `
  --output-dir Persistence\Migrations
```

Generate the deployment artifact:

```powershell
dotnet ef migrations script --idempotent `
  --project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj `
  --startup-project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj `
  --context WindowsScriptRunnerDbContext `
  --output .\artifacts\sql\WindowsScriptRunner-idempotent.sql
```

Rollback a disposable database to the empty model with `dotnet ef database update 0` using the same project, startup project, and context arguments. For a production rollback, restore from the deployment backup or apply a separately reviewed down script only after rehearsing it against a production-like copy; the application does not perform rollback automatically.

Production processes do not apply migrations by default. Apply the reviewed artifact through deployment tooling before starting the new application version. Back up production data and rehearse both forward and rollback procedures in the target SQL Server version before deployment.

## Phase 4 migration

`20260729224310_AddWorkerQueueLeases` / `AddWorkerQueueLeases` creates `wsr.JobLeaseFencingSequence`, `wsr.JobLeases`, its checks, foreign keys, unique lease ID, recovery/ownership indexes, and rowversion. It also permits the exact non-secret audit property name `FencingToken` while continuing to reject other token-, password-, and secret-shaped audit keys.

Before adding the required lease invariant, the migration normalizes possible pre-lease worker states. Unstarted `Claimed` jobs return to `ExecutionQueued`. `DryRunRunning`, active `Claimed`, `Executing`, and `PostValidation` jobs become `TimedOut`; active execution rows receive a matching timed-out outcome. Each affected job receives a bounded `LegacyWorkerStateRecovered` audit event with actor `system:migration`. No parameter, credential, or script content is copied.

The down migration removes the lease table and sequence and restores the prior audit-key constraint. Real SQL tests apply both migrations, roll back Phase 4 to the Phase 3 migration, reapply Phase 4, and apply the generated idempotent script twice. Production rollback still requires a reviewed backup/rehearsal plan because down migration cannot reconstruct leases that did not exist in Phase 3.
