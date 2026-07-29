# Windows Script Runner

Windows Script Runner is a Windows-hosted .NET application foundation for future controlled automation. The repository has completed **Phase 2: Domain and Application Contracts**.

## Status

The solution contains a validated domain model, application commands and queries, public DTOs, a Razor Pages scaffold, a configurable heartbeat worker, architectural boundaries, and meaningful tests. Phase 2 review remediation protects evidence-bearing lifecycle transitions, completes active execution attempts only through the execution-outcome operation, derives approval and read-only rules from an immutable submission-time policy snapshot, requires Execute-capable script versions to also support DryRun, enforces the requested phase selected at submission, rejects undefined domain enum values at aggregate and application boundaries, validates aggregate changes before mutation, stores job parameters as name/value bindings whose type and sensitivity always come from the pinned script version, requires credential-reference IDs for secure parameters, keeps audit metadata bounded, and keeps unexpected worker failures observable. No persistence or operational automation is implemented.

## Solution structure

- `src/WindowsScriptRunner.Web` — Razor Pages UI and health endpoint
- `src/WindowsScriptRunner.Worker` — cancellation-aware worker heartbeat
- `src/WindowsScriptRunner.Application` — use-case handlers and persistence/audit abstractions
- `src/WindowsScriptRunner.Domain` — independent aggregates, lifecycle rules, and value objects
- `src/WindowsScriptRunner.Infrastructure` — future external concerns
- `src/WindowsScriptRunner.Contracts` — future shared contracts
- `src/WindowsScriptRunner.PowerShell` — future isolated execution boundary
- `src/WindowsScriptRunner.Reporting` — future report generation
- `tests` — unit, integration scaffold, worker, security, and PowerShell boundary tests
- `automation`, `deployment`, `docs` — future operational assets and documentation

## Prerequisites

- Git
- Stable .NET 10 SDK
- PowerShell 7

## Commands

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
dotnet format --verify-no-changes
dotnet run --project .\src\WindowsScriptRunner.Web\WindowsScriptRunner.Web.csproj
dotnet run --project .\src\WindowsScriptRunner.Worker\WindowsScriptRunner.Worker.csproj
```

## Current limitations

- No database has been implemented.
- No PowerShell scripts are executed.
- No production job processing exists.
- No authentication or authorization model is complete.
- Approval fingerprints are supplied and validated structurally, but trusted fingerprint calculation is future work.
- Windows identities compare case-insensitively in Phase 2; future authentication should map users to stable SIDs or equivalent principal identifiers.
- Secure parameters store only credential-reference IDs. External credential lookup and secret retrieval remain future Infrastructure work.
- Job parameter type and sensitivity are never trusted from stored job-parameter metadata; responses and audit classification derive from the pinned immutable `ScriptParameterDefinition`.
- Deployment documentation is planning-only.
- The project is not production-ready.

The next implementation phase is **Phase 3: SQL Server Persistence**.
