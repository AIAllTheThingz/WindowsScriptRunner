# Infrastructure

Infrastructure implements the Application persistence contracts with EF Core and SQL Server.

- `Persistence/Entities` contains internal database row types.
- `Persistence/Configurations` contains explicit schema, column, key, index, relationship, constraint, and rowversion mappings.
- `Persistence/Mapping` translates between domain aggregates and tracked row graphs.
- `Persistence/Repositories` implements domain-specific repositories without committing.
- `SqlAuditWriter` stages append-only audit events.
- `SqlUnitOfWork` owns the single atomic `SaveChangesAsync` boundary.
- `Persistence/Migrations` contains the reviewed SQL Server migration history.
- `Persistence/Health` provides SQL readiness checks.
- `Persistence/Queue` provides bounded queue/expired-lease projections, the SQL fencing-sequence source, and the SQL Server UTC worker-coordination clock.

Call `services.AddInfrastructure(configuration)` from a composition root. Configure `ConnectionStrings:WindowsScriptRunner`. `Persistence:ApplyMigrationsOnStartup` defaults to `false`; prefer applying reviewed migration artifacts before production startup.

The current migration history provides initial persistence, `JobLeases` with `JobLeaseFencingSequence`, and durable reporting through the immutable `JobReports` envelope plus one-to-one typed `LocalHostInventoryReports` detail.

Queue projections load only bounded identifiers and routing metadata; full trusted aggregates are loaded inside Application handlers. Repositories and queue sources never commit. `SqlUnitOfWork` remains the single production `SaveChangesAsync` boundary.

`SqlJobReportRepository` supports only add and bounded typed Local Host Inventory lookup/list operations; no update method exists. `SqlJobRepository` also supplies the bounded `AwaitingApproval` list used by the protected portal. The report schema stores no raw stdout, stderr, or JSON payload and independently enforces metadata, provenance, typed bounds, deterministic uniqueness, and supported architecture. Phase 8 adds the `20260731165153_AddTrustedDryRunApprovalEvidence` migration for immutable accepted DryRun evidence and the report-list index; it does not add a persisted identity/session/fingerprint record.

See [SQL Server persistence](../../docs/sql-server-persistence.md), [database schema](../../docs/database-schema.md), and [database migrations](../../docs/database-migrations.md).
