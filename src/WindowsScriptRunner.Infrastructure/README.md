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

Call `services.AddInfrastructure(configuration)` from a composition root. Configure `ConnectionStrings:WindowsScriptRunner`. `Persistence:ApplyMigrationsOnStartup` defaults to `false`; prefer applying reviewed migration artifacts before production startup.
