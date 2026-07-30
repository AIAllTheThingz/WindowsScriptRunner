# Security properties

- No raw credential property exists in the domain model; `CredentialReference` stores only an external identifier.
- `SecureReference` job parameters must contain a canonical non-empty `CredentialReferenceId` GUID. Application handlers resolve the ID, reject missing or disabled references, store only the canonical ID, and never audit external vault identifiers.
- Null, empty, and whitespace are one absent-value representation. The pinned definition accepts or rejects absence before type parsing or credential lookup. Accepted absence removes the explicit draft binding; required absence without a default leaves the job unchanged and performs no credential lookup, persistence update, success audit, or commit.
- Parameter defaults remain immutable `ScriptParameterDefinition` metadata. Clearing an override never copies a default into `JobParameter`, and clear audit data never contains a prior value or credential-reference ID.
- Sensitive job parameters redact `ToString`, query responses, and audit values.
- `JobParameter` stores only name/value binding data. Parameter type, sensitivity, SecureReference classification, value validation, audit classification, and response redaction derive from the pinned immutable `ScriptParameterDefinition`.
- Parameter audit events store bounded metadata such as parameter name, pinned type, pinned sensitivity, value-present flag, and serialized length. Full parameter values are not written to audit properties.
- Credential external identifiers must be provider-scoped reference URIs whose scheme matches the provider type and which contain an authority and path. User information, query strings, fragments, unlabelled values, and obvious embedded-secret markers are rejected.
- Draft, submitted, approved, executing, and terminal job responses derive redaction from the pinned script version. Inconsistent or corrupted parameter bindings fail closed and do not expose raw values.
- Script paths must be relative and reject rooted or traversal forms.
- Script SHA-256 metadata must contain exactly 64 hexadecimal characters.
- Published script versions reject mutation.
- Published Execute-capable script versions must also support DryRun; Execute job submissions enforce the same invariant defensively before policy capture.
- Web has no direct reference to Worker or PowerShell.
- Domain references no solution, ASP.NET Core, Entity Framework Core, or SQL client assembly.
- Submission captures trusted script risk plus Execute- and PostValidation-phase capabilities in an immutable job policy snapshot only after rejecting undefined risk and phase enum values.
- New submissions require the selected script definition to be enabled and the selected version to be published. Disabling a script later prevents new submissions; already-submitted jobs keep their captured Phase 2 policy until future runtime governance is implemented.
- Submitted jobs enforce the requested phase: Validation stops after validation, DryRun stops after dry-run, and only Execute requests may enter approval/execution states.
- Requesters cannot self-approve Medium, High, or Critical work, and callers cannot lower risk at approval time. The documented Phase 2 policy permits ReadOnly and Low self-approval.
- Windows user identities compare with ordinal case-insensitive equality so casing cannot bypass self-approval checks. Future authentication should map users to stable SIDs or equivalent principal identifiers.
- Read-only completion requires captured ReadOnly risk and a captured absence of Execute support; callers cannot override either value.
- Approval, rejection, execution, and completion states require dedicated evidence-bearing operations; the generic application transition handler rejects protected targets and refuses to terminalize jobs with active execution attempts.
- Execution outcomes complete the active `JobExecution` and terminalize the parent `Job` in one aggregate operation, preventing orphaned active attempts.
- Aggregate actor, timestamp, lifecycle, and child-object validation occurs before mutation.
- `ScriptDefinition.UpdateDetails` validates both proposed text fields before assigning either, preventing partial field changes when validation fails.
- Source project files are parsed to enforce exact project-reference allowlists, including explicit Web-to-Worker and Web-to-PowerShell prohibitions.
- Strong identifiers are immutable reference records with no public parameterless constructor; aggregate boundaries reject null identifiers.
- Script definitions reject duplicate semantic versions and duplicate `ScriptVersionId` values before mutation.
- Audit properties reject control characters and obvious sensitive key names.
- SQL persistence uses parameterized EF Core queries; no raw SQL is assembled from caller input.
- EF sensitive-data logging is not enabled. Repository and health logs use bounded identifiers and categories without parameter values, external credential identifiers, connection strings, or provider exception text.
- Credential-reference rows contain only provider metadata, a validated external identifier, and its SHA-256 lookup hash. They never contain raw credential material.
- Mutable aggregate roots use SQL Server rowversion concurrency tokens. Persistence exceptions are translated to bounded application exceptions without leaking SQL text or connection details.
- Audit events are append-only through the application abstraction and are committed in the same transaction as aggregate changes.
- Production startup migration is disabled by default. Readiness reports unhealthy if SQL is unavailable or migrations are pending, while liveness remains independent of SQL.
- Database constraints and triggers repeat critical integrity rules, including unique aggregate keys, one active execution per job, valid enum ranges, temporal ordering, and Execute-with-DryRun publication.

Authentication, authorization, executable signing, production trusted-artifact resolution, external credential retrieval, operating-system sandboxing, runtime cancellation policy for already-approved jobs after script disable, and production approval controls are not implemented.

## Phase 4 queue security

- Production registers no `IJobWorkHandler`, so it advertises zero work kinds and leases no jobs.
- Worker continues to have no PowerShell project reference, `Process.Start`, `System.Diagnostics.Process`, or `System.Management.Automation` use.
- Candidate and claimed-work descriptors contain no parameters, serialized values, credential-reference IDs, external identifiers, or script content.
- The SQL candidate projection is bounded, parameterized, filters exact eligible status plus lease absence, and orders deterministically.
- Every worker-controlled mutation requires current lease ID, worker ID, and fencing token. Domain validation precedes mutation; stale audit and job state cannot commit.
- Acquisition revalidates that the persisted worker is enabled and live. Heartbeat failure immediately pauses new claims, and prolonged inability to heartbeat fails the hosted service.
- Renewal uses the existing fencing token and writes no routine audit. Loss or inability to renew safely cancels the handler.
- Audit metadata is limited to work kind, worker ID, lease ID, fencing token, expiration, and recovery disposition. `FencingToken` is coordination metadata, not authentication or secret material.
- Logs use identifiers, counts, outcomes, and bounded persistence categories. They omit parameter values, credential data, scripts, connection strings, SQL authentication data, and approval comments.
- Lease coordination is at-least-once. A future side-effecting handler must make its downstream operations idempotent and propagate or validate fencing where that downstream system supports it.

## Phase 5 PowerShell security

- PowerShell runs only as an external `pwsh.exe` process. Production projects contain no `Microsoft.PowerShell.SDK`, `System.Management.Automation`, runspace, `powershell.exe`, command-shell, or execution-policy-bypass dependency.
- Runtime discovery is deterministic and validates fixed JSON metadata. PowerShell Core, Windows, minimum version, preview policy, and architecture are enforced, and the successful runtime is cached.
- `TrustedPowerShellScript` has no public constructor. Phase 5 creates only the test fixture artifact through test-only internal access; there is no production resolver or arbitrary script API.
- The script must be an existing canonical local `.ps1` beneath the separator-normalized allowed root. UNC paths, device paths, alternate data streams, traversal, sibling-prefix escapes, and reparse-point components are rejected.
- SHA-256 is recomputed with a read-only file handle immediately before startup and compared in constant time. A small close-to-process-start time-of-check/time-of-use race remains.
- Parameter names use a conservative identifier grammar, must belong to the artifact allowlist, and are unique case-insensitively. Count and value length are bounded; null, NUL, leading-hyphen, and sensitive-classified values are rejected.
- `ArgumentList` supplies `-NoLogo`, `-NoProfile`, `-NonInteractive`, `-File`, the trusted path, and named literal values. No caller value enters `-Command`; no shell quoting heuristic or `ExecutionPolicy Bypass` is used.
- Command-line arguments are visible to operating-system process inspection. Phase 5 therefore does not accept secret values or perform secret injection.
- The child environment is cleared and rebuilt from a fixed Windows allowlist plus telemetry/update-check controls. Parent API keys, connection strings, and arbitrary variables are not inherited.
- Trusted-script and working roots cannot overlap or be nested. Each execution receives a unique directory beneath the configured working root. It is removed after exit, failure, timeout, cancellation, or output overflow.
- UTF-8 stdout and stderr are drained concurrently into fixed-size, bounded capture. Output text and parameter values are returned only to the caller and never logged; logs contain safe IDs, artifact/runtime metadata, durations, reasons, exit codes, byte counts, and truncation flags.
- Timeout, caller cancellation, and output overflow terminate the complete process tree through a kill-on-close Windows Job Object with a full-tree kill fallback. Termination is bounded and failure is not reported as ordinary success.
- Job Objects provide lifetime containment only. They do not restrict filesystem, registry, network, privileges, or the PowerShell language. The immediate post-start assignment has a small race because the process is not created suspended.
- Web and Worker neither reference nor register the PowerShell project. The production Worker still registers no `IJobWorkHandler`, so Phase 5 cannot lease or execute queued work.
