# Roadmap

1. Repository and solution scaffold — complete.
2. Domain and application contracts — complete.
3. SQL Server persistence — complete.
4. Worker foundation and queue processing — implemented by this PR and pending review.
5. Isolated PowerShell execution — next after Phase 4 review and merge.
6. Production hardening and deployment — not started.

Phase 4 is limited to worker coordination and lease-backed queue mechanics. It does not add script execution, authentication, authorization, external secret retrieval, deployment automation, or Phase 5 behavior.
