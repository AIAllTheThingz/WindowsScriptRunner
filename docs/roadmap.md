# Roadmap

## Completed implementation

1. Repository and solution scaffold — complete and merged.
2. Domain and application contracts — complete and merged.
3. SQL Server persistence — complete and merged.
4. Worker foundation and queue processing — complete and merged.
5. Isolated PowerShell execution — complete and merged.
6. First reviewed automation package — complete and merged through PR #8.
7. Strict parsing and durable typed inventory reporting — complete and merged through PR #7.
8. Identity, authentication, authorization, and approval workflow — complete and merged through PR #9.

The dependency order was preserved: Phase 6 was reviewed and merged first, then Phase 7 was integrated against that reviewed baseline and merged.

Phase 8 establishes Negotiate-authenticated principals and stable SID mapping before exposing the safe typed Local Host Inventory views or approval actions through Web. It replaces browser authority with a trusted calculation bound to the pinned script version, requested phase, targets, parameters, execution window, and accepted dry-run evidence. It retains Domain separation-of-duties and Application audit/lease/state boundaries. Phase 8 is complete and merged; it has not been deployed or rolled out.

## Next phase

9. Production hardening and deployment — next and in progress.

Phase 9 now includes the first deployment foundation: Windows Service hosting integration, explicit Worker and IIS install/verify scripts, reviewed SQL backup-and-migration execution, and hash-pinned PowerShell artifact installation. It still covers production SQL migration rollout and rollback, certificates and HTTPS, service identities, SPN/Kerberos and browser-zone validation, permissions, backup/restore rehearsal, secrets integration, observability export, retention policy, operational runbooks, and deployment verification.

Additional packages, remoting, generic reporting, package discovery, and side-effecting automation remain unscheduled. They are not part of this Phase 9 foundation.
