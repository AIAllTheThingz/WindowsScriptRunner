# Implementation roadmap

1. **Repository and solution scaffolding — Complete and merged**
2. **Domain and application contracts — Complete and merged**
3. **SQL Server persistence — Complete and merged**
4. **Worker foundation and queue processing — Complete and merged**
5. **PowerShell execution boundary — Complete and merged**
6. **First reviewed automation package — Complete; pushed for review**
7. **Typed durable Local Host Inventory reporting — Complete; pushed for review**
8. **Identity, authentication, authorization, and approval workflow — Next**
9. **Production hardening and deployment — Planned**

## Phase 7 result

Reporting strictly parses the complete bounded Local Host Inventory result. Application revalidates the current fenced lease and pinned package, derives deterministic report identity, stages the immutable typed report, completes the ReadOnly DryRun, removes the lease, appends bounded audit metadata, and commits through the existing unit of work. Infrastructure adds the `JobReports` envelope and one-to-one `LocalHostInventoryReports` detail table.

No public report UI or endpoint exists because there is no authenticated principal or authorization policy.

## Phase 8 boundary

Phase 8 should add:

- stable authenticated identity mapping;
- authentication for Web;
- authorization policies for job, report, administration, and approval operations;
- trusted approval-fingerprint calculation;
- separation-of-duties enforcement using authenticated principals;
- approval and rejection composition through existing Domain/Application rules; and
- security, integration, and end-to-end tests for access control and approval evidence.

Phase 8 must not include production deployment or broaden automation execution.

## Phase 9 boundary

Phase 9 should package and harden the already-reviewed application for Windows Server. It owns deployment assets, service installation, IIS configuration, SQL rollout, operational identities and permissions, HTTPS, backup/restore, production observability, and runbooks.
