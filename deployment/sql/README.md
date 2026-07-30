# SQL Server deployment

## Status

The EF Core SQL Server model and reviewed migrations are implemented through durable Phase 7 reporting. Production rollout automation is planned for Phase 9 and is not present here.

Migration source is owned by:

`src/WindowsScriptRunner.Infrastructure/Persistence/Migrations`

The current history includes:

1. `InitialSqlServerPersistence`
2. `AddWorkerQueueLeases`
3. `AddDurableLocalHostInventoryReports`

Production startup migration is disabled by default. The repository supports design-time migration commands and tests clean apply, upgrade, rollback, reapply, and pending-model detection, but it does not provide a production connection profile, reviewed generated rollout script, backup/restore automation, maintenance window procedure, or operator sign-off workflow.

Phase 9 must produce and rehearse deployment and rollback artifacts against a representative production topology. Dropping the reporting migration destroys durable reports and therefore requires an explicit backup and retention decision.

See [database migrations](../../docs/database-migrations.md), [database schema](../../docs/database-schema.md), and [SQL Server persistence](../../docs/sql-server-persistence.md).
