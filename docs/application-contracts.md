# Application contracts

## Commands and query

- `CreateDraftJobCommand` creates, audits, and commits a draft.
- `AddJobTargetCommand` loads a draft and delegates target invariants to Domain.
- `SetJobParameterCommand` locates the published definition, validates the serialized value, resolves enabled credential references for `SecureReference` parameters, stores only canonical credential-reference IDs, and writes bounded audit metadata instead of raw values.
- `SubmitJobCommand` validates targets, enabled script definition, published version, Phase 2 requested-phase support, and required/typed parameters, then captures trusted script policy.
- `TransitionJobCommand` is restricted to explicitly enumerated operational transitions that require no separate evidence. It rejects Submitted, Approved, Rejected, Executing, Completed, and CompletedWithWarnings.
- `ApproveJobCommand` and `RejectJobCommand` record a structurally validated fingerprint, optional comment, and actor through dedicated aggregate operations. They contain no caller-selected risk.
- `CompleteReadOnlyJobCommand` invokes the dedicated trusted read-only completion rule and contains no caller-selected risk or Execute capability.
- `CompleteValidationJobCommand` and `CompleteDryRunJobCommand` complete requested validation-only and dry-run-only work through explicit operations. They contain no arbitrary target status.
- `GetJobQuery` maps a job to `JobDetailResponse` without exposing sensitive values.

Handlers load required entities, validate related references, perform the domain operation, construct safe audit events, update repositories, write success audit events, and commit only after the operation succeeds. Domain or application validation failures therefore do not produce misleading success audit records or commits.

## Boundaries

`IJobRepository`, `IScriptDefinitionRepository`, `IWorkerNodeRepository`, and `ICredentialReferenceRepository` are domain-specific async interfaces. `ICredentialReferenceRepository` is used only to verify that a supplied secure parameter references an existing enabled credential reference; it does not return or store raw credential values. `IAuditWriter` records audit events, `IUnitOfWork` defines the commit boundary, `IClock` supplies UTC time, `ICurrentUser` represents an actor, and `IJobFingerprintService` defines future approval fingerprint creation.

No generic repository or SQL terminology is exposed. Phase 3 will provide Infrastructure implementations and persistence mappings.

## DTOs and sensitive values

Contracts uses immutable records, GUIDs at the external boundary, string enum names, and `DateTimeOffset`. Contracts does not reference Domain or expose domain entities. `JobParameterResponse` reports whether a value is redacted; application mapping always replaces a sensitive serialized value with `[REDACTED]`.

Serialized `StringArray` parameter values use a JSON array of strings, for example `["server-01","server-02"]`. This representation is validated in Domain but is not passed to PowerShell in Phase 2. `SecureReference` values must use canonical GUID `D` format and represent `CredentialReferenceId`; arbitrary strings and raw secret-shaped values are rejected before storage.
