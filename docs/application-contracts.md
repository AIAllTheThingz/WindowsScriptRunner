# Application contracts

## Commands and query

- `CreateDraftJobCommand` creates, audits, and commits a draft.
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

`IJobRepository`, `IScriptDefinitionRepository`, `IWorkerNodeRepository`, and `ICredentialReferenceRepository` are domain-specific async interfaces. `ICredentialReferenceRepository` is used only to verify that a supplied secure parameter references an existing enabled credential reference; it does not return or store raw credential values. `IAuditWriter` records audit events, `IUnitOfWork` defines the commit boundary, `IClock` supplies UTC time, `ICurrentUser` represents an actor, and `IJobFingerprintService` defines future approval fingerprint creation.

No generic repository or SQL terminology is exposed. Phase 3 will provide Infrastructure implementations and persistence mappings.

## DTOs and sensitive values

Contracts uses immutable records, GUIDs at the external boundary, string enum names, and `DateTimeOffset`. `StartExecutionAttemptRequest` carries the job ID and optional worker-node ID required to enter Executing without exposing domain entities. Contracts does not reference Domain or expose domain entities. `JobParameterResponse` reports whether a value is redacted; application mapping always replaces a sensitive serialized value with `[REDACTED]` based on the pinned immutable `ScriptParameterDefinition`, never on duplicated job-parameter metadata.

Serialized `StringArray` parameter values use a JSON array of strings, for example `["server-01","server-02"]`. This representation is validated in Domain but is not passed to PowerShell in Phase 2. `SecureReference` values must use canonical GUID `D` format and represent `CredentialReferenceId`; arbitrary strings and raw secret-shaped values are rejected before storage.

`JobParameter` stores only explicit binding data: parameter name and a present serialized value. Future persistence must reconstruct only this binding data and validate it against the pinned immutable `ScriptVersion` before any value is exposed. Absent values are represented by no binding, never by a null, empty, or whitespace `JobParameter`.

Domain and application boundaries reject undefined enum values before they can be captured into policy snapshots, job requests, parameters, approvals, execution outcomes, or operational transitions. Contract DTOs continue to expose enum names as strings; parsing and validation are expected at the eventual API boundary.
