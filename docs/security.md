# Phase 2 security properties

- No raw credential property exists in the domain model; `CredentialReference` stores only an external identifier.
- `SecureReference` job parameters must contain a canonical non-empty `CredentialReferenceId` GUID. Application handlers resolve the ID, reject missing or disabled references, store only the canonical ID, and never audit external vault identifiers.
- Null, empty, and whitespace are one absent-value representation. The pinned definition accepts or rejects absence before type parsing or credential lookup. Accepted absence removes the explicit draft binding; required absence without a default leaves the job unchanged and performs no credential lookup, persistence update, success audit, or commit.
- Parameter defaults remain immutable `ScriptParameterDefinition` metadata. Clearing an override never copies a default into `JobParameter`, and clear audit data never contains a prior value or credential-reference ID.
- Sensitive job parameters redact `ToString`, query responses, and audit values.
- `JobParameter` stores only name/value binding data. Parameter type, sensitivity, SecureReference classification, value validation, audit classification, and response redaction derive from the pinned immutable `ScriptParameterDefinition`.
- Parameter audit events store bounded metadata such as parameter name, pinned type, pinned sensitivity, value-present flag, and serialized length. Full parameter values are not written to audit properties.
- Credential external identifiers must be provider-scoped reference URIs whose scheme matches the provider type and which contain an authority and path. User information, query strings, fragments, unlabelled values, and obvious embedded-secret markers are rejected.
- Draft, submitted, approved, executing, and terminal job responses derive redaction from the pinned script version. Inconsistent or corrupted parameter bindings fail closed and do not expose raw values.
- Script paths must be relative and reject rooted or traversal forms.
- Script SHA-256 metadata must contain exactly 64 hexadecimal characters.
- Published script versions reject mutation.
- Published Execute-capable script versions must also support DryRun; Execute job submissions enforce the same invariant defensively before policy capture.
- Web has no direct reference to Worker or PowerShell.
- Domain references no solution, ASP.NET Core, Entity Framework Core, or SQL client assembly.
- Submission captures trusted script risk plus Execute- and PostValidation-phase capabilities in an immutable job policy snapshot only after rejecting undefined risk and phase enum values.
- New submissions require the selected script definition to be enabled and the selected version to be published. Disabling a script later prevents new submissions; already-submitted jobs keep their captured Phase 2 policy until future runtime governance is implemented.
- Submitted jobs enforce the requested phase: Validation stops after validation, DryRun stops after dry-run, and only Execute requests may enter approval/execution states.
- Requesters cannot self-approve Medium, High, or Critical work, and callers cannot lower risk at approval time. The documented Phase 2 policy permits ReadOnly and Low self-approval.
- Windows user identities compare with ordinal case-insensitive equality so casing cannot bypass self-approval checks. Future authentication should map users to stable SIDs or equivalent principal identifiers.
- Read-only completion requires captured ReadOnly risk and a captured absence of Execute support; callers cannot override either value.
- Approval, rejection, execution, and completion states require dedicated evidence-bearing operations; the generic application transition handler rejects protected targets and refuses to terminalize jobs with active execution attempts.
- Execution outcomes complete the active `JobExecution` and terminalize the parent `Job` in one aggregate operation, preventing orphaned active attempts.
- Aggregate actor, timestamp, lifecycle, and child-object validation occurs before mutation.
- `ScriptDefinition.UpdateDetails` validates both proposed text fields before assigning either, preventing partial field changes when validation fails.
- Source project files are parsed to enforce exact project-reference allowlists, including explicit Web-to-Worker and Web-to-PowerShell prohibitions.
- Strong identifiers are immutable reference records with no public parameterless constructor; aggregate boundaries reject null identifiers.
- Script definitions reject duplicate semantic versions and duplicate `ScriptVersionId` values before mutation.
- Audit properties reject control characters and obvious sensitive key names.

Authentication, authorization, executable signing, trusted hash calculation, SQL security, external credential retrieval, process isolation, runtime cancellation policy for already-approved jobs after script disable, and production approval controls are not implemented.
