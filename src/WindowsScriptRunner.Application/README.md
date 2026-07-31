# Application

Application owns use-case orchestration and the abstractions required by those use cases. It references Domain and Contracts but contains no EF Core, SQL Server, ASP.NET, Worker, PowerShell, or filesystem implementation.

Responsibilities include:

- script, job, parameter, approval, and execution commands;
- pinned script-version validation and safe DTO mapping;
- credential-reference existence checks without secret retrieval;
- worker registration, heartbeat, fenced lease, queue, and recovery handlers;
- audit, clock, authenticated-current-user, trusted fingerprint, repository, and unit-of-work abstractions; and
- package-specific atomic Local Host Inventory report completion and typed report queries.

Handlers validate before staging mutations, write only bounded non-sensitive audit metadata, and commit through `IUnitOfWork`. Sensitive parameter classification comes from the pinned immutable `ScriptParameterDefinition`, never from stored job bindings.

`CompleteLocalHostInventoryDryRunHandler` revalidates the exact reviewed package and current fenced lease, recognizes only exact deterministic replays, stages one immutable typed report, completes the ReadOnly DryRun, removes the lease, writes non-inventory audit metadata, and commits once. It accepts a Reporting-validated typed value and exposes no arbitrary JSON, schema, report type, risk, sensitivity, or generic terminal status.

`GetLocalHostInventoryReportHandler` retrieves one report by report ID or job ID and maps it to the immutable Contracts DTO. Phase 8 adds 1–100 bounded typed report and awaiting-approval queries for the protected Web adapter; it does not add a generic repository, raw output query, or persistence bypass.

`ApprovalFingerprintService` computes versioned canonical SHA-256 evidence from trusted persisted job, pinned script/policy, target, parameter, and execution data. `ApproveJobHandler` and `RejectJobHandler` take no caller actor: they use `ICurrentUser`, recompute the fingerprint, delegate state and separation-of-duties rules to Domain, stage bounded audit data, and commit through the existing unit of work.

See [Application contracts](../../docs/application-contracts.md).
