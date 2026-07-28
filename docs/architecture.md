# Architecture

The web application and worker are separate processes. The web process must never execute PowerShell directly. It will eventually submit work through application contracts, while the worker will coordinate job execution.

The Domain project remains independent of all outer layers. Application will coordinate future use cases. Infrastructure will implement future persistence and external concerns. PowerShell execution is planned to occur through a separate child process behind the isolated PowerShell boundary. SQL Server is planned but is not implemented in Phase 1.

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
