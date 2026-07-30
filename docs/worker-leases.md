# Worker leases

Phase 4 uses an aggregate-owned lease and SQL fencing sequence to coordinate multiple workers. The guarantee is at-least-once processing with stale-writer rejection, not exactly-once external side effects.

SQL Server UTC is the authoritative clock for worker registration, heartbeats, lease acquisition and renewal, leased lifecycle checks, and expiration discovery/recovery. Process-local clocks are used only for local scheduling and bounded service-failure durations, so host clock skew cannot prematurely expire another worker's lease.

## Lease identity and fencing

A lease contains:

- `JobLeaseId`, unique independently of the job;
- owning `WorkerNodeId`;
- `JobWorkKind` (`DryRun` or `Execute`);
- positive `FencingToken`;
- `AcquiredUtc`, `LastRenewedUtc`, and `ExpiresUtc`; and
- persistence rowversion.

`wsr.JobLeaseFencingSequence` provides globally increasing tokens. Sequence gaps are harmless. Every worker-controlled mutation supplies all three credentials: lease ID, worker ID, and fencing token. Reuse of an old lease ID, impersonation of another worker, or a stale fencing token fails before aggregate mutation.

Fencing protects WindowsScriptRunner's database state. A future handler that writes to another system must use that system's idempotency/fencing mechanism where available; the lease alone cannot make arbitrary external side effects exactly once.

## Acquisition

Candidate discovery is read-only and can race. Acquisition always reloads the aggregate and worker in a fresh scope, verifies that the worker is enabled and live, allocates a fencing token, and asks the aggregate to acquire. The aggregate accepts only:

- `DryRunQueued` plus `DryRun`, leaving the status queued; or
- `ExecutionQueued` plus `Execute`, changing status to `Claimed`.

The lease, aggregate change, and `JobLeaseAcquired` audit commit atomically. Optimistic concurrency and the one-lease primary key ensure that at most one contender commits. Losing workers discard the attempt and continue.

## Renewal and release

Renewal requires current credentials, occurs before half the lease duration, and must extend expiration. Persistence retries run immediately after their bounded backoff instead of waiting another renewal interval. Renewal changes no job status, generates no fencing token, and writes no normal audit event.

Safe release is restricted to unstarted work:

- leased `DryRunQueued` remains queued; or
- leased `Claimed` Execute work with no active execution returns to `ExecutionQueued`.

Release removes the lease and writes `JobLeaseReleased`. Running DryRun, Executing, and PostValidation work cannot be released because external effects may already exist.

## Completion

Lease-aware lifecycle handlers validate current credentials and non-expiration before mutation. DryRun start/completion and Execute start/post-validation/outcome operations preserve the lease while active and remove it only at explicit terminal resolution. A stale handler cannot report success after a different owner or recovery changed the lease.

## Expiration recovery

`ExpiredLeaseRecoveryService` discovers `ExpiresUtc <= now` in bounded deterministic batches. Every candidate is reloaded and revalidated in a fresh scope:

- queued DryRun: remove lease and leave `DryRunQueued`;
- unstarted claimed Execute: remove lease and return to `ExecutionQueued`;
- running DryRun: move job to `TimedOut` and remove lease;
- Executing or PostValidation: record a timed-out execution outcome, move job to `TimedOut`, and remove lease.

Recovery writes `JobLeaseExpired` and `JobLeaseRecovered` in the same transaction. Concurrent renew/recover and recover/recover races have one valid winner; expected stale conflicts create no duplicate recovery audit.

## Audit and security

Lease audit properties are bounded to work kind, worker ID, lease ID, fencing token, expiration, and recovery disposition. Fencing tokens are non-secret coordination numbers. Parameter values, credential-reference IDs, external credential identifiers, script content, approval comments, and connection data are prohibited.

## Phase 5 contract

Phase 5 may register a production handler for an explicitly supported work kind. That handler must:

1. accept only the supplied `ClaimedJobWork`;
2. use its worker identity and fencing token without replacement;
3. honor cancellation on lease loss and shutdown;
4. resolve the lease through the lease-aware Application commands;
5. make external operations idempotent/fenced where possible; and
6. never load or log secrets through the queue descriptor.

Phase 4 intentionally supplies no production handler and does not launch PowerShell.
