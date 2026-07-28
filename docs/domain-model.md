# Domain model

## Model

```mermaid
classDiagram
    ScriptDefinition "1" *-- "*" ScriptVersion
    ScriptVersion "1" *-- "*" ScriptParameterDefinition
    Job "1" *-- "*" JobTarget
    Job "1" *-- "*" JobParameter
    Job "1" *-- "*" JobExecution
    Job "1" *-- "*" JobApproval
    WorkerNode "1" *-- "*" WorkerCapability
    Job --> ScriptDefinitionId
    Job --> ScriptVersionId
```

`ScriptDefinition` owns version identity and uniqueness. Each `ScriptVersion` owns typed parameter definitions, phases, report formats, hash metadata, publication, and immutability.

`Job` owns targets, supplied parameters, execution attempts, approval decisions, draft protection, submission validation, and lifecycle transitions. Public collections are read-only views.

`WorkerNode` owns capabilities and monotonic heartbeat state. `AuditEvent` defensively copies non-sensitive properties. `CredentialReference` represents only an external provider reference and never contains a raw credential.

Strong GUID-backed IDs prevent accidental identifier mixing. `ScriptName`, `ScriptVersionNumber`, `UserIdentity`, `TargetName`, `ChangeReference`, and `WorkerCapability` centralize validation and equality.

The Domain project owns no persistence, ASP.NET, SQL, PowerShell, file-system, Git, or serialization concerns. Persistence reconstruction patterns and storage mappings remain Phase 3 work.
