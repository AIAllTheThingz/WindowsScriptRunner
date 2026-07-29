# Windows Script Runner

Windows Script Runner is a Windows-hosted .NET application foundation for future controlled automation. **Phase 3: SQL Server Persistence is implemented on this branch and pending review.**

## Status

The solution contains the validated Phase 2 domain/application model plus an EF Core SQL Server persistence layer. Infrastructure owns explicit persistence entities, mappings, repositories, append-only audit staging, a single unit-of-work commit boundary, optimistic concurrency, migrations, SQL readiness checks, and bounded exception translation. Real SQL Server tests exercise schema creation and rollback, idempotent deployment SQL, repository round trips, constraints, transactions, concurrency, cancellation, health checks, and query shape.

Production startup does not apply migrations by default. Operators must deploy the reviewed migration artifact or explicitly opt into startup migration for a controlled environment.

## Solution structure

- `src/WindowsScriptRunner.Web` — Razor Pages UI and health endpoint
- `src/WindowsScriptRunner.Worker` — cancellation-aware worker heartbeat
- `src/WindowsScriptRunner.Application` — use-case handlers and persistence/audit abstractions
- `src/WindowsScriptRunner.Domain` — independent aggregates, lifecycle rules, and value objects
- `src/WindowsScriptRunner.Infrastructure` — EF Core SQL Server persistence, repositories, migrations, health checks, and composition
- `src/WindowsScriptRunner.Contracts` — shared public request and response contracts
- `src/WindowsScriptRunner.PowerShell` — future isolated execution boundary
- `src/WindowsScriptRunner.Reporting` — future report generation
- `tests` — unit, integration scaffold, real SQL Server, worker, security, and PowerShell boundary tests
- `automation`, `deployment`, `docs` — future operational assets and documentation

## Prerequisites

- Git
- Stable .NET 10 SDK
- PowerShell 7
- SQL Server; SQL Server LocalDB is supported for development and tests

## Commands

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
dotnet format --verify-no-changes
dotnet tool restore
dotnet ef database update --project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj --startup-project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj
dotnet run --project .\src\WindowsScriptRunner.Web\WindowsScriptRunner.Web.csproj
dotnet run --project .\src\WindowsScriptRunner.Worker\WindowsScriptRunner.Worker.csproj
```

## Current limitations

- No PowerShell scripts are executed.
- No production job processing exists.
- No authentication or authorization model is complete.
- Approval fingerprints are supplied and validated structurally, but trusted fingerprint calculation is future work.
- Windows identities compare case-insensitively in Phase 2; future authentication should map users to stable SIDs or equivalent principal identifiers.
- Secure parameters store only credential-reference IDs. External credential lookup and secret retrieval remain future Infrastructure work.
- Credential-reference persistence stores a provider-scoped external identifier and a SHA-256 lookup hash, never raw credential material.
- Job parameter type and sensitivity are never trusted from stored job-parameter metadata; responses and audit classification derive from the pinned immutable `ScriptParameterDefinition`.
- Null, empty, and whitespace parameter input is one canonical absent value. If the pinned definition permits absence, the draft removes the explicit binding, leaves any definition-owned default in place, skips credential lookup, and writes a bounded `JobParameterCleared` audit event without the prior value.
- Domain aggregate operations validate every proposed value before changing scalar, collection, timestamp, or child state. In particular, `ScriptDefinition.UpdateDetails` applies display name, description, and timestamp atomically.
- Deployment documentation is planning-only.
- The project is not production-ready.

See [SQL Server persistence](docs/sql-server-persistence.md), [database schema](docs/database-schema.md), and [database migrations](docs/database-migrations.md) for Phase 3 operational details.
