# Approval fingerprints

An approval fingerprint is trusted review evidence, not a browser-generated approval claim. Phase 8 implements `IJobFingerprintService` with `ApprovalFingerprintService` in Application. Its canonical format version is `windows-script-runner-approval-v2` and its output is one lowercase 64-character SHA-256 value.

## Preconditions

The service fails with a bounded application conflict unless all of the following are current:

- the job is `AwaitingApproval` and requested `Execute`;
- the immutable policy snapshot matches the current pinned definition and version;
- the version remains published; and
- immutable accepted `JobDryRunEvidence` is present for a completed `DryRun`.

## Bound data

The canonical byte sequence length-prefixes every named field and includes:

- format version; job identity; stable requester; requested phase and status; creation, submission, and update times; description; and change reference;
- script definition/version IDs, semantic version, relative path, SHA-256, commit, minimum PowerShell version, timeout, and sorted supported phases;
- captured policy definition/version IDs, risk, Execute capability, and post-validation capability;
- targets sorted case-insensitively then ordinally, including target name, add time, and stable adding actor;
- parameters sorted the same way, including exact stored serialized values; and
- accepted DryRun evidence: work kind, trusted lifecycle source, worker/lease/fencing provenance when the lifecycle was leased, and execution-window opened/completed UTC timestamps.

This binds the reviewed script version, requested phase, targets, parameters, execution window, and accepted dry-run evidence. A changed value produces a different fingerprint. Sensitive and SecureReference values can influence the internal calculation when they are part of the pinned job binding, but they are never displayed, logged, audited, or returned by the fingerprint service.

## Decision-time verification

The browser echoes the fingerprint rendered during review. On approval or rejection, Application recomputes the current fingerprint from persisted trusted data and requires two lowercase hexadecimal 64-character values to match using `CryptographicOperations.FixedTimeEquals`. The browser cannot supply a replacement calculation, actor, policy, target set, parameter interpretation, or acceptance evidence.

The expected fingerprint is returned only in the protected approval-review contract. It is an integrity/concurrency token, not a credential or authorization grant. Antiforgery, authenticated identity, policy/resource authorization, job state, and Domain invariants remain independently required.
