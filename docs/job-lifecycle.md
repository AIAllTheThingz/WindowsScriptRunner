# Job lifecycle

`Job` owns status changes. Callers cannot assign `JobStatus`, skip required states, transition to the same state, or leave a terminal state. There is no public generic status transition method. Every successful transition receives an acting user and UTC timestamp and updates `LastActingUser` and `UpdatedUtc`.

## Normal transitions

| Current | Next |
|---|---|
| Draft | Submitted |
| Submitted | Validated |
| Validated | DryRunQueued, or Completed through validation-only completion |
| DryRunQueued | DryRunRunning |
| DryRunRunning | DryRunCompleted |
| DryRunCompleted | AwaitingApproval for Execute requests, or Completed for DryRun-only requests and the trusted read-only rule |
| AwaitingApproval | Approved or Rejected |
| Approved | ExecutionQueued |
| ExecutionQueued | Claimed |
| Claimed | Executing |
| Executing | PostValidation, Completed, or CompletedWithWarnings |
| PostValidation | Completed or CompletedWithWarnings |

Dedicated rules permit `Validated -> Completed` for validation-only requests and `DryRunCompleted -> Completed` for dry-run-only requests. A separate trusted read-only rule permits `DryRunCompleted -> Completed` only for `ReadOnly` work when the captured policy says the version has no Execute phase.

Submitted is reachable only through `Submit`; Approved and Rejected only through evidence-recording decision operations; Executing only through creation of an execution attempt; and CompletedWithWarnings only through a terminal execution outcome. Completed is also reachable through the dedicated validation-only, dry-run-only, and trusted read-only completion rules above. The retained application transition command exposes only an explicit allowlist of non-protected operational transitions.

Appropriate non-terminal states may end as Failed, Cancelled, TimedOut, Blocked, or NotRun. If an execution attempt is active, those terminal outcomes must be recorded with the execution-outcome operation so the attempt and job reach terminal state together. Rejected is reachable only from AwaitingApproval. Failure is never represented as Completed.

## Terminal states

Completed, CompletedWithWarnings, Failed, Rejected, Cancelled, TimedOut, Blocked, and NotRun are terminal.

## Approval fingerprint

Phase 2 accepts a supplied 64-character hexadecimal SHA-256 fingerprint. A future implementation will bind it to the script version, requested phase, targets, parameters, execution window, and dry-run evidence.

Approval policy is evaluated from the immutable policy snapshot captured from the published script at submission. Medium, High, and Critical requesters cannot self-approve; the documented Phase 2 policy permits Low and ReadOnly self-approval. Validation precedes decision-record and status mutation.

## Requested phase enforcement

Phase 2 supports submitted requests for `Validation`, `DryRun`, and `Execute`.

- `Validation` requests may move from Draft to Submitted to Validated to Completed. They cannot queue dry-run, require approval, queue execution, claim, execute, or post-validate.
- `DryRun` requests may move from Draft to Submitted to Validated to DryRunQueued to DryRunRunning to DryRunCompleted to Completed. They cannot require approval, queue execution, claim, execute, or post-validate, even when the script version also supports Execute.
- `Execute` requests are the only requests that may require approval, queue execution, be claimed, start execution attempts, enter post-validation, and record execution outcomes. Execute requests require a script version that also supports DryRun so approval and execution cannot bypass dry-run capability.

Other enum values, including `Discovery`, `Report`, and `PostValidation`, are rejected during submission until a full lifecycle is modeled for them. Undefined enum values are rejected before job creation, policy capture, transition, approval, parameter, or execution-outcome mutation.
