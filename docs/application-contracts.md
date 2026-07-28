# Application contracts

## Commands and query

- `CreateDraftJobCommand` creates, audits, and commits a draft.
- `AddJobTargetCommand` loads a draft and delegates target invariants to Domain.
- `SetJobParameterCommand` locates the published definition, validates the serialized value, and redacts sensitive audit data.
- `SubmitJobCommand` validates targets, phase support, and required/typed parameters.
- `TransitionJobCommand` delegates all lifecycle enforcement to Domain.
- `GetJobQuery` maps a job to `JobDetailResponse` without exposing sensitive values.

## Boundaries

`IJobRepository`, `IScriptDefinitionRepository`, `IWorkerNodeRepository`, and `ICredentialReferenceRepository` are domain-specific async interfaces. `IAuditWriter` records audit events, `IUnitOfWork` defines the commit boundary, `IClock` supplies UTC time, `ICurrentUser` represents an actor, and `IJobFingerprintService` defines future approval fingerprint creation.

No generic repository or SQL terminology is exposed. Phase 3 will provide Infrastructure implementations and persistence mappings.

## DTOs and sensitive values

Contracts uses immutable records, GUIDs at the external boundary, string enum names, and `DateTimeOffset`. Contracts does not reference Domain or expose domain entities. `JobParameterResponse` reports whether a value is redacted; application mapping always replaces a sensitive serialized value with `[REDACTED]`.

Serialized `StringArray` parameter values use a JSON array of strings, for example `["server-01","server-02"]`. This representation is validated in Domain but is not passed to PowerShell in Phase 2.
