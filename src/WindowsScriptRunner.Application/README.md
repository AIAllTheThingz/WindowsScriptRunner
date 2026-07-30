# Application

Application owns use-case orchestration and the abstractions required by those use cases. It references Domain and Contracts but contains no EF Core, SQL Server, ASP.NET, Worker, PowerShell, or filesystem implementation.

Responsibilities include:

- script, job, parameter, approval, and execution commands;
- pinned script-version validation and safe DTO mapping;
- credential-reference existence checks without secret retrieval;
- worker registration, heartbeat, fenced lease, queue, and recovery handlers;
- audit, clock, current-user, fingerprint, repository, and unit-of-work abstractions; and
- package-specific atomic Local Host Inventory report completion and typed report queries.

Handlers validate before staging mutations, write only bounded non-sensitive audit metadata, and commit through `IUnitOfWork`. Sensitive parameter classification comes from the pinned immutable `ScriptParameterDefinition`, never from stored job bindings.

`CompleteLocalHostInventoryDryRunHandler` revalidates the exact reviewed package and current fenced lease, recognizes only exact deterministic replays, stages one immutable typed report, completes the ReadOnly DryRun, removes the lease, writes non-inventory audit metadata, and commits once. It accepts a Reporting-validated typed value and exposes no arbitrary JSON, schema, report type, risk, sensitivity, or generic terminal status.

`GetLocalHostInventoryReportHandler` retrieves one report by report ID or job ID and maps it to the immutable Contracts DTO. Web does not compose this query until Phase 8 identity and authorization exist.

See [Application contracts](../../docs/application-contracts.md).
