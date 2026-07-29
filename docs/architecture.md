# Architecture

The web application and worker are separate processes. The web process must never execute PowerShell directly. It will eventually submit work through application contracts, while the worker will coordinate job execution.

The Domain project remains independent of all outer layers and owns aggregates and lifecycle invariants. Application coordinates use cases through domain-specific repository, audit, clock, and unit-of-work interfaces. Infrastructure implements those persistence contracts with EF Core and SQL Server. PowerShell execution is planned to occur through a separate child process behind the isolated PowerShell boundary and is not implemented in Phase 3.

```text
Browser
   |
   v
Web --> Application --> Domain
 |           |
 +--> Infrastructure (EF Core / SQL Server)
 +--> Reporting

Worker --> Application --> Domain
   |----> Infrastructure
   |----> Reporting
   +----> PowerShell boundary --> separate child process (future)
```

Arrows represent compile-time dependencies. Domain has no solution-project dependencies.

Contracts contains immutable transport DTOs and does not reference Domain. Application maps Domain objects to safe contract responses. Web and Worker are composition roots: both register Infrastructure, while neither references EF Core or `Microsoft.Data.SqlClient` directly. Worker does not reference the PowerShell project.

Security tests parse each source `.csproj` and compare its `ProjectReference` entries with an explicit allowlist. Compiled-reference checks remain supplementary. The source-level rules explicitly prohibit Web from referencing Worker or PowerShell and require Domain and Contracts to have no project references.

Phase 2 application handlers remain orchestration-only. They load domain objects, validate related references, delegate invariants to Domain, construct bounded audit events, then update repositories and commit. No handler accepts caller-provided risk, Execute capability, parameter sensitivity, parameter type, raw credential material, or generic completion status overrides. Execution terminalization is modeled as a dedicated outcome handler so an active `JobExecution` cannot be left open by a direct job-status update.

Parameter classification is a read-side and write-side trust boundary: Application loads the pinned `ScriptDefinition`/`ScriptVersion` for both parameter writes and job-detail reads. Stored `JobParameter` data is only a name/value binding; response mapping fails closed if that binding cannot be validated against the pinned version.

Parameter absence is also resolved at this boundary. The pinned definition decides whether canonical absence (null, empty, or whitespace) is allowed before any type-specific or credential operation. An accepted absence calls the aggregate-controlled draft clear operation, which removes the explicit binding and preserves definition-owned defaults. Present SecureReference values alone cross the credential-reference lookup boundary.

Domain mutation is validation-first. Aggregate methods validate proposed scalars, actors, timestamps, transitions, children, and collection uniqueness before assigning fields or changing collections. `ScriptDefinition.UpdateDetails` validates both proposed text fields into locals before atomically assigning either field and its timestamp.

## Persistence boundary

Infrastructure uses separate internal EF row types rather than annotating or exposing domain objects. `PersistenceMapper` is the only domain-to-row and row-to-domain translation boundary. Internal, validated rehydration factories reconstruct aggregates without public setters or persistence attributes.

Repositories track one aggregate graph per scoped `WindowsScriptRunnerDbContext` and stage changes without saving. `SqlAuditWriter` stages append-only audit rows in the same context. `SqlUnitOfWork.CommitAsync` is the only production `SaveChangesAsync` boundary, so aggregate and audit changes commit atomically. The configured execution strategy encloses the commit; when unchanged tracked aggregate roots are used as read dependencies, they are rowversion-revalidated under a serializable transaction without being updated. SQL rowversion columns protect mutable roots and validation dependencies, and stale operations become bounded application-facing concurrency exceptions.

`AddInfrastructure` registers a scoped context, repositories, audit writer, unit of work, readiness health check, and a guarded migration hosted service. Startup migration is disabled unless `Persistence:ApplyMigrationsOnStartup` is explicitly enabled. Liveness does not depend on SQL Server; readiness requires connectivity and no pending migrations.
