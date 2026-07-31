# Roadmap

## Completed implementation

1. Repository and solution scaffold — complete and merged.
2. Domain and application contracts — complete and merged.
3. SQL Server persistence — complete and merged.
4. Worker foundation and queue processing — complete and merged.
5. Isolated PowerShell execution — complete and merged.
6. First reviewed automation package — complete and merged through PR #8.
7. Strict parsing and durable typed inventory reporting — complete and merged through PR #7.

The dependency order was preserved: Phase 6 was reviewed and merged first, then Phase 7 was integrated against that reviewed baseline and merged.

## Current implementation awaiting review

8. Identity, authentication, authorization, and approval workflow.

Phase 8 establishes Negotiate-authenticated principals and stable SID mapping before exposing the safe typed Local Host Inventory views or approval actions through Web. It replaces browser authority with a trusted calculation bound to the pinned script version, requested phase, targets, parameters, execution window, and accepted dry-run evidence. It retains Domain separation-of-duties and Application audit/lease/state boundaries. The implementation is committed on its review branch, pending review, and has not been deployed or rolled out.

## Production readiness

9. Production hardening and deployment.

Phase 9 covers IIS configuration, Windows Service installation, production SQL migration rollout and rollback, certificates and HTTPS, service identities, SPN/Kerberos and browser-zone validation, permissions, backup/restore rehearsal, secrets integration, observability export, retention policy, operational runbooks, and deployment verification.

Additional packages, remoting, generic reporting, package discovery, and side-effecting automation are unscheduled. They are not authorized by Phase 8 and must not be added before review and the Phase 9 production-safety boundary are complete.
