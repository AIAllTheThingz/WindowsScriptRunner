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

The current application contract accepts a structurally valid 64-character hexadecimal SHA-256 fingerprint. Phase 8 will calculate and verify the trusted fingerprint from the script version, requested phase, targets, parameters, execution window, and dry-run evidence at an authenticated boundary.

Approval policy is evaluated from the immutable policy snapshot captured from the published script at submission. Medium, High, and Critical requesters cannot self-approve; the current policy permits Low and ReadOnly self-approval. Validation precedes decision-record and status mutation. Web approval actions and authenticated principal mapping remain Phase 8 work.

## Requested phase enforcement

The current lifecycle supports submitted requests for `Validation`, `DryRun`, and `Execute`.

- `Validation` requests may move from Draft to Submitted to Validated to Completed. They cannot queue dry-run, require approval, queue execution, claim, execute, or post-validate.
- `DryRun` requests may move from Draft to Submitted to Validated to DryRunQueued to DryRunRunning to DryRunCompleted to Completed. They cannot require approval, queue execution, claim, execute, or post-validate, even when the script version also supports Execute.
- `Execute` requests are the only requests that may require approval, queue execution, be claimed, start execution attempts, enter post-validation, and record execution outcomes. Execute requests require a script version that also supports DryRun so approval and execution cannot bypass dry-run capability.

Other enum values, including `Discovery`, `Report`, and `PostValidation`, are rejected during submission until a full lifecycle is modeled for them. Undefined enum values are rejected before job creation, policy capture, transition, approval, parameter, or execution-outcome mutation.

## Lease-controlled transitions

Worker-controlled transitions are lease-aware. The current lease ID, worker ID, and fencing token are required for renewal and every transition after acquisition. The generic transition application handler cannot enter `DryRunRunning`, `DryRunCompleted`, `Claimed`, or `PostValidation`, and it cannot mutate a leased job.

| Work | Acquisition | Handler start | Successful resolution | Expiration before start | Expiration after start |
|---|---|---|---|---|---|
| DryRun | remains `DryRunQueued` with lease | `DryRunQueued -> DryRunRunning` | `DryRunRunning -> DryRunCompleted`, lease removed | lease removed; remains queued | `DryRunRunning -> TimedOut`, lease removed |
| Execute | `ExecutionQueued -> Claimed` with lease | `Claimed -> Executing` and creates attempt | terminal outcome completes attempt/job and removes lease | `Claimed -> ExecutionQueued`, lease removed | active attempt becomes `TimedOut`, lease removed |

Execute may enter `PostValidation` only through the lease-aware handler. Lease renewal changes only lease timestamps. A stale worker cannot renew, release, transition, or report success after expiration recovery or ownership change.

## Reviewed inventory completion

The local-host inventory DryRun route has a stricter successful resolution than the generic lifecycle table can express. A zero process exit code is only transport success. The bounded output must also parse and canonicalize as the expected typed report.

Application then commits the immutable `JobReport`, `DryRunCompleted` job state, lease removal, and audit event atomically. A report already associated with the job blocks replay. Invalid output, a stale lease, a pinned-version mismatch, or persistence failure cannot produce a completed job without its corresponding typed report.
