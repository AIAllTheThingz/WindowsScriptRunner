# Job lifecycle

`Job` owns status changes. Callers cannot assign `JobStatus`, skip required states, transition to the same state, or leave a terminal state. Every successful transition receives an acting user and UTC timestamp and updates `LastActingUser` and `UpdatedUtc`.

## Normal transitions

| Current | Next |
|---|---|
| Draft | Submitted |
| Submitted | Validated |
| Validated | DryRunQueued |
| DryRunQueued | DryRunRunning |
| DryRunRunning | DryRunCompleted |
| DryRunCompleted | AwaitingApproval |
| AwaitingApproval | Approved or Rejected |
| Approved | ExecutionQueued |
| ExecutionQueued | Claimed |
| Claimed | Executing |
| Executing | PostValidation, Completed, or CompletedWithWarnings |
| PostValidation | Completed or CompletedWithWarnings |

A dedicated rule permits `DryRunCompleted -> Completed` only for `ReadOnly` work when the version has no Execute phase.

Appropriate non-terminal states may end as Failed, Cancelled, TimedOut, Blocked, or NotRun. Rejected is reachable only from AwaitingApproval. Failure is never represented as Completed.

## Terminal states

Completed, CompletedWithWarnings, Failed, Rejected, Cancelled, TimedOut, Blocked, and NotRun are terminal.

## Approval fingerprint

Phase 2 accepts a supplied 64-character hexadecimal SHA-256 fingerprint. A future implementation will bind it to the script version, requested phase, targets, parameters, execution window, and dry-run evidence.
