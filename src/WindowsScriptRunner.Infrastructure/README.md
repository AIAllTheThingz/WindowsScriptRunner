# Infrastructure

Infrastructure implements the Application persistence contracts with EF Core and SQL Server.

- `Persistence/Entities` contains internal database row types.
- `Persistence/Configurations` contains explicit schema, column, key, index, relationship, constraint, and rowversion mappings.
- `Persistence/Mapping` translates between domain aggregates and tracked row graphs.
- `Persistence/Repositories` implements the four domain-specific repositories without committing.
- `SqlAuditWriter` stages append-only audit events.
- `SqlUnitOfWork` owns the single atomic `SaveChangesAsync` boundary.
- `Persistence/Migrations` contains the reviewed SQL Server migration history.
- `Persistence/Health` provides SQL readiness checks.
- `Persistence/Queue` provides bounded queue/expired-lease projections and the SQL fencing-sequence source.

Call `services.AddInfrastructure(configuration)` from a composition root. Configure `ConnectionStrings:WindowsScriptRunner`. `Persistence:ApplyMigrationsOnStartup` defaults to `false`; prefer applying reviewed migration artifacts before production startup.

Phase 4 adds `AddWorkerQueueLeases`, `wsr.JobLeases`, and `wsr.JobLeaseFencingSequence`. Queue projections load only identifiers, work kind, and timestamps; the full trusted aggregate is loaded only inside Application command handlers. Repositories and queue sources never call `SaveChanges`; the existing scoped `SqlUnitOfWork` remains the sole production commit boundary.
