# SQL Server deployment

## Status

The EF Core SQL Server model and reviewed migrations are implemented through durable Phase 8 reporting. Phase 9 has added `Invoke-ReviewedMigration.ps1` for an explicit, Windows-integrated rollout path. It creates a `COPY_ONLY` backup before applying a reviewed idempotent script and supports `-WhatIf`. No production database has been changed by this repository work.

`Invoke-ReviewedMigration.ps1` intentionally supports only a local SQL Server host or instance and a local absolute backup path. The SQL Server service account must have write access to the local backup directory before the migration is attempted.

Migration source is owned by:

`src/WindowsScriptRunner.Infrastructure/Persistence/Migrations`

The current history includes:

1. `InitialSqlServerPersistence`
2. `AddWorkerQueueLeases`
3. `AddDurableLocalHostInventoryReports`
4. `AddTrustedDryRunApprovalEvidence`

Production startup migration is disabled by default. The repository supports design-time migration commands and tests clean apply, upgrade, rollback, reapply, and pending-model detection. The rollout script still requires an operator-generated reviewed idempotent script, an approved backup path, Windows-integrated `sqlcmd`, a maintenance window, and an operator sign-off record.

The rollout script does not implement rollback automatically. Restoring the pre-migration backup and revalidating application readiness remains the supported rollback boundary. Phase 9 must still rehearse forward migration, restore, retention, and verification against a representative topology.

See [database migrations](../../docs/database-migrations.md), [database schema](../../docs/database-schema.md), and [SQL Server persistence](../../docs/sql-server-persistence.md).
