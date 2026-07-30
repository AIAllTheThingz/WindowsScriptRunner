# Roadmap

## Completed implementation

1. Repository and solution scaffold — complete and merged.
2. Domain and application contracts — complete and merged.
3. SQL Server persistence — complete and merged.
4. Worker foundation and queue processing — complete and merged.
5. Isolated PowerShell execution — complete and merged.
6. First reviewed automation package — complete and pushed on `origin/codex/phase-6-first-automation-package`.
7. Strict parsing and durable typed inventory reporting — complete and pushed on dependent branch `origin/codex/phase-7-durable-reporting`.

Implementation status and merge status are intentionally reported separately. Phase 7 depends on Phase 6 and must be reviewed and merged after it.

## Next

8. Identity, authentication, authorization, and approval workflow.

Phase 8 must establish authenticated principals and stable identity mapping before exposing typed reports or approval actions through Web. It must replace caller-supplied approval fingerprints with a trusted calculation bound to the pinned script version, requested phase, targets, parameters, execution window, and accepted dry-run evidence.

## Production readiness

9. Production hardening and deployment.

Phase 9 covers IIS configuration, Windows Service installation, production SQL migration rollout and rollback, certificates and HTTPS, service identities, permissions, backup/restore rehearsal, secrets integration, observability export, retention policy, operational runbooks, and deployment verification.

Additional packages, remoting, generic reporting, package discovery, and side-effecting automation are unscheduled. They must not be added before the identity and production-safety boundaries are complete.
