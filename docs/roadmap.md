# Roadmap

1. Repository and solution scaffold — complete.
2. Domain and application contracts — complete.
3. SQL Server persistence — complete.
4. Worker foundation and queue processing — complete and merged.
5. Isolated PowerShell execution — complete and merged.
6. First automation package — implemented by this branch.
7. Reporting and durable inventory-result persistence — next.
8. Production hardening and deployment — not started.

Phase 6 adds only `windows.local-host-inventory` version `1.0.0`. It is hash-pinned, parameterless, local-only, ReadOnly, and DryRun-only. It connects only its pinned `ScriptVersionId` to the leased Worker queue. Output is bounded and discarded after outcome mapping; report persistence, authentication, authorization, secrets, arbitrary scripts, remoting, deployment, and general package discovery remain out of scope.
