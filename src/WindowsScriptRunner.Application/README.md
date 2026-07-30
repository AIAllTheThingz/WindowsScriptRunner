# Application

Contains Phase 2 use-case handlers plus focused repository, credential-reference, audit, clock, current-user, fingerprint, and unit-of-work abstractions. Handlers validate related references, derive job-parameter classification and absent-value policy from pinned script definitions for writes and reads, skip credential lookup for accepted absence, emit bounded clear/set audit events, route active execution completion through a dedicated outcome handler, and leave persistence implementations intentionally absent.

Phase 7 adds the package-specific `CompleteLocalHostInventoryDryRunHandler`, `IJobReportRepository`, and bounded typed report queries. The completion handler revalidates the pinned reviewed package and current fenced lease, recognizes only exact deterministic replays, stages one immutable typed report, completes the ReadOnly DryRun, resolves the lease, writes non-inventory audit metadata, and commits once. Its command accepts a Reporting-validated typed value and exposes no arbitrary JSON, schema, report type, risk, sensitivity, or terminal status.

`GetLocalHostInventoryReportHandler` retrieves one report by report ID or job ID and maps it to the immutable Contracts DTO. Web does not compose this query until identity and authorization exist.
