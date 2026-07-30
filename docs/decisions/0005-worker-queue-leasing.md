# 0005: Worker queue leasing

Status: Accepted for Phase 4; route filtering extended in Phase 6

## Context

Multiple WindowsScriptRunner Worker processes must coordinate queued jobs without holding long database transactions or claiming work they cannot execute. A worker can crash after acquisition, lose SQL connectivity, or finish after another process has recovered the job. Phase 4 must establish this coordination before any production PowerShell execution exists.

## Decision

Use a one-to-one aggregate-owned `JobLease` persisted in `wsr.JobLeases`, with a globally monotonic token from `wsr.JobLeaseFencingSequence`. Candidate discovery is a bounded projection over eligible statuses and lease absence. Acquisition is an optimistic race committed through the existing aggregate/audit unit of work. Every subsequent worker-controlled mutation requires lease ID, worker ID, and fencing token.

Workers advertise only `(JobWorkKind, ScriptVersionId)` routes represented by registered `IJobWorkHandler` instances. No route means no candidate query and no claim. Enabled Phase 6 production registers only the reviewed inventory DryRun route; SQL excludes unsupported versions before acquisition.

Leases are renewed periodically. Unstarted work can release safely. Expired active work is terminalized as timed out; expired unstarted work is requeued. Polling, heartbeat, renewal, and recovery use fresh scopes and bounded backoff. Shutdown drains tracked tasks for a configured interval and leaves potentially side-effecting active work to expire.

## Consequences

- Multi-worker claim races have a single committed owner.
- Stale completions are rejected by aggregate fencing checks.
- Crashed work becomes recoverable without a long database lock.
- Queue descriptors remain free of parameters and credentials.
- Sequence gaps and duplicate delivery attempts are expected.
- The system provides at-least-once coordination, not exactly-once effects.
- Future handlers must be cancellation-aware and make downstream effects idempotent or fenced.
- Production processes only routes deliberately registered by reviewed Worker-side composition.

Subsequent status: Phase 7 retains the fenced lease through report validation and commits the immutable typed report, job completion, lease deletion, and audit event atomically. Exact report replay is idempotent; a conflicting replay fails closed.

## Alternatives not selected

- Database row locks held for the full work duration would couple execution to a fragile long transaction.
- Status-only claiming cannot distinguish a stale worker from the current owner.
- In-memory queues do not coordinate hosts or survive restarts.
- Registering a placeholder production executor would allow unsupported work to be claimed and violates the phase boundary.
