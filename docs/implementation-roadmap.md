# Implementation roadmap

1. **Repository and solution scaffolding — Complete**
2. **Domain and application contracts — Complete**
3. **SQL Server persistence — Complete**
4. **Worker foundation and queue processing — Complete and merged**
5. **PowerShell execution boundary — Complete and merged**
6. **First automation package — Implemented**
7. **Durable reporting — Next**
8. Approval workflow
9. Production hardening and deployment

Phase 6 adds one reviewed path from a pinned SQL queue route through the existing lease boundary to the PowerShell child process. It does not create a general automation platform: `windows.local-host-inventory` `1.0.0` is the only production package, accepts no parameters or credentials, supports only DryRun, and does not persist its JSON output.
