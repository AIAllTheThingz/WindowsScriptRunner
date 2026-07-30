# Roadmap

1. Repository and solution scaffold — complete.
2. Domain and application contracts — complete.
3. SQL Server persistence — complete.
4. Worker foundation and queue processing — complete.
5. Isolated PowerShell execution — implemented by this PR and pending review.
6. First automation package — next after Phase 5 review and merge.
7. Production hardening and deployment — not started.

Phase 5 is limited to the controlled PowerShell child-process boundary. It does not add production queue dispatch, arbitrary script selection, authentication, authorization, external secret retrieval, or Phase 6 automation-package behavior.
