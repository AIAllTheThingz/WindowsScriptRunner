# Windows Script Runner roadmap

This is the single source of truth for project status and execution order. It was reconciled on
2026-09-07 against the reviewed Phase 9B baseline at `fe45ee5` (PR #16).

## Product boundary

Windows Script Runner currently supports one reviewed, hash-pinned, local-only, parameterless,
ReadOnly/DryRun automation package: `windows.local-host-inventory` version `1.0.0`. The roadmap does
not broaden that boundary until the existing capability is operationally proven.

Status meanings:

- **Complete** — merged and validated for the implemented scope.
- **Next** — the only milestone ready to begin.
- **Blocked** — waits for the preceding milestone or an unmet external prerequisite.
- **Unscheduled** — a possible future capability, not a commitment.

## Delivered baseline

| Phase | Outcome | Status | Evidence |
| --- | --- | --- | --- |
| 1 | Repository and solution scaffold | Complete | `main` history |
| 2 | Domain model and Application contracts | Complete | [PR #1](https://github.com/AIAllTheThingz/WindowsScriptRunner/pull/1) |
| 3 | SQL Server persistence and migrations | Complete | [PR #2](https://github.com/AIAllTheThingz/WindowsScriptRunner/pull/2) |
| 4 | Worker queue, leasing, recovery, and dispatch | Complete | [PR #3](https://github.com/AIAllTheThingz/WindowsScriptRunner/pull/3), [PR #4](https://github.com/AIAllTheThingz/WindowsScriptRunner/pull/4) |
| 5 | Isolated, bounded PowerShell 7 execution | Complete | [PR #5](https://github.com/AIAllTheThingz/WindowsScriptRunner/pull/5), [PR #6](https://github.com/AIAllTheThingz/WindowsScriptRunner/pull/6) |
| 6 | First reviewed Local Host Inventory package | Complete | [PR #8](https://github.com/AIAllTheThingz/WindowsScriptRunner/pull/8) |
| 7 | Strict parsing and durable typed reporting | Complete | [PR #7](https://github.com/AIAllTheThingz/WindowsScriptRunner/pull/7) |
| 8 | Windows identity, authorization, safe report UI, and approval workflow | Complete | [PR #9](https://github.com/AIAllTheThingz/WindowsScriptRunner/pull/9) |
| 9A | Windows Service, IIS, SQL migration, and reviewed-artifact deployment tooling | Complete | [PR #10](https://github.com/AIAllTheThingz/WindowsScriptRunner/pull/10) |

Phase 6 was merged before its dependent Phase 7 work. Subsequent merged work adopted the pinned
Public-AI-Governance baseline, removed redundant code and assets, and corrected SQL and full-suite
test portability through PRs #11–#16. The current full-suite record is 752 passed, 0 failed, and 0
skipped; see the [validation report](validation-report.md).

The Phase 9 foundation is implemented, but it is not production-readiness evidence. Phase 9B now
adds guarded target-machine deployment, native IIS/Hosting Bundle preflight, certificate and SNI
verification, backup verification, and the authenticated fixed Local Host Inventory request path.
No representative Windows Server/IIS deployment, production SQL rollout, or production use is
claimed.

## Execution roadmap

### Phase 9B — close production-readiness gaps

**Status: Blocked pending the representative environment and accountable production-readiness
approval recorded in the [deployment runbook](phase-9-deployment.md).**

Use the existing [deployment runbook](phase-9-deployment.md) and deployment scripts against an
approved representative environment. Do not add product capability during this phase.

Required work:

1. Select and record the supported Windows Server, IIS, .NET Hosting Bundle, SQL Server, and host
   topology.
2. Assign operational, security, database, support, and production-approval owners.
3. Provision least-privilege Web, Worker, and database identities; verify filesystem, service, IIS,
   and SQL permissions.
4. Configure protected production settings and the approved secret source without committing
   secrets.
5. Configure HTTPS, certificate renewal, SPNs, Kerberos, and browser-zone behavior; verify expected
   and denied access.
6. Export redacted telemetry; define health signals, alerts, retention, escalation, and operator
   runbooks.
7. Rehearse SQL backup, forward migration, restore, application verification, and data-retention
   handling.
8. Provide and validate a supported authorized submission path, either a protected Web flow or a
   reviewed operator procedure, for the existing pinned package with its local-only, parameterless,
   ReadOnly/DryRun constraints, requester identity, target selection, and audit evidence.
9. Deploy an immutable release, verify Web and Worker startup, run the reviewed package end to end,
   then rehearse upgrade and rollback.
10. Run security, privacy, accessibility, failure, recovery, and capacity checks appropriate to the
   selected environment.

Exit gate:

- the exact release artifact, configuration class, and representative environment are recorded;
- an authorized submission path for the existing package is exercised end to end with its requester,
  target, audit, and typed-report evidence;
- deployment, authentication, authorization, health, alerting, backup/restore, upgrade, and rollback
  evidence pass;
- known limitations and residual risks are either resolved or explicitly accepted by accountable
  humans; and
- production-readiness approval is recorded separately from implementation completion.

### Phase 10 — controlled ReadOnly pilot

**Status: Blocked by Phase 9**

Pilot only the existing Local Host Inventory package on an explicit operational allowlist of Worker
hosts and users. Keep the package local-only, parameterless, ReadOnly, and DryRun-only. Monitor job success,
lease recovery, execution bounds, authorization denials, report availability, audit records, alerts,
and support procedures through an approved observation window.

Exit gate:

- pilot scope, owners, stop criteria, and rollback are approved;
- no unresolved high or critical finding remains without explicit risk acceptance;
- an operator completes incident and rollback exercises using the runbooks; and
- pilot evidence supports a separate release decision.

### Phase 11 — release the existing capability

**Status: Blocked by Phase 10**

Release the proven Local Host Inventory capability without expanding its execution boundary. Finalize
supported versions, installation and upgrade documentation, operational objectives, capacity and
retention limits, release notes, incident ownership, and support handoff.

Exit gate:

- release, support, security, operations, and rollback approvals identify the exact artifact and
  supported environment;
- a clean install passes and, if the artifact or configuration changed after the pilot, an upgrade
  from the pilot passes; and
- user, administrator, accessibility, and recovery documentation is reviewed and current.

## Unscheduled capability backlog

These items are deliberately outside Phases 9–11:

1. Additional reviewed local ReadOnly packages.
2. Package lifecycle and discovery beyond the compiled allowlist.
3. Additional typed report families.
4. Remote targets and credential retrieval/injection.
5. Side-effecting automation.

Each item requires its own scoped design, threat analysis, authorization model, failure and recovery
behavior, tests, and approval before it can enter the execution roadmap. Arbitrary script upload or
arbitrary command execution is not a planned product capability.

## Maintenance rule

Update this file in the same pull request as a material scope or status change. Move a milestone to
Complete only when the change is merged and its required evidence is recorded. Implementation,
validation, review, production approval, and operational verification are separate states.
