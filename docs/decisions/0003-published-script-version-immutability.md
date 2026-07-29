# ADR 0003: Published script-version immutability

Status: Accepted

A published `ScriptVersion` cannot accept parameter-definition changes. Script identity, path, hash, supported phases, timeout, report formats, and PowerShell requirement are constructor-only. Script versions reject undefined phase and report-format enum values, and Execute-capable versions must also support DryRun before publication.

Corrections require a new semantic version. This preserves the meaning of submitted jobs and future approval fingerprints.

At submission, `Job` captures the trusted script-definition ID, version ID, risk, Execute support, and PostValidation support in an immutable policy snapshot after validating that the risk and requested phase are defined enum values. Later approval, read-only completion, and post-validation entry decisions use this snapshot rather than caller-supplied policy values. Persistence reconstruction of this snapshot remains Phase 3 work.

The same pinned version is the sole authority for parameter type, sensitivity, allowed values, required status, and SecureReference classification. `JobParameter` stores only a parameter-name/serialized-value binding, and application response mapping must load the pinned version before exposing or redacting values. Persistence must not introduce duplicated authoritative parameter metadata.

Canonical absence is null, empty, or whitespace. The pinned definition decides whether absence is valid before any type parsing or credential-reference lookup. Valid absence removes the explicit draft binding so a version-owned default can apply naturally; defaults are never copied into `JobParameter`. Only present SecureReference values are parsed and resolved, and clear audit events omit prior identifiers and values.

Submission also requires the script definition to be enabled at that moment. Disabling a script definition later prevents new submissions but does not retroactively cancel already-submitted jobs in Phase 2; runtime governance for approved or queued jobs after disable is deferred.
