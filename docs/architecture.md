# Architecture

This document describes the Phase 8 implementation awaiting review. Phase labels identify when each boundary was introduced; they do not imply that later sections replace the earlier controls.

The web application and Worker are separate processes. Web never executes PowerShell or references the PowerShell or Automation projects. The Worker is the only production composition root that may enable the reviewed automation package.

The Domain project remains independent of all outer layers and owns aggregates and lifecycle invariants. Application coordinates use cases through domain-specific repository, audit, clock, and unit-of-work interfaces. Infrastructure implements those persistence contracts with EF Core and SQL Server. Reporting is independent and owns the strict package-specific parser, canonical typed parse result, and deterministic digest representation. PowerShell owns the complete out-of-process PowerShell 7 boundary. Automation owns the single reviewed production artifact, catalog, registration, process-result adaptation, and lease-aware handler.

```text
Windows-authenticated browser
   |
   v
Web (Negotiate, Razor Pages) --> Application --> Domain
 |           |
 +--> Infrastructure (EF Core / SQL Server)

Worker --> Application --> Domain
   |----> Infrastructure
   +----> Automation --> PowerShell --> reviewed pwsh.exe child process
               |------> Reporting

Application ---------------------> Reporting

PowerShellTests --> PowerShell boundary --> controlled test and reviewed package processes
```

Arrows represent compile-time dependencies. Domain has no solution-project dependencies.

Contracts contains immutable transport DTOs and does not reference Domain. Application maps Domain objects to safe contract responses. Web and Worker are composition roots: both register Application and Infrastructure, while neither references EF Core or `Microsoft.Data.SqlClient` directly. Worker references Automation but not PowerShell or Reporting directly. Reporting has no solution-project dependency. Automation is the narrow reviewed bridge and references Application, Domain, PowerShell, and Reporting.

Security tests parse each source `.csproj` and compare its `ProjectReference` entries with an explicit allowlist. Compiled-reference checks remain supplementary. The source-level rules explicitly prohibit Web from referencing Worker, Automation, or PowerShell and require Domain and Contracts to have no project references.

Application handlers remain orchestration-only. They load domain objects, validate related references, delegate invariants to Domain, construct bounded audit events, then update repositories and commit. No handler accepts caller-provided risk, Execute capability, parameter sensitivity, parameter type, raw credential material, or generic completion status overrides. Execution terminalization is modeled as a dedicated outcome handler so an active `JobExecution` cannot be left open by a direct job-status update.

Parameter classification is a read-side and write-side trust boundary: Application loads the pinned `ScriptDefinition`/`ScriptVersion` for both parameter writes and job-detail reads. Stored `JobParameter` data is only a name/value binding; response mapping fails closed if that binding cannot be validated against the pinned version.

Parameter absence is also resolved at this boundary. The pinned definition decides whether canonical absence (null, empty, or whitespace) is allowed before any type-specific or credential operation. An accepted absence calls the aggregate-controlled draft clear operation, which removes the explicit binding and preserves definition-owned defaults. Present SecureReference values alone cross the credential-reference lookup boundary.

Domain mutation is validation-first. Aggregate methods validate proposed scalars, actors, timestamps, transitions, children, and collection uniqueness before assigning fields or changing collections. `ScriptDefinition.UpdateDetails` validates both proposed text fields into locals before atomically assigning either field and its timestamp.

## Persistence boundary

Infrastructure uses separate internal EF row types rather than annotating or exposing domain objects. `PersistenceMapper` is the only domain-to-row and row-to-domain translation boundary. Internal, validated rehydration factories reconstruct aggregates without public setters or persistence attributes.

Repositories track one aggregate graph per scoped `WindowsScriptRunnerDbContext` and stage changes without saving. `SqlAuditWriter` stages append-only audit rows in the same context. `SqlUnitOfWork.CommitAsync` is the only production `SaveChangesAsync` boundary, so aggregate and audit changes commit atomically. The configured execution strategy encloses the commit; when unchanged tracked aggregate roots are used as read dependencies, they are rowversion-revalidated under a serializable transaction without being updated. SQL rowversion columns protect mutable roots and validation dependencies, and stale operations become bounded application-facing concurrency exceptions.

`AddInfrastructure` registers a scoped context, repositories, audit writer, unit of work, readiness health check, and a guarded migration hosted service. Startup migration is disabled unless `Persistence:ApplyMigrationsOnStartup` is explicitly enabled. Liveness does not depend on SQL Server; readiness requires connectivity and no pending migrations.

## Phase 4 worker coordination

The Worker composition root registers Application and Infrastructure plus four focused hosted services: startup registration, persistent heartbeat, handler-gated queue polling, and expired-lease recovery. Startup registration commits a stable `WorkerNode` identity, its complete configured capability set, and its first heartbeat atomically. Every subsequent heartbeat and every lease operation creates a fresh dependency-injection scope.

Candidate discovery returns identifiers and queue metadata only. It does not load parameters, credential references, script contents, or the aggregate graph. Supported `(JobWorkKind, ScriptVersionId)` routes come exclusively from the immutable startup `IJobWorkHandler` registry. When the reviewed package is disabled the registry is empty. When enabled, it contains only the DryRun route for the reviewed inventory version. Unknown versions are filtered in SQL and remain unclaimed.

`Job` owns its optional `JobLease`. Persisted job handlers and worker handlers obtain SQL Server UTC through the scoped coordination clock; lease acquisition also obtains a monotonic SQL fencing token. Queue entry, registration, heartbeat, acquisition, renewal, leased lifecycle validation, and expiration recovery therefore share one time authority across hosts. Terminal lease resolution uses a bounded retry only when the job row is unchanged and the same fenced owner renewed the lease concurrently. The queue tracks every dispatch, renews through fresh scopes, cancels work after lease loss, limits local concurrency, and drains for a configured interval at shutdown. Expired leases are independently revalidated and recovered. This produces at-least-once coordination; it does not promise exactly-once external side effects.

Worker liveness is deliberately separate from Web health. `/health`, `/health/live`, and `/health/ready` remain Web endpoints, and Web readiness continues to depend only on its SQL/migration state. The Worker exposes testable in-process state rather than a new HTTP endpoint.

## Phase 5 PowerShell boundary

`WindowsScriptRunner.PowerShell` alone owns executable discovery, the constant runtime probe, `ProcessStartInfo`, process lifecycle, stream capture, Windows Job Object containment, trusted-path validation, hash verification, working directories, and the minimized child environment. It uses `ProcessStartInfo.ArgumentList`; caller values never enter `-Command`, a command string, or a shell. The public execution method accepts a request containing an internally constructed `TrustedPowerShellScript`, named non-sensitive arguments, an execution ID, and a bounded timeout. There is no public arbitrary path, command, or script-text execution API.

The locator considers a configured absolute `pwsh.exe`, `WINDOWSSCRIPTRUNNER_PWSH_PATH`, PATH entries inspected with file APIs, and stable `%ProgramFiles%\PowerShell` installations. A constant JSON probe requires PowerShell Core on Windows, the configured minimum version, allowed preview status, and the configured architecture. The probe timeout remains active until the root exits and both redirected pipes close. The successful immutable runtime is cached.

Every execution atomically reserves and exclusively creates `<working-root>\<execution-id>`, rejects a competing directory or reparse point, and holds an undeletable claim until cleanup. It revalidates the controlled script path and SHA-256 immediately before launch, starts `pwsh.exe` without a shell, drains UTF-8 stdout and stderr concurrently into bounded buffers, and removes the claim, reservation, and working directory on every completion path. Root exit does not stop lifecycle enforcement while inherited output pipes remain open. Timeout and output overflow return distinct results; caller cancellation terminates the process tree, drains the pipes, cleans up, and throws `OperationCanceledException`.

Windows Job Objects apply `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`; `Process.Kill(entireProcessTree: true)` is the fallback and retry path, including when the root has already exited but descendants remain. This controls process lifetime, not filesystem, registry, network, token, or privilege access. Assignment occurs immediately after startup, so a small process-start-to-assignment race remains. The trusted hash check also has a small check-to-process-start filesystem race. Phase 5 makes neither an operating-system sandbox nor an absolute malicious-filesystem claim.

Web never registers the boundary. Worker calls the Automation composition extension; only explicit reviewed-package enablement causes that extension to register the PowerShell boundary and the single reviewed handler.

## Phase 6 first automation package

`WindowsScriptRunner.Automation` pins package ID `windows.local-host-inventory`, semantic version `1.0.0`, stable definition/version GUIDs, relative artifact path, SHA-256, empty parameter allowlist, ReadOnly risk, DryRun-only behavior, JSON report format, minimum PowerShell version, and one-minute timeout. None of those trust values come from configuration, a request, or SQL. Configuration can only enable the known package, opt into registration, and select the Phase 5 trusted and working roots.

The artifact catalog first compares the loaded immutable `ScriptDefinition` and published `ScriptVersion` with every pinned value. A PowerShell-owned reviewed-artifact factory then resolves the relative path beneath the configured root and performs the Phase 5 path/reparse/hash validation. The execution boundary repeats trust validation immediately before launch.

The singleton handler creates fresh scopes to inspect fenced ownership, load the aggregate and immutable script metadata, start DryRun, and terminalize. It derives the PowerShell execution ID from the immutable `JobId`, maps no arguments because the package defines none, and invokes only `IPowerShellExecutionBoundary`. Nonzero exit, timeout, output overflow, trust failure, runtime failure, and caller cancellation map to bounded terminal outcomes. Lease loss never permits a stale mutation.

The script emits one bounded JSON document with schema version, computer name, OS description/version/architecture, PowerShell version, and UTC collection time. It does not enumerate software, users, environment variables, certificates, network data, or secrets.

## Phase 7 typed durable reporting

Reporting accepts a narrow process-result abstraction rather than a PowerShell implementation type. The parser requires an exited code-zero result, untruncated streams, whitespace-only stderr, consistent execution timestamps, and no more than 8 KiB of well-formed UTF-8 stdout. `Utf8JsonReader` runs with comments and trailing commas disabled and a depth limit. The parser explicitly tracks every property name at every object level, rejects duplicates, unknowns, wrong casing, missing values, nulls, wrong types, control characters, malformed names and versions, unsupported architectures, old PowerShell versions, and collection timestamps outside the process window. Only the fully validated immutable typed result crosses into Application.

`CompleteLocalHostInventoryDryRunHandler` is the only report write use case. Its command has no caller-selected package, risk, schema policy, report type, format, sensitivity, terminal status, or arbitrary JSON. The handler loads the job, revalidates the pinned published `windows.local-host-inventory` `1.0.0` metadata, recognizes an already committed exact replay when present, otherwise validates current DryRun lease ID, worker ID, fencing token, work kind, expiration, job status, and PowerShell execution ID using SQL coordination time. It then creates a deterministic `JobReportId`, computes the canonical SHA-256, stages the immutable report, completes the ReadOnly DryRun, removes the lease through Domain invariants, writes a bounded audit event, and commits once.

The digest covers stable report provenance and every typed inventory value, but excludes the SQL persisted timestamp so the same execution produces the same comparison material after an uncertain commit. The report ID is derived from job/package/schema identity. Exact retries require completed job state, no lease, identical script/job/worker/lease/fencing/execution provenance, identical typed values, collection time, and digest. Any conflict fails closed and never overwrites the report. Concurrent attempts are independently constrained by the primary key and unique job/package/schema, lease, and PowerShell execution indexes. This guarantees at most one accepted durable report; it does not claim exactly-once PowerShell execution.

Infrastructure persists a `JobReports` envelope and one required `LocalHostInventoryReports` detail row. There is no raw stdout, stderr, JSON payload, generic payload table, or report update repository. The one scoped unit of work atomically commits job, lease deletion, report envelope/detail, and audit rows. The read repository rehydrates the typed Domain model and fails closed when envelope and detail disagree. Application maps it to one immutable Contracts response by report ID or job ID.

## Phase 8 protected portal and approval boundary

Web registers Application and Infrastructure, then composes Windows Negotiate, a fallback authenticated policy, SID-based group policies, and resource authorization. It maps a real authenticated Windows principal to a stable `sid:<canonical-sid>` `UserIdentity`; names and browser form fields are never identity or role authority. Web still has no project reference to Worker, Automation, PowerShell, or Reporting, and it never invokes a process, accesses an execution working directory, or obtains a credential.

The portal is a thin adapter over Application handlers. It can show job details, bounded awaiting-approval jobs, and the persisted Local Host Inventory report through a safe view model that omits raw output, JSON, worker/lease/fencing provenance, execution identity, digests, and secure references. Its report list is bounded to 100 and filters every item by its owning job's resource authorization.

Approval review loads an Application `ApprovalReviewResponse`, which includes a server-calculated expected fingerprint. A POST reloads and reauthorizes the route-bound job, runs normal Razor Pages antiforgery validation, and calls the existing Application decision handler. Leased DryRun completion records immutable evidence carrying the DryRun work kind, worker/lease/fencing provenance, and the execution-window timestamps. Application fingerprints that evidence—not Execute attempts—and Domain requires DryRun evidence to be accepted before an Execute request can enter approval or receive a decision. The handler derives the actor from `ICurrentUser`, recomputes trusted fingerprint evidence, delegates separation-of-duties/state rules to Domain, stages audit/job changes, and commits through Infrastructure. Web has no direct state, audit, or lease mutation.

The explicit Web routes and policies are in [authorization matrix](authorization-matrix.md); SID mapping is in [Windows authentication](windows-authentication.md). IIS, HTTPS, Kerberos/SPN operational validation, service identity setup, and deployment remain outside this architecture in Phase 9.
