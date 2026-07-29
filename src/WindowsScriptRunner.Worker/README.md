# Worker

The Phase 4 Worker coordinates durable queue leases but executes no scripts. Production registers zero `IJobWorkHandler` implementations, so it supports zero work kinds and cannot claim a job.

Startup requires `ConnectionStrings:WindowsScriptRunner` and a stable `Worker:NodeId` unless the development-only `Worker:AllowEphemeralNodeId` switch is explicitly enabled. The complete option reference is in `docs/worker-queue.md`.

Hosted services:

- `WorkerRegistrationHostedService` registers identity, capabilities, and initial heartbeat atomically.
- `WorkerHeartbeatService` persists liveness and fails after the configured stale window.
- `JobQueueWorker` polls only handler-supported work, owns backoff/concurrency/renewal/drain behavior, and tracks every dispatch.
- `ExpiredLeaseRecoveryService` revalidates and resolves expired leases in bounded batches.

Apply the reviewed database migration before startup. `Persistence:ApplyMigrationsOnStartup` remains false by default. Do not add a production handler here until the Phase 5 contract is approved; handlers must resolve leases explicitly and must not receive raw parameter or credential material through queue descriptors.
