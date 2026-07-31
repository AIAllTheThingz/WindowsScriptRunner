# Implementation roadmap

1. **Repository and solution scaffolding — Complete and merged**
2. **Domain and application contracts — Complete and merged**
3. **SQL Server persistence — Complete and merged**
4. **Worker foundation and queue processing — Complete and merged**
5. **PowerShell execution boundary — Complete and merged**
6. **First reviewed automation package — Complete and merged**
7. **Typed durable Local Host Inventory reporting — Complete and merged**
8. **Identity, authentication, authorization, and approval workflow — Implemented locally; pending review**
9. **Production hardening and deployment — Planned**

## Phase 7 result

Reporting strictly parses the complete bounded Local Host Inventory result. Application revalidates the current fenced lease and pinned package, derives deterministic report identity, stages the immutable typed report, completes the ReadOnly DryRun, removes the lease, appends bounded audit metadata, and commits through the existing unit of work. Infrastructure adds the `JobReports` envelope and one-to-one `LocalHostInventoryReports` detail table.

No public report UI or endpoint exists because there is no authenticated principal or authorization policy.

## Phase 8 result

Phase 8 adds Negotiate-protected Web composition, stable Windows SID mapping, SID group policies, resource authorization, typed Local Host Inventory list/lookup/detail pages, and bounded approval review/approval/rejection pages. Approval fingerprints are calculated in Application from trusted persisted evidence and validated again at decision time. Authenticated requester identity reaches the existing Domain separation-of-duties rule through `ICurrentUser`; Web cannot provide an actor, role, policy, or fingerprint calculation.

The implementation adds no EF Core model change or migration. It does not broaden automation execution, script selection, reporting formats, credential access, remoting, or deployment scope. It is locally implemented and validated pending review, not deployed.

## Phase 9 boundary

Phase 9 should package and harden the already-reviewed application for Windows Server. It owns deployment assets, service installation, IIS configuration, SQL rollout, operational identities and permissions, HTTPS, SPN/Kerberos and browser-zone validation, backup/restore, production observability, and runbooks.
