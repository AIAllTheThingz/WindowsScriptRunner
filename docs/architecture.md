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
