# Architecture

The web application and worker are separate processes. The web process must never execute PowerShell directly. It will eventually submit work through application contracts, while the worker will coordinate job execution.

The Domain project remains independent of all outer layers and owns aggregates and lifecycle invariants. Application coordinates use cases through domain-specific repository, audit, clock, and unit-of-work interfaces. Infrastructure implements those persistence contracts with EF Core and SQL Server. The Phase 5 PowerShell project owns an out-of-process PowerShell 7 boundary and has no production-project dependency. Only its integration tests invoke the boundary.

```text
Browser
   |
   v
Web --> Application --> Domain
 |           |
 +--> Infrastructure (EF Core / SQL Server)
 +--> Reporting

Worker --> Application --> Domain
   |----> Infrastructure
   +----> Reporting

PowerShellTests --> PowerShell boundary --> controlled pwsh.exe child process
```

Arrows represent compile-time dependencies. Domain has no solution-project dependencies.

Contracts contains immutable transport DTOs and does not reference Domain. Application maps Domain objects to safe contract responses. Web and Worker are composition roots: both register Infrastructure, while neither references EF Core or `Microsoft.Data.SqlClient` directly. Worker does not reference the PowerShell project.

Security tests parse each source `.csproj` and compare its `ProjectReference` entries with an explicit allowlist. Compiled-reference checks remain supplementary. The source-level rules explicitly prohibit Web from referencing Worker or PowerShell and require Domain and Contracts to have no project references.

Phase 2 application handlers remain orchestration-only. They load domain objects, validate related references, delegate invariants to Domain, construct bounded audit events, then update repositories and commit. No handler accepts caller-provided risk, Execute capability, parameter sensitivity, parameter type, raw credential material, or generic completion status overrides. Execution terminalization is modeled as a dedicated outcome handler so an active `JobExecution` cannot be left open by a direct job-status update.

Parameter classification is a read-side and write-side trust boundary: Application loads the pinned `ScriptDefinition`/`ScriptVersion` for both parameter writes and job-detail reads. Stored `JobParameter` data is only a name/value binding; response mapping fails closed if that binding cannot be validated against the pinned version.

Parameter absence is also resolved at this boundary. The pinned definition decides whether canonical absence (null, empty, or whitespace) is allowed before any type-specific or credential operation. An accepted absence calls the aggregate-controlled draft clear operation, which removes the explicit binding and preserves definition-owned defaults. Present SecureReference values alone cross the credential-reference lookup boundary.

Domain mutation is validation-first. Aggregate methods validate proposed scalars, actors, timestamps, transitions, children, and collection uniqueness before assigning fields or changing collections. `ScriptDefinition.UpdateDetails` validates both proposed text fields into locals before atomically assigning either field and its timestamp.

## Persistence boundary

Infrastructure uses separate internal EF row types rather than annotating or exposing domain objects. `PersistenceMapper` is the only domain-to-row and row-to-domain translation boundary. Internal, validated rehydration factories reconstruct aggregates without public setters or persistence attributes.

Repositories track one aggregate graph per scoped `WindowsScriptRunnerDbContext` and stage changes without saving. `SqlAuditWriter` stages append-only audit rows in the same context. `SqlUnitOfWork.CommitAsync` is the only production `SaveChangesAsync` boundary, so aggregate and audit changes commit atomically. The configured execution strategy encloses the commit; when unchanged tracked aggregate roots are used as read dependencies, they are rowversion-revalidated under a serializable transaction without being updated. SQL rowversion columns protect mutable roots and validation dependencies, and stale operations become bounded application-facing concurrency exceptions.

`AddInfrastructure` registers a scoped context, repositories, audit writer, unit of work, readiness health check, and a guarded migration hosted service. Startup migration is disabled unless `Persistence:ApplyMigrationsOnStartup` is explicitly enabled. Liveness does not depend on SQL Server; readiness requires connectivity and no pending migrations.

## Phase 4 worker coordination

The Worker composition root registers Application and Infrastructure plus four focused hosted services: startup registration, persistent heartbeat, handler-gated queue polling, and expired-lease recovery. Startup registration commits a stable `WorkerNode` identity, its complete configured capability set, and its first heartbeat atomically. Every subsequent heartbeat and every lease operation creates a fresh dependency-injection scope.

Candidate discovery returns identifiers and queue metadata only. It does not load parameters, credential references, script contents, or the aggregate graph. Supported `JobWorkKind` values come exclusively from the startup `IJobWorkHandler` registry. The production Phase 4 registry contains zero handlers, so it performs no candidate query and cannot acquire a lease. Test-only handlers exercise coordination without crossing the PowerShell boundary.

`Job` owns its optional `JobLease`. Persisted job handlers and worker handlers obtain SQL Server UTC through the scoped coordination clock; lease acquisition also obtains a monotonic SQL fencing token. Queue entry, registration, heartbeat, acquisition, renewal, leased lifecycle validation, and expiration recovery therefore share one time authority across hosts. Terminal lease resolution uses a bounded retry only when the job row is unchanged and the same fenced owner renewed the lease concurrently. The queue tracks every dispatch, renews through fresh scopes, cancels work after lease loss, limits local concurrency, and drains for a configured interval at shutdown. Expired leases are independently revalidated and recovered. This produces at-least-once coordination; it does not promise exactly-once external side effects.

Worker liveness is deliberately separate from Web health. `/health`, `/health/live`, and `/health/ready` remain Web endpoints, and Web readiness continues to depend only on its SQL/migration state. The Worker exposes testable in-process state rather than a new HTTP endpoint.

## Phase 5 PowerShell boundary

`WindowsScriptRunner.PowerShell` alone owns executable discovery, the constant runtime probe, `ProcessStartInfo`, process lifecycle, stream capture, Windows Job Object containment, trusted-path validation, hash verification, working directories, and the minimized child environment. It uses `ProcessStartInfo.ArgumentList`; caller values never enter `-Command`, a command string, or a shell. The public execution method accepts a request containing an internally constructed `TrustedPowerShellScript`, named non-sensitive arguments, an execution ID, and a bounded timeout. There is no public arbitrary path, command, or script-text execution API.

The locator considers a configured absolute `pwsh.exe`, `WINDOWSSCRIPTRUNNER_PWSH_PATH`, PATH entries inspected with file APIs, and stable `%ProgramFiles%\PowerShell` installations. A constant JSON probe requires PowerShell Core on Windows, the configured minimum version, allowed preview status, and the configured architecture. The successful immutable runtime is cached.

Every execution creates `<working-root>\<execution-id>`, revalidates the controlled script path and SHA-256 immediately before launch, starts `pwsh.exe` without a shell, drains UTF-8 stdout and stderr concurrently into bounded buffers, and removes the working directory on every completion path. Timeout and output overflow return distinct results; caller cancellation terminates the process tree, drains the pipes, cleans up, and throws `OperationCanceledException`.

Windows Job Objects apply `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`; `Process.Kill(entireProcessTree: true)` is the fallback and retry path. This controls process lifetime, not filesystem, registry, network, token, or privilege access. Assignment occurs immediately after startup, so a small process-start-to-assignment race remains. The trusted hash check also has a small check-to-process-start filesystem race. Phase 5 makes neither an operating-system sandbox nor an absolute malicious-filesystem claim.

No Web or Worker composition root registers this boundary. The production handler registry remains empty, and no queue status, lease, or database row changes because Phase 5 exists. Phase 6 may introduce a reviewed handler that resolves a production trusted artifact and maps leased work into this boundary; that integration is not present here.
