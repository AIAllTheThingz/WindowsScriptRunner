# Worker queue

Phase 4 provides durable lease coordination. When enabled, Phase 6 registers exactly one production handler for the pinned `windows.local-host-inventory` `1.0.0` DryRun route. Registration is disabled by default; with the package disabled, the supported route set is empty, candidate discovery is skipped, and no queued job is leased.

## Startup and liveness

The configured `NodeId` is the durable `WorkerNodeId`. Production requires a non-empty GUID and never generates or persists an arbitrary identity file. `AllowEphemeralNodeId` defaults to false and exists only for explicit development use. Startup creates or loads the node, requires the configured name to match, rejects a disabled row, atomically synchronizes capabilities, records a heartbeat, and commits once.

Normal heartbeat uses a fresh scope, records SQL Server UTC, and creates no audit event. A persistence failure marks heartbeat unhealthy immediately, stopping new acquisitions. Transient failures use bounded persistence backoff capped by the remaining liveness window, with no extra heartbeat-interval wait before retry. If no successful heartbeat can be stored within `WorkerStaleAfterSeconds`, the heartbeat service fails rather than allowing an invisible worker to continue claiming.

## Handler registry and polling

`JobWorkHandlerRegistry` is immutable after startup. It rejects invalid or duplicate `(JobWorkKind, ScriptVersionId)` routes. Candidate discovery receives exactly that registry's supported route set. No route means no query and no claim.

For each non-overlapping poll, `JobQueueWorker`:

1. removes and observes completed dispatches;
2. stops when cancellation is requested or queue processing is disabled;
3. pauses acquisition while registration or heartbeat is unhealthy;
4. calculates available local concurrency slots;
5. requests at most the smaller of available slots and `ClaimCandidateBatchSize`;
6. attempts candidates one at a time through fresh scopes;
7. treats optimistic-concurrency or ownership changes as expected claim conflicts;
8. starts a tracked handler and renewal loop only after a committed lease;
9. resets empty-queue backoff after work is found; and
10. uses a separate persistence-failure backoff for SQL unavailability.

Eligible work is:

- `DryRunQueued` mapped to `JobWorkKind.DryRun`;
- `ExecutionQueued` mapped to `JobWorkKind.Execute`;
- without an active lease; and
- with a `ScriptVersionId` in the exact handler-supported route set.

The Phase 6 production registry contains only `(DryRun, windows.local-host-inventory 1.0.0 version ID)`. Unsupported versions are excluded by SQL rather than claimed and released repeatedly. Ordering is FIFO-like and deterministic: `UpdatedUtc`, `CreatedUtc`, then `JobId`. No priority or scheduling model is added.

## Backoff and concurrency

Empty queue and persistence failure each keep independent exponential state. Both start at `QueuePollingIntervalMilliseconds`, double to their configured maximum, and add bounded ±10% jitter without exceeding the maximum. Successful database access resets persistence backoff; a successful claim resets empty backoff. Randomness and delays are injectable for deterministic tests.

`MaxConcurrentJobs` is local to one Worker process and is bounded from 1 through 32. A candidate query and claim count never exceed available slots. Every dispatch task is tracked; completion, failure, and cancellation are observed, and a completed dispatch releases its local slot. No global static semaphore, fire-and-forget `Task.Run`, or overlapping uncontrolled poll loop is used.

## Dispatch, renewal, and return invariant

The handler receives `ClaimedJobWork`: job ID, work kind, pinned script-version ID, lease ID, worker ID, fencing token, and expiration only. It receives no script path, hash, parameters, credentials, or content. Renewal uses the same fenced credentials, SQL Server UTC, and a fresh scope. After a persistence backoff it retries immediately rather than waiting another scheduled renewal interval. A lost/missing/recovered lease cancels the handler. SQL failure retries only while renewal can still be assured before the current expiration; otherwise the handler is cancelled.

A successful handler must have removed its lease through an explicit lifecycle completion or safe release. If it returns with the lease still current, the Worker logs an invariant violation and attempts release only if work remains unstarted. Active work is left for expiration recovery.

The Phase 6 handler independently revalidates the current fenced lease, loads the pinned job and script aggregate only after ownership is established, and uses fresh scopes for every lifecycle mutation. DryRun success atomically reaches `Completed` and removes the lease. Controlled failure outcomes also remove the lease. Caller cancellation terminalizes only while the same lease is current; lease loss or uncertain persistence leaves recovery to expiration.

## Shutdown

Host cancellation stops polling and acquisition first, signals all tracked handlers, and continues their existing renewal loops during the drain window. The Worker waits up to `DrainTimeoutSeconds`. Completed/cancelled unstarted work is safely released. Active work that may have side effects is never force-released; after timeout its renewal stops and the lease expires for recovery. Final logs distinguish a normal drain from an unexpected background-service failure.

## Configuration

| Option | Default | Rule |
|---|---:|---|
| `NodeId` | empty | Non-empty in production |
| `Name` | empty | Required, at most 200 characters |
| `HeartbeatIntervalSeconds` | 30 | Positive |
| `WorkerStaleAfterSeconds` | 90 | Greater than two heartbeat intervals |
| `QueuePollingIntervalMilliseconds` | 1000 | 50–60000 |
| `EmptyQueueBackoffMaximumSeconds` | 30 | Positive, bounded |
| `PersistenceFailureBackoffMaximumSeconds` | 30 | At least heartbeat interval |
| `LeaseDurationSeconds` | 120 | Positive, bounded |
| `LeaseRenewalIntervalSeconds` | 30 | Less than half lease duration |
| `LeaseRecoveryIntervalSeconds` | 30 | Positive |
| `DrainTimeoutSeconds` | 60 | Positive |
| `MaxConcurrentJobs` | 1 | 1–32 |
| `ClaimCandidateBatchSize` | 10 | 1–100 |
| `QueueProcessingEnabled` | true | Disable to stop queue acquisition |
| `AllowEphemeralNodeId` | false | Development-only explicit opt-in |
| `Capabilities` | empty | Unique case-insensitive names |

Phase 6 adds two disabled-by-default package flags outside the Worker section:

| Option | Default | Rule |
|---|---:|---|
| `Automation:LocalHostInventory:Enabled` | false | Registers only the reviewed handler and PowerShell boundary |
| `Automation:LocalHostInventory:RegisterOnStartup` | false | Requires `Enabled=true`; creates the exact package if absent and otherwise verifies an exact match |

When enabled, `PowerShellExecution:AllowedScriptRoot` must be the absolute directory containing `windows.local-host-inventory\1.0.0\Collect-LocalHostInventory.ps1`; `WorkingRoot` must be a separate absolute non-overlapping directory. The configured PowerShell minimum version cannot be lower than the package-pinned `7.4.0`. The artifact is copied under `automation\...` in build and publish output, so an operator commonly selects the output `automation` directory as the allowed root. No configuration key can change package identity, version, relative path, SHA-256, phases, or parameter allowlist.

## Observability

Safe structured logs contain worker/job/lease IDs, work kind, fencing token, expiration, counts, outcomes, recovery disposition, and bounded persistence categories. `System.Diagnostics.Metrics` publishes:

- `worker.queue.polls`, `worker.queue.claims`, `worker.queue.claim_conflicts`, `worker.queue.empty_polls`;
- `worker.queue.dispatch_started`, `worker.queue.dispatch_completed`, `worker.queue.dispatch_failed`;
- `worker.lease.renewed`, `worker.lease.lost`, `worker.lease.recovered`;
- `worker.heartbeat.success`, `worker.heartbeat.failure`; and
- gauges `worker.active_dispatches` and `worker.available_slots`.

Testable runtime state exposes Registered, heartbeat health, queue health, last successful poll, and active dispatch count. No Worker HTTP endpoint or telemetry exporter is added.
