# Worker

The Worker coordinates durable fenced queue leases and can register one reviewed production `IJobWorkHandler` through Automation. Package enablement and bootstrap registration are disabled by default.

Startup requires `ConnectionStrings:WindowsScriptRunner` and a stable `Worker:NodeId` unless the development-only `Worker:AllowEphemeralNodeId` switch is explicitly enabled. The complete option reference is in `docs/worker-queue.md`.

Hosted services:

- `WorkerRegistrationHostedService` registers identity, capabilities, and initial heartbeat atomically.
- `WorkerHeartbeatService` persists liveness and fails after the configured stale window.
- `JobQueueWorker` polls only handler-supported work, owns backoff/concurrency/renewal/drain behavior, and tracks every dispatch.
- `ExpiredLeaseRecoveryService` revalidates and resolves expired leases in bounded batches.

Apply the reviewed database migrations before startup. `Persistence:ApplyMigrationsOnStartup` remains false by default. Production handlers must resolve leases explicitly and must not receive raw parameter or credential material through queue descriptors.

To enable the reviewed `windows.local-host-inventory` `1.0.0` package, set `Automation:LocalHostInventory:Enabled=true`, optionally set `RegisterOnStartup=true`, and configure absolute, non-overlapping `PowerShellExecution:AllowedScriptRoot` and `WorkingRoot` directories. The allowed root contains the deterministically copied `windows.local-host-inventory\1.0.0\Collect-LocalHostInventory.ps1`. The SQL candidate query advertises only its pinned DryRun/script-version route; unknown versions are never claimed.

The Automation handler—not Worker—passes code-zero output to Reporting. Valid typed inventory goes to the atomic Application report completion handler; malformed or untrusted success output becomes a controlled failure with no report. Worker contains no inventory JSON parser and no report persistence policy.

Worker has no HTTP endpoint. Phase 9 adds Windows Service hosting integration through the
`Microsoft.Extensions.Hosting.WindowsServices` lifetime; installation, upgrade, rollback, and
verification remain explicit deployment-script operations. See the [Windows Service deployment
status](../../deployment/windows-service/README.md).

See [Worker queue](../../docs/worker-queue.md), [Worker leases](../../docs/worker-leases.md), and [Windows Service deployment status](../../deployment/windows-service/README.md).
