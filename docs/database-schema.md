# Database schema

The initial migration creates the `wsr` schema and uses `wsr.__EFMigrationsHistory` for migration history.

## Aggregate tables

- `ScriptDefinitions` with owned `ScriptVersions`, `ScriptVersionPhases`, `ScriptVersionReportFormats`, `ScriptParameterDefinitions`, and `ScriptParameterAllowedValues`
- `Jobs` with owned `JobTargets`, `JobParameters`, `JobExecutions`, and `JobApprovals`
- `WorkerNodes` with owned `WorkerCapabilities`
- `CredentialReferences`
- append-only `AuditEvents` with `AuditEventProperties`

Foreign keys preserve aggregate ownership and use explicit delete behavior. Script-to-version, version-to-child, job-to-child, worker-to-capability, and audit-to-property relationships cascade only within their aggregate. Jobs use a composite `(ScriptDefinitionId, ScriptVersionId)` foreign key to ensure the pinned version belongs to the pinned script. Job-to-script, job-to-version, and execution-to-worker relationships use `NO ACTION`, so historical jobs cannot disappear through cross-aggregate deletion.

Unique indexes enforce semantic version uniqueness per script, child-name uniqueness, normalized worker-name uniqueness, credential provider/reference uniqueness, and audit event identity. A filtered unique index permits at most one active execution for each job.

Operational indexes cover job status/update time, job creation time, requester/creation time, pinned script IDs, approval decision time, worker enabled/heartbeat state, and audit queries by time, entity, actor, and event type. Indexes are limited to existing repository and audit access patterns; no Phase 4 claiming or dashboard indexes are added.

Mutable aggregate roots use SQL Server `rowversion` columns. Check constraints enforce defined enum ranges, bounded temporal ordering, policy consistency, execution completion consistency, and other invariants that can be represented declaratively. Triggers enforce cross-table publication rules: a published Execute-capable script version must also support DryRun, and parameter allowed values are restricted to Enum definitions.

Database constraints are defense in depth. Domain rules remain authoritative for valid lifecycle operations.

## Phase 4 lease schema

`wsr.JobLeases` is a one-to-one owned child of `wsr.Jobs`; `JobId` is its primary key. `LeaseId` is independently unique. The row stores `WorkerNodeId`, `WorkKind`, `FencingToken`, `AcquiredUtc`, `LastRenewedUtc`, `ExpiresUtc`, and a SQL `rowversion`.

The Job foreign key cascades so aggregate deletion cannot orphan a lease. The Worker foreign key uses `NO ACTION`, preventing deletion of a worker that owns leases. Checks require non-empty IDs, `DryRun` or `Execute`, a positive fencing token, `LastRenewedUtc >= AcquiredUtc`, and `ExpiresUtc > LastRenewedUtc`. Indexes cover `ExpiresUtc`, `(WorkerNodeId, ExpiresUtc)`, `(WorkKind, ExpiresUtc)`, and unique `LeaseId`.

`wsr.JobLeaseFencingSequence` starts at 1 and increments globally. Gaps are expected because sequence allocation is not rolled back; monotonic uniqueness, not density, is the safety property. Candidate discovery uses the existing Jobs status/update index plus lease absence and returns a bounded projection ordered by `UpdatedUtc`, `CreatedUtc`, then `JobId`.
