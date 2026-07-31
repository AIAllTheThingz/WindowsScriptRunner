# Windows Script Runner

Windows Script Runner is a Windows-hosted .NET application for controlled automation. Phase 8 is committed on its review branch and awaits review; it has not been merged into `main`, deployed, or rolled out.

## Current status

The solution includes:

- an independent Domain model and Application use cases;
- SQL Server persistence, migrations, health checks, and transactional auditing;
- a fenced, lease-backed Worker queue with SQL-authoritative coordination time;
- an isolated PowerShell 7 child-process boundary;
- one reviewed, hash-pinned production package, `windows.local-host-inventory` version `1.0.0`; and
- strict Local Host Inventory parsing with immutable typed report persistence; and
- a Negotiate-protected Razor Pages portal with SID-based authorization, safe typed inventory views, and trusted approval/rejection workflow.

The inventory package is ReadOnly, local-only, parameterless, and DryRun-only. Its successful result is validated against the exact schema and stored as one typed SQL report in the same transaction that completes the job, removes the lease, and records bounded audit metadata. Raw stdout, stderr, and arbitrary JSON are not persisted.

Phases 1–7 are implemented, validated, and merged into `main`. Phase 8 is implemented and validated on its review branch. Phase 9 remains production hardening and deployment.

## Solution structure

- `src/WindowsScriptRunner.Web` — Razor Pages shell and liveness/readiness endpoints
- `src/WindowsScriptRunner.Worker` — registration, heartbeat, queue polling, lease renewal/recovery, and dispatch
- `src/WindowsScriptRunner.Application` — use-case handlers and persistence, audit, clock, and report abstractions
- `src/WindowsScriptRunner.Automation` — reviewed package catalog, registration, PowerShell orchestration, and production handler
- `src/WindowsScriptRunner.Domain` — aggregates, lifecycle rules, identifiers, and immutable report model
- `src/WindowsScriptRunner.Infrastructure` — EF Core SQL Server persistence, repositories, migrations, and health checks
- `src/WindowsScriptRunner.Contracts` — immutable transport DTOs
- `src/WindowsScriptRunner.PowerShell` — bounded out-of-process PowerShell 7 execution
- `src/WindowsScriptRunner.Reporting` — strict inventory parsing, validation, canonicalization, and digest calculation
- `tests` — unit, security, Worker, SQL Server, integration, and real PowerShell tests
- `automation` — reserved repository-maintenance automation area
- `deployment` — Phase 9 deployment planning and status
- `docs` — architecture, security, operations, ADRs, roadmap, and validation evidence

## Prerequisites

- Git
- Stable .NET 10 SDK
- PowerShell 7.4 or later for real execution tests and the reviewed package
- SQL Server; SQL Server LocalDB is supported for development and tests

## Validate the solution

```powershell
dotnet tool restore
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
dotnet format --verify-no-changes
dotnet tool run dotnet-ef migrations has-pending-model-changes --project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj --startup-project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj --configuration Release --no-build
```

The Phase 8 validation evidence and final test count are recorded in [validation report](docs/validation-report.md).

See [development setup](docs/development-setup.md) for local configuration and startup instructions.

## Operational defaults

- `Persistence:ApplyMigrationsOnStartup` is `false`.
- `Automation:LocalHostInventory:Enabled` is `false`.
- `Automation:LocalHostInventory:RegisterOnStartup` is `false`.
- Production Worker identity requires a stable non-empty `Worker:NodeId`.
- The trusted script root and execution working root must be absolute, local, and non-overlapping.
- Web authenticates with Windows Negotiate. Configure only approved Windows group SIDs through protected environment-specific configuration; `AdministratorGroupSids` must be non-empty outside tests.

Apply reviewed migrations before production startup. Startup migration is an explicit controlled-environment option, not the production default.

## Current limitations

- Only `windows.local-host-inventory` version `1.0.0` is executable.
- The package has no parameters, credentials, remoting, network access, or side effects.
- Queue routing is constrained to the exact `(JobWorkKind, ScriptVersionId)` supported route.
- Web exposes only authenticated, authorized Local Host Inventory list/lookup/detail views backed by a safe typed view model. It has no generic report endpoint or raw-output download.
- Windows Negotiate, stable SID identity mapping, authorization, trusted approval fingerprints, and approval/rejection workflow are implemented; IIS/Kerberos/SPN/HTTPS deployment validation is not.
- External secret retrieval and injection are not implemented.
- There is no generic package discovery, arbitrary script upload, generic reporting, or operating-system sandbox.
- IIS configuration, Windows Service installation, production SQL rollout, backup rehearsal, and operational deployment automation are not implemented.
- There is no password, application session, account provisioning, identity federation, or server-side Windows sign-out; Windows-session sign-out guidance is provided instead.
- The product is not production-ready.

## Documentation

Start with the [documentation index](docs/README.md), [roadmap](docs/roadmap.md), [Windows authentication](docs/windows-authentication.md), [authorization matrix](docs/authorization-matrix.md), [approval workflow](docs/approval-workflow.md), [architecture](docs/architecture.md), [security model](docs/security.md), and [validation report](docs/validation-report.md).
