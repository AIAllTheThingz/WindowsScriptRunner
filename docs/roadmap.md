# Roadmap

1. Repository and solution scaffold — complete.
2. Domain and application contracts — complete.
3. SQL Server persistence — complete.
4. Worker foundation and queue processing — complete and merged.
5. Isolated PowerShell execution — complete and merged.
6. First automation package — complete on `origin/codex/phase-6-first-automation-package`.
7. Reporting and durable inventory-result persistence — complete on this branch.
8. Identity, authentication, authorization, and approval workflow — next.

Phase 7 remains deliberately narrow. Only successful output from `windows.local-host-inventory` version `1.0.0` is accepted. The focused Reporting parser validates the exact schema and execution envelope, converts it to immutable typed values, and computes a deterministic digest. Application persists one report and completes the fenced job and lease atomically. SQL stores typed fields only; raw stdout, stderr, arbitrary JSON, CSV, HTML, report uploads, new packages, public report pages, and generic schema registration remain out of scope.

This branch depends on the unmerged Phase 6 branch because Phase 6 was not in `origin/main` when Phase 7 began.
