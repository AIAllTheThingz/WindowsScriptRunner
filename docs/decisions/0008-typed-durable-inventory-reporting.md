# ADR 0008: Typed durable Local Host Inventory reporting

- Status: Accepted
- Date: 2026-07-30

## Context

Phase 6 introduced one reviewed production package, `windows.local-host-inventory` version `1.0.0`. Its successful PowerShell stdout was bounded but only superficially classified and then discarded. Worker coordination is at least once, so an uncertain database result or concurrent dispatch retry can repeat completion. Authentication and authorization do not yet exist.

The design must durably retain the trusted inventory without turning Reporting into a generic engine, weakening the process boundary, leaking raw output, or completing the job separately from report persistence.

## Decision

Reporting is an independent focused project. A dedicated parser accepts a narrow complete process-result abstraction and validates successful, untruncated execution, whitespace-only stderr, strict UTF-8 size, exact duplicate-free JSON schema, bounded safe strings, supported architecture and versions, and collection time against the process window. Only a fully validated immutable typed result crosses to Application.

Domain contains one immutable `JobReport` envelope and one typed `LocalHostInventoryReportPayload`. The envelope carries job/script/package/schema/format identity, worker/lease/fencing provenance, PowerShell execution ID, SQL-created and collected timestamps, typed detail, and a canonical SHA-256.

Application owns one trusted write command. It revalidates the current fenced DryRun lease with SQL time and the exact pinned Phase 6 definition/version, derives deterministic report identity, stages the report, completes the ReadOnly job, removes the lease through Domain invariants, appends bounded audit metadata, and commits all records through the existing unit of work.

Report identity is deterministic from job/package/schema. The canonical digest covers stable provenance and every typed value but excludes the SQL persistence timestamp so an uncertain commit retry has identical comparison material. An existing report is idempotent success only when job state, lease absence, script/job/worker/lease/fencing/execution provenance, collection time, typed payload, and digest all match. Conflicts fail closed and reports are never updated.

Infrastructure persists two tables: `JobReports` and required one-to-one `LocalHostInventoryReports`. SQL constraints repeat exact metadata, type, identifier, timestamp, architecture, digest, and uniqueness invariants. There is no generic payload table.

The typed read path returns one immutable Contracts DTO by report ID or job ID. Web exposure is deferred until identity, authentication, and authorization exist.

## Consequences

- A valid execution creates at most one accepted durable typed report even when completion is retried.
- Report persistence, job completion, lease deletion, and audit insertion are atomic.
- Raw stdout, stderr, rejected values, hash input material, and arbitrary JSON are not persisted or audited.
- Worker does not parse JSON or own report persistence rules; PowerShell remains the only process-execution project.
- The schema and parser support only Local Host Inventory `1.0.0` schema `1.0`.
- PowerShell execution remains at least once; this ADR does not claim exactly-once external execution.
- No CSV, HTML, text, upload, user schema, generic reporting, report mutation, or public download surface is introduced.
- Phase 8 must add identity, authentication, authorization, and approval workflow before any Web report presentation.
