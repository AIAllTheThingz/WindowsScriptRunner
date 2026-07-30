# Application contracts

## Commands and query

- `CreateDraftJobCommand` verifies that the selected version belongs to the script, that the requested phase is supported by the Phase 2 lifecycle and that version, and that Execute capability includes DryRun before it creates, audits, and commits a draft.
- `AddJobTargetCommand` loads a draft and delegates target invariants to Domain.
- `SetJobParameterCommand` locates the pinned script version and exact `ScriptParameterDefinition` before interpreting input. Null, empty, and whitespace input is canonical absence: the pinned definition first accepts or rejects that absence, an accepted clear removes the explicit binding, and no type parsing or credential lookup occurs. Present values are validated against the pinned definition; present `SecureReference` values must be canonical non-empty IDs that resolve to enabled credential references. Stored data remains only the canonical name/value binding.
- `SubmitJobCommand` validates targets, enabled script definition, published version, Phase 2 requested-phase support, Execute-with-DryRun support, and required/typed parameters, then captures trusted script policy.
- `TransitionJobCommand` is restricted to explicitly enumerated operational transitions that require no separate evidence. It rejects Submitted, Approved, Rejected, Executing, Completed, and CompletedWithWarnings. If a job is Executing, PostValidation, or has an active execution attempt, terminal statuses must be recorded through `RecordExecutionOutcomeCommand` instead of the generic transition command.
- `ApproveJobCommand` and `RejectJobCommand` record a structurally validated fingerprint, optional comment, and actor through dedicated aggregate operations. They contain no caller-selected risk.
- `CompleteReadOnlyJobCommand` invokes the dedicated trusted read-only completion rule and contains no caller-selected risk or Execute capability.
- `CompleteValidationJobCommand` and `CompleteDryRunJobCommand` complete requested validation-only and dry-run-only work through explicit operations. They contain no arbitrary target status.
- `StartExecutionAttemptCommand` validates an optional worker reference and starts the single execution attempt that moves a claimed Execute job into Executing.
- `RecordExecutionOutcomeCommand` completes the single active execution attempt and moves the job to the matching terminal state as one aggregate operation. This prevents orphaned active attempts when execution work ends as success, warning, failure, cancellation, timeout, blocked, or not-run.
- `GetJobQuery` maps a job to `JobDetailResponse` by loading the pinned script definition/version and deriving parameter type, sensitivity, and redaction from the pinned definitions. Inconsistent or corrupted parameter bindings fail closed without returning raw values.

Handlers load required entities, validate related references, perform the domain operation, construct safe audit events, update repositories, write success audit events, and commit only after the operation succeeds. Domain or application validation failures therefore do not produce misleading success audit records or commits.

Accepted clears write `JobParameterCleared` rather than misleading set semantics. The bounded properties are parameter name, pinned type, pinned sensitivity, whether an explicit binding existed, and false value/reference-present flags. The prior serialized value, credential-reference ID, caller whitespace, external identifier, and vault path are never included. A clear command is an intentional draft mutation and updates actor/timestamp even when no binding existed. Required absence without a default fails before repository update, audit, commit, or credential lookup. Clearing a required parameter with a definition-owned default removes only the explicit override; Application does not copy the default into `JobParameter`.

## Boundaries

`IJobRepository`, `IScriptDefinitionRepository`, `IWorkerNodeRepository`, and `ICredentialReferenceRepository` are domain-specific async interfaces. `ICredentialReferenceRepository` is used only to verify that a supplied secure parameter references an existing enabled credential reference; it does not return or store raw credential values. `IAuditWriter` records audit events, `IUnitOfWork` defines the commit boundary, `IClock` supplies process-local time for scheduling and bounded elapsed windows, `IWorkerCoordinationClock` supplies shared authoritative time for persisted job and distributed worker state, `ICurrentUser` represents an actor, and `IJobFingerprintService` defines future approval fingerprint creation.

No generic repository or SQL terminology is exposed through Application. Phase 3 provides SQL Server implementations entirely in Infrastructure. Repository methods load or stage tracked aggregate graphs and propagate cancellation; they do not commit. The scoped `IUnitOfWork` performs the one atomic commit for both aggregate and audit changes.

`IJobRepository.UpdateLeaseAsync` is the renewal-only staging path. It synchronizes the existing lease timestamps without dirtying the unchanged job row; other job mutations continue through the aggregate update path.

## DTOs and sensitive values

Contracts uses immutable records, GUIDs at the external boundary, string enum names, and `DateTimeOffset`. `StartExecutionAttemptRequest` carries the job ID and optional worker-node ID required to enter Executing without exposing domain entities. Contracts does not reference Domain or expose domain entities. `JobParameterResponse` reports whether a value is redacted; application mapping always replaces a sensitive serialized value with `[REDACTED]` based on the pinned immutable `ScriptParameterDefinition`, never on duplicated job-parameter metadata.

Serialized `StringArray` parameter values use a JSON array of strings, for example `["server-01","server-02"]`. This representation is validated in Domain but is not passed to PowerShell in Phase 2. `SecureReference` values must use canonical GUID `D` format and represent `CredentialReferenceId`; arbitrary strings and raw secret-shaped values are rejected before storage.

`JobParameter` stores only explicit binding data: parameter name and a present serialized value. Persistence reconstructs only this binding data; Application continues to validate it against the pinned immutable `ScriptVersion` before any value is exposed. Absent values are represented by no binding, never by a null, empty, or whitespace `JobParameter`.

Domain and application boundaries reject undefined enum values before they can be captured into policy snapshots, job requests, parameters, approvals, execution outcomes, or operational transitions. Contract DTOs continue to expose enum names as strings; parsing and validation are expected at the eventual API boundary.

## Worker and queue contracts

`RegisterWorkerHandler` creates or loads the configured `WorkerNode`, requires an exact persisted name match, rejects disabled nodes, atomically synchronizes the complete capability set, records a heartbeat, audits only creation or capability changes, and commits once. `RecordWorkerHeartbeatHandler` loads and validates the node, records a monotonic heartbeat, and commits without audit noise.

Persisted job mutations, worker registration, heartbeat, lease mutation, leased lifecycle validation, and expiration discovery obtain their timestamps through `IWorkerCoordinationClock`. Infrastructure implements it with SQL Server UTC so queue entry and ownership decisions do not compare timestamps produced by different application or worker host clocks.

`IJobQueueCandidateSource` accepts the supported work-kind set, a bounded count, and the current time. It returns only `JobId`, `JobWorkKind`, `CreatedUtc`, and `UpdatedUtc`. `IExpiredJobLeaseCandidateSource` returns bounded expired lease identifiers and fenced credentials. `IFencingTokenSource` supplies a positive monotonic token from SQL Server.

Lease acquisition, renewal, unstarted release, expiration recovery, inspection, dry-run start/completion, execution start, post-validation entry, and execution terminalization have explicit commands and handlers. Every worker-controlled mutation after acquisition requires `JobLeaseCredentials` containing the lease ID, worker ID, and fencing token. Stale or mismatched credentials fail before mutation, audit, or commit. Terminal handlers retry a bounded concurrency conflict only when the job row is unchanged and the same fenced credentials still own the renewed lease.

`ClaimedJobWork` contains only the job/work identifiers, worker/lease identifiers, fencing token, and expiration. It never contains parameters or credential-reference IDs. `IJobWorkHandler` receives that descriptor and cancellation. A successful handler must explicitly resolve its lease through a lifecycle completion or safe unstarted release; merely returning is an invariant violation.
