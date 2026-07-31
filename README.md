# Windows Script Runner

Windows Script Runner is a Windows-hosted .NET application foundation for controlled automation. **Phases 1–5 are complete and merged. Phase 6 adds the first reviewed production automation package.**

## Status

The solution contains the validated Domain, Application, SQL Server persistence, lease-backed Worker, and isolated PowerShell 7 boundary. Phase 6 adds exactly one production package: `windows.local-host-inventory` version `1.0.0`, a ReadOnly, local-worker, DryRun-only inventory collector. The package and its idempotent registration are disabled by default.

Production startup does not apply migrations by default. Operators must deploy the reviewed migration artifact or explicitly opt into startup migration for a controlled environment.

## Solution structure

- `src/WindowsScriptRunner.Web` — Razor Pages UI and health endpoint
- `src/WindowsScriptRunner.Worker` — durable registration, heartbeat, lease recovery, and handler-gated queue coordination
- `src/WindowsScriptRunner.Application` — use-case handlers and persistence/audit abstractions
- `src/WindowsScriptRunner.Automation` — the reviewed, hash-pinned production package, catalog, registration, and Worker handler
- `src/WindowsScriptRunner.Domain` — independent aggregates, lifecycle rules, and value objects
- `src/WindowsScriptRunner.Infrastructure` — EF Core SQL Server persistence, repositories, migrations, health checks, and composition
- `src/WindowsScriptRunner.Contracts` — shared public request and response contracts
- `src/WindowsScriptRunner.PowerShell` — isolated, bounded PowerShell 7 child-process boundary
- `src/WindowsScriptRunner.Reporting` — future report generation
- `tests` — unit, integration scaffold, real SQL Server, worker, security, and PowerShell boundary tests
- `automation`, `deployment`, `docs` — operational placeholders and documentation

## Prerequisites

- Git
- Stable .NET 10 SDK
- PowerShell 7
- SQL Server; SQL Server LocalDB is supported for development and tests

## Commands

```powershell
dotnet tool restore
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
dotnet format
dotnet format --verify-no-changes
dotnet ef database update --project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj --startup-project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj
dotnet run --project .\src\WindowsScriptRunner.Web\WindowsScriptRunner.Web.csproj
dotnet run --project .\src\WindowsScriptRunner.Worker\WindowsScriptRunner.Worker.csproj
```

## Current limitations

- Only `windows.local-host-inventory` version `1.0.0` is a production automation package. It has no parameters, no remoting, no credentials, no network calls, and no side effects.
- The package supports only `ExecutionPhase.DryRun`. Its successful read-only dry run moves directly to `Completed`; it does not use approval or Execute states.
- `Automation:LocalHostInventory:Enabled` and `RegisterOnStartup` both default to `false`. Enabling requires explicit, fully qualified non-overlapping `PowerShellExecution:AllowedScriptRoot` and `WorkingRoot` values.
- Candidate discovery is constrained by `(JobWorkKind, ScriptVersionId)`. Unsupported versions remain queued and are not repeatedly claimed.
- The inventory JSON is bounded process output only. It is neither logged nor persisted; durable report storage and rich reporting are later work.
- No authentication or authorization model is complete.
- Approval fingerprints are supplied and validated structurally, but trusted fingerprint calculation is future work.
- Windows identities compare case-insensitively in Phase 2; future authentication should map users to stable SIDs or equivalent principal identifiers.
- Secure parameters store only credential-reference IDs. External credential lookup and secret retrieval remain future Infrastructure work.
- Credential-reference persistence stores a provider-scoped external identifier and a SHA-256 lookup hash, never raw credential material.
- Job parameter type and sensitivity are never trusted from stored job-parameter metadata; responses and audit classification derive from the pinned immutable `ScriptParameterDefinition`.
- Null, empty, and whitespace parameter input is one canonical absent value. If the pinned definition permits absence, the draft removes the explicit binding, leaves any definition-owned default in place, skips credential lookup, and writes a bounded `JobParameterCleared` audit event without the prior value.
- Domain aggregate operations validate every proposed value before changing scalar, collection, timestamp, or child state. In particular, `ScriptDefinition.UpdateDetails` applies display name, description, and timestamp atomically.
- Deployment documentation is planning-only.
- No authentication, authorization, production deployment, service installation, external secret retrieval, report persistence, or operating-system sandbox is provided by Phase 6.
- The project is not production-ready.

See the [architecture](docs/architecture.md), [PowerShell execution boundary](docs/powershell-execution-boundary.md), [worker queue](docs/worker-queue.md), [worker leases](docs/worker-leases.md), [security properties](docs/security.md), and [ADR 0007](docs/decisions/0007-first-production-automation-package.md).
