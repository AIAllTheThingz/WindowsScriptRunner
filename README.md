# Windows Script Runner

Windows Script Runner is a Windows-hosted .NET application foundation for future controlled automation. The repository is currently in **Phase 1: Repository and solution scaffolding**.

## Status

The solution contains a Razor Pages web scaffold, a configurable heartbeat worker, architectural class-library boundaries, and meaningful scaffold tests. No operational automation features are implemented.

## Solution structure

- `src/WindowsScriptRunner.Web` — Razor Pages UI and health endpoint
- `src/WindowsScriptRunner.Worker` — cancellation-aware worker heartbeat
- `src/WindowsScriptRunner.Application` — future use cases and orchestration
- `src/WindowsScriptRunner.Domain` — future independent domain model
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
- Deployment documentation is planning-only.
- The project is not production-ready.

The next implementation phase is **Phase 2: Domain and Application Contracts**.
