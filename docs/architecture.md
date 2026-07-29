# Architecture

The web application and worker are separate processes. The web process must never execute PowerShell directly. It will eventually submit work through application contracts, while the worker will coordinate job execution.

The Domain project remains independent of all outer layers and now owns aggregates and lifecycle invariants. Application coordinates Phase 2 use cases through domain-specific repository, audit, clock, and unit-of-work interfaces. Infrastructure will implement these persistence and external concerns in Phase 3. PowerShell execution is planned to occur through a separate child process behind the isolated PowerShell boundary. SQL Server is planned but is not implemented.

```text
Browser
   |
   v
Web --> Application --> Domain
 |           |
 +--> Infrastructure (future SQL Server)
 +--> Reporting

Worker --> Application --> Domain
   |----> Infrastructure
   |----> Reporting
   +----> PowerShell boundary --> separate child process (future)
```

Arrows represent compile-time dependencies. Domain has no solution-project dependencies.

Contracts contains immutable transport DTOs and does not reference Domain. Application maps Domain objects to safe contract responses. Neither Web nor Worker resolves persistence-dependent handlers during startup.

Security tests parse each source `.csproj` and compare its `ProjectReference` entries with an explicit allowlist. Compiled-reference checks remain supplementary. The source-level rules explicitly prohibit Web from referencing Worker or PowerShell and require Domain and Contracts to have no project references.

Phase 2 application handlers remain orchestration-only. They load domain objects, validate related references, delegate invariants to Domain, construct bounded audit events, then update repositories and commit. No handler accepts caller-provided risk, Execute capability, parameter sensitivity, parameter type, raw credential material, or generic completion status overrides. Execution terminalization is modeled as a dedicated outcome handler so an active `JobExecution` cannot be left open by a direct job-status update.

Parameter classification is a read-side and write-side trust boundary: Application loads the pinned `ScriptDefinition`/`ScriptVersion` for both parameter writes and job-detail reads. Stored `JobParameter` data is only a name/value binding; response mapping fails closed if that binding cannot be validated against the pinned version.

Parameter absence is also resolved at this boundary. The pinned definition decides whether canonical absence (null, empty, or whitespace) is allowed before any type-specific or credential operation. An accepted absence calls the aggregate-controlled draft clear operation, which removes the explicit binding and preserves definition-owned defaults. Present SecureReference values alone cross the credential-reference lookup boundary.

Domain mutation is validation-first. Aggregate methods validate proposed scalars, actors, timestamps, transitions, children, and collection uniqueness before assigning fields or changing collections. `ScriptDefinition.UpdateDetails` validates both proposed text fields into locals before atomically assigning either field and its timestamp.
