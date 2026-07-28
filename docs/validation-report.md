# Phase 1 validation report

Validation date: 2026-07-28

Unless otherwise noted, commands ran from `C:\Users\mez\Documents\WindowsScriptRunner`.

## Prerequisites

### Git

- Command: `git --version`
- Working directory: `C:\Users\mez\Documents\Script Runner .net application`
- Outcome: Git was available.
- Important output: `git version 2.54.0.windows.1`
- Limitations: Version presence only; no remote operation was attempted.
- Status: **Passed**

### .NET SDK

- Command: `dotnet --info`
- Working directory: repository root (also checked before creation from the original workspace)
- Outcome: The pinned stable .NET 10 SDK was selected.
- Important output: SDK `10.0.302`, host `10.0.10`, `global.json` resolved from the repository.
- Limitations: No optional workloads are installed or required.
- Status: **Passed**

### PowerShell

- Command: `pwsh --version`
- Working directory: `C:\Users\mez\Documents\Script Runner .net application`
- Outcome: PowerShell 7 was available.
- Important output: `PowerShell 7.6.4`
- Limitations: No PowerShell scripts were executed.
- Status: **Passed**

## Restore

- Command: `dotnet restore`
- Working directory: repository root
- Outcome: All 13 projects restored; the final run reported all projects up to date.
- Important output: `All projects are up-to-date for restore.`
- Limitations: Uses the configured NuGet sources and local cache.
- Status: **Passed**

## Release build

- Command: `dotnet build --configuration Release`
- Working directory: repository root
- Outcome: All projects compiled.
- Important output: `Build succeeded. 0 Warning(s), 0 Error(s).`
- Limitations: This validates compilation, analyzers, and project references; it is not a deployment build.
- Status: **Passed**

Two focused corrections preceded the passing run:

1. A `--no-restore` build failed because Roslyn cannot enforce IDE0005 during build without global XML documentation generation. IDE0005 was changed from warning to suggestion; analyzers and warnings-as-errors remain enabled.
2. The next build found one missing worker-test namespace import. The import was added, the worker-test project was rebuilt successfully, and then the full build passed.

These intermediate build attempts had status **Failed** and were not reported as passing.

## Tests

- Command: `dotnet test --configuration Release`
- Working directory: repository root
- Outcome: Five test projects ran successfully.
- Important output: 16 passed, 0 failed, 0 skipped: Unit 2, Integration 1, Worker 5, Security 6, PowerShell 2.
- Limitations: Integration coverage is intentionally limited to dependency-injection construction without external infrastructure.
- Status: **Passed**

## Formatting

- Command: `dotnet format --verify-no-changes`
- Working directory: repository root
- Outcome: The final verification produced no violations.
- Important output: Exit code 0 with no diagnostics.
- Limitations: The first verification found only CRLF/UTF-8 normalization issues. `dotnet format --no-restore` corrected those mechanical issues before verification was rerun.
- Status: **Passed**

The initial formatting verification had status **Failed**; the final verification passed after the focused normalization.

## Web startup and endpoints

- Command: `dotnet run --project .\src\WindowsScriptRunner.Web\WindowsScriptRunner.Web.csproj`
- Working directory: repository root
- Outcome: The application built and started without an unhandled exception.
- Important output: `Now listening on: http://localhost:5093`; Development environment.
- Limitations: The default `dotnet run` profile is HTTP and logged that it could not determine an HTTPS redirect port. An HTTPS development profile remains configured at `https://localhost:7127`.
- Status: **Passed**

The following actual URLs returned HTTP 200:

- `http://localhost:5093/`
- `http://localhost:5093/Scripts`
- `http://localhost:5093/Jobs`
- `http://localhost:5093/Workers`
- `http://localhost:5093/Audit`
- `http://localhost:5093/Administration`
- `http://localhost:5093/health`

The health body was a minimal 20-byte success response. The exact validation process was stopped afterward. No database connection, PowerShell process launch, or secret appeared in the implementation or startup output.

## Worker startup and shutdown

- Command launched by the temporary validation harness: `dotnet run --project .\src\WindowsScriptRunner.Worker\WindowsScriptRunner.Worker.csproj`
- Working directory: repository root
- Outcome: The worker started, logged its Phase 1 limitation, emitted a heartbeat, received a real console Ctrl+Break cancellation, and stopped with exit code 0.
- Important output: `Windows Script Runner worker started.`, `Job execution is not implemented`, `Worker heartbeat`, `Worker cancellation requested.`, and `Windows Script Runner worker stopped cleanly.`
- Limitations: The validation process used an environment-only interval override of 1 second. Both checked-in configuration files remain at 30 seconds. The transient Python process-control harness was removed after validation.
- Status: **Passed**

An earlier terminal session could not forward Ctrl+C, so that exact process was stopped by its resolved PID and had status **NotRun** for graceful cancellation. The subsequent Ctrl+Break validation completed the required shutdown check successfully.

## Architecture and scope checks

- Commands: `dotnet sln .\WindowsScriptRunner.sln list`, `dotnet list <project> reference`, and targeted `rg`/process inspections
- Working directory: repository root
- Outcome: 13 projects are in the solution; required reference directions are present; tests enforce critical boundaries.
- Important output: Domain and Contracts have no project references; Web has no direct Worker or PowerShell reference; no prohibited package, secret marker, command-execution code, or leftover validation process was found.
- Limitations: Architectural tests inspect compiled direct assembly references and intentionally do not claim runtime security hardening.
- Status: **Passed**

## Failed, blocked, and not-run summary

- Final required checks: no Failed or Blocked items.
- Corrected intermediate checks: two build attempts and the first format verification failed, as documented above.
- A combined background web harness and TTY allocation were blocked by the command host; the exact web command was then validated through a supported managed session.
- Production deployment, database integration, PowerShell execution, authentication, authorization, job processing, and later roadmap phases: **NotRun** because they are outside Phase 1 scope.
