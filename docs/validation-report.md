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

# Phase 2 validation

Validation date: 2026-07-28. All times are America/Chicago (`-05:00`). Working directory for every command below was `C:\Users\mez\Documents\WindowsScriptRunner`.

## Baseline

The repository started clean on `main`, tracking `origin/main`, with all 13 expected projects and the Phase 1 dependency directions intact.

### Baseline restore

- Command: `dotnet restore`
- Start time: `2026-07-28T14:31:17.2210828-05:00`
- End time: `2026-07-28T14:31:19.9592172-05:00`
- Outcome: All projects were up to date.
- Important output: `All projects are up-to-date for restore.`
- Limitations: No external infrastructure was contacted by application code.
- Status: **Passed**

### Baseline Release build

- Command: `dotnet build --configuration Release`
- Start time: `2026-07-28T14:31:24.0046421-05:00`
- End time: `2026-07-28T14:31:28.7703264-05:00`
- Outcome: Phase 1 compiled with 0 warnings and 0 errors.
- Important output: `Build succeeded.`
- Limitations: Compilation is not deployment validation.
- Status: **Passed**

### Baseline tests

- Command: `dotnet test --configuration Release`
- Start time: `2026-07-28T14:31:33.5804634-05:00`
- End time: `2026-07-28T14:31:41.5643424-05:00`
- Outcome: All 16 Phase 1 tests passed.
- Important output: 0 failed and 0 skipped.
- Limitations: No database or external integration was exercised.
- Status: **Passed**

## Final prerequisites and restore

### .NET SDK

- Command: `dotnet --info`
- Start time: `2026-07-28T14:44:33.3456373-05:00`
- End time: `2026-07-28T14:44:33.9955330-05:00`
- Outcome: Repository selected stable SDK 10.0.302 and host 10.0.10.
- Important output: `global.json` resolved from the repository.
- Limitations: Optional workloads are not installed or required.
- Status: **Passed**

### Restore

- Command: `dotnet restore`
- Start time: `2026-07-28T14:44:38.3669232-05:00`
- End time: `2026-07-28T14:44:40.1433214-05:00`
- Outcome: All projects restored successfully.
- Important output: `All projects are up-to-date for restore.`
- Limitations: No database packages or migrations exist.
- Status: **Passed**

## Final build and tests

### Release build

- Command: `dotnet build --configuration Release`
- Start time: `2026-07-28T14:50:09.7918963-05:00`
- End time: `2026-07-28T14:50:14.6868102-05:00`
- Outcome: All 13 projects compiled with strict analyzers.
- Important output: `Build succeeded. 0 Warning(s), 0 Error(s).`
- Limitations: Persistence implementations are intentionally absent.
- Status: **Passed**

### Test suite

- Command: `dotnet test --configuration Release`
- Start time: `2026-07-28T14:47:53.1840583-05:00`
- End time: `2026-07-28T14:48:00.8935813-05:00`
- Outcome: All 81 tests passed.
- Important output: Unit 60, Integration 3, Worker 5, Security 11, PowerShell boundary 2; 0 failed and 0 skipped.
- Limitations: Application handler integration uses handwritten in-memory fakes. No SQL integration is claimed.
- Status: **Passed**

After the exact test run, a final `dotnet test --configuration Release --no-build` confirmation ran from `2026-07-28T14:50:14.6879445-05:00` to `2026-07-28T14:50:18.3881818-05:00` following the last DTO refinement. All 81 tests passed again.

## Formatting

### Apply formatting

- Command: `dotnet format`
- Start time: `2026-07-28T14:43:45.4337656-05:00`
- End time: `2026-07-28T14:44:05.5702922-05:00`
- Outcome: Repository formatting and line-ending policy were applied.
- Important output: Exit code 0.
- Limitations: A second focused formatter run followed later test additions.
- Status: **Passed**

### Verify formatting

- Command: `dotnet format --verify-no-changes`
- Start time: `2026-07-28T14:50:26.8699168-05:00`
- End time: `2026-07-28T14:50:46.7289210-05:00`
- Outcome: No formatting changes were required.
- Important output: Exit code 0 with no diagnostics.
- Limitations: Markdown content is reviewed separately.
- Status: **Passed**

## Web startup and endpoints

- Command: `dotnet run --project .\src\WindowsScriptRunner.Web\WindowsScriptRunner.Web.csproj`
- Start time: `2026-07-28T14:44:45.6486607-05:00`
- End time: `2026-07-28T14:45:15.5222035-05:00`
- Outcome: Web started without an unhandled exception; `/`, `/Scripts`, `/Jobs`, `/Workers`, `/Audit`, `/Administration`, and `/health` each returned HTTP 200 at `http://localhost:5093`.
- Important output: `Application started`; health returned a minimal 20-byte body.
- Limitations: The default HTTP profile logged the existing non-fatal inability to infer an HTTPS redirect port. The HTTPS development profile remains configured.
- Status: **Passed**

The exact validation process was stopped. No database connection, PowerShell child process, or sensitive value appeared in logs.

## Worker startup and shutdown

- Command launched by a transient process-control harness: `dotnet run --project .\src\WindowsScriptRunner.Worker\WindowsScriptRunner.Worker.csproj`
- Start time: `2026-07-28T14:45:33.543261-05:00`
- End time: `2026-07-28T14:45:39.416880-05:00`
- Outcome: Worker started, emitted a heartbeat, received Ctrl+Break cancellation, and stopped with exit code 0.
- Important output: startup, Phase 1 no-execution limitation, heartbeat, cancellation, and clean-shutdown messages were observed.
- Limitations: An environment-only 1-second heartbeat override was used. Both checked-in settings remain 30 seconds. The transient Python harness was removed.
- Status: **Passed**

No database connection, PowerShell child process, job claim, or job execution occurred.

## Architecture and scope

- Command/evidence: Security tests plus `dotnet list` and targeted package/source/process inspections
- Start time: Covered by the final test run starting `2026-07-28T14:47:53.1840583-05:00`
- End time: Post-test inspections completed before `2026-07-28T14:48:24.4064826-05:00`
- Outcome: Domain and Contracts have no project references; Contracts does not reference Domain; Web still has no Worker or PowerShell reference; no prohibited package or source process-execution call exists; no validation host remains running.
- Important output: 11 security tests passed; heartbeat configuration is 30 seconds in both checked-in files.
- Limitations: These checks do not claim completed authentication, authorization, SQL security, signing, or process isolation.
- Status: **Passed**

## Corrected intermediate failures

- The first focused Domain build failed with one nullable-flow warning in approval fingerprint normalization. The value was normalized once and the focused Domain rebuild passed.
- The first focused UnitTests build failed because one test file omitted the `WindowsScriptRunner.Domain` enum namespace. The import was added and the focused suite passed.
- These intermediate attempts had status **Failed**. No analyzer was disabled and no failure was suppressed.

## Blocked and not-run items

- Blocked final checks: none.
- SQL Server, Entity Framework Core, migrations, repository implementations, PowerShell execution, report generation, authentication, authorization, approval UI, production job processing, and deployment: **NotRun** because they are outside Phase 2 scope.
- No PowerShell script, database migration, or external service integration was run.

# Phase 2 Review Remediation

Validation date: 2026-07-28. All times are America/Chicago (`-05:00`). Every repository command ran from `C:\Users\mez\Documents\WindowsScriptRunner`.

## Baseline before remediation

- `dotnet restore` ran from `2026-07-28T16:18:39.1484049-05:00` to `2026-07-28T16:18:41.8501430-05:00`; all projects were up to date. **Passed**
- `dotnet build --configuration Release` ran from `2026-07-28T16:18:49.5055942-05:00` to `2026-07-28T16:19:00.3545381-05:00`; build succeeded with 0 warnings and 0 errors. **Passed**
- `dotnet test --configuration Release` ran from `2026-07-28T16:19:06.9298977-05:00` to `2026-07-28T16:19:15.2952466-05:00`; all 81 pre-remediation tests passed: Unit 60, Integration 3, Worker 5, Security 11, and PowerShell boundary 2. **Passed**
- `dotnet format --verify-no-changes` ran from `2026-07-28T16:19:19.6360845-05:00` to `2026-07-28T16:19:40.2013213-05:00`; no formatting changes were required. **Passed**

The baseline verifies the reviewed commit before changes; it does not validate the review remediations.

## Final restore

- Command: `dotnet restore`
- Start time: `2026-07-28T16:29:41.3125465-05:00`
- End time: `2026-07-28T16:29:43.1027816-05:00`
- Outcome: All projects were up to date.
- Important output: `All projects are up-to-date for restore.`
- Limitations: Restore validates dependencies, not runtime behavior.
- Status: **Passed**

## Final Release build

- Command: `dotnet build --configuration Release`
- Start time: `2026-07-28T16:29:48.5355210-05:00`
- End time: `2026-07-28T16:29:52.6706524-05:00`
- Outcome: All solution projects compiled.
- Important output: `Build succeeded. 0 Warning(s), 0 Error(s).`
- Limitations: No deployment or external infrastructure was exercised.
- Status: **Passed**

## Final test suite

- Command: `dotnet test --configuration Release`
- Start time: `2026-07-28T16:29:57.4725371-05:00`
- End time: `2026-07-28T16:30:05.7569905-05:00`
- Outcome: All 111 tests passed with 0 failures and 0 skips.
- Important output: Unit 79, Integration 3, Worker 7, Security 20, and PowerShell boundary 2.
- Limitations: Integration tests remain in-memory and do not claim SQL, authentication, or PowerShell execution coverage.
- Status: **Passed**

## Formatting

### Apply formatting

- Command: `dotnet format`
- Start time: `2026-07-28T16:30:10.2235112-05:00`
- End time: `2026-07-28T16:30:31.2134019-05:00`
- Outcome: Formatting completed with exit code 0.
- Important output: No diagnostics were emitted.
- Limitations: Markdown is reviewed separately.
- Status: **Passed**

### Verify formatting

- Command: `dotnet format --verify-no-changes`
- Start time: `2026-07-28T16:30:36.4510341-05:00`
- End time: `2026-07-28T16:30:58.2452921-05:00`
- Outcome: No formatting changes were required.
- Important output: Exit code 0 with no diagnostics.
- Limitations: None beyond formatter scope.
- Status: **Passed**

## Post-format build and test confirmation

### No-restore build

- Command: `dotnet build --configuration Release --no-restore`
- Start time: `2026-07-28T16:31:03.5020837-05:00`
- End time: `2026-07-28T16:31:07.4336869-05:00`
- Outcome: All projects compiled after formatting.
- Important output: `Build succeeded. 0 Warning(s), 0 Error(s).`
- Limitations: No package restore was attempted by design.
- Status: **Passed**

### No-build test

- Command: `dotnet test --configuration Release --no-build`
- Start time: `2026-07-28T16:31:12.4669992-05:00`
- End time: `2026-07-28T16:31:16.5062120-05:00`
- Outcome: All 111 tests passed again with 0 failures and 0 skips.
- Important output: Unit 79, Integration 3, Worker 7, Security 20, and PowerShell boundary 2.
- Limitations: Tests used the immediately preceding Release build.
- Status: **Passed**

## Web startup and endpoints

- Command: `dotnet run --no-launch-profile --project .\src\WindowsScriptRunner.Web\WindowsScriptRunner.Web.csproj`, with validation-only `ASPNETCORE_URLS=http://127.0.0.1:5094`
- Start time: `2026-07-28T16:32:20.0070678-05:00`
- End time: `2026-07-28T16:32:41.9819416-05:00`
- Outcome: Web started and listened on `http://127.0.0.1:5094`. `/`, `/Scripts`, `/Jobs`, `/Workers`, `/Audit`, `/Administration`, and `/health` each returned HTTP 200; health returned 20 bytes.
- Important output: `Application started`; every requested page reported status 200.
- Limitations: The validation host could not deliver Ctrl+C to its non-TTY process, so the exact child process was stopped after the requests; the `dotnet run` wrapper consequently returned exit code 1. The application had already passed startup and endpoint validation. The existing HTTPS redirect warning appeared because no HTTPS port was configured for this validation-only HTTP URL.
- Status: **Passed**

Process and source inspections found no remaining validation host, database provider/access call, PowerShell process-launch call, or `System.Management.Automation` use.

## Worker startup and shutdown

- Command: `dotnet run --no-launch-profile --project .\src\WindowsScriptRunner.Worker\WindowsScriptRunner.Worker.csproj`, launched by a transient in-memory Python process-control command
- Start time: `2026-07-28T16:33:00.726419-05:00`
- End time: `2026-07-28T16:33:06.499905-05:00`
- Outcome: Worker started, emitted a heartbeat, received a real Ctrl+Break cancellation, logged requested cancellation and a clean stop, and exited with code 0.
- Important output: `Worker heartbeat`, `Worker cancellation requested.`, and `Windows Script Runner worker stopped cleanly.`
- Limitations: An environment-only one-second heartbeat interval was used. No file was created by the process-control command, and checked-in configuration remains unchanged.
- Status: **Passed**

No database access, PowerShell child process, job claim, or script execution occurred.

## Architecture and remediation coverage

- Command/evidence: Final Security and Unit test results plus source inspection
- Start time: Covered by the final test run starting `2026-07-28T16:29:57.4725371-05:00`
- End time: Final source/process inspection completed after worker validation.
- Outcome: Source `.csproj` files match exact dependency allowlists; Domain and Contracts have zero project references; Web has no Worker or PowerShell reference. Regression tests cover protected transitions, trusted approval/read-only policy, validation-before-mutation, application success-only auditing/commit, and worker exception propagation/logging/disposal.
- Important output: Security 20 passed and Unit 79 passed.
- Limitations: These tests do not claim Phase 3 persistence reconstruction or production authorization controls.
- Status: **Passed**

## Corrected intermediate failures and blocked harness attempts

- The first focused worker test run had 1 failure and 6 passes because `BackgroundService.StartAsync` does not necessarily surface a background execution fault. The test was corrected to await `ExecuteTask`, and the focused worker suite then passed 7 of 7. The worker implementation was not weakened. Initial status: **Failed**; corrected status: **Passed**.
- A targeted architecture command named a nonexistent `WindowsScriptRunner.ArchitectureTests` project. The architecture tests actually reside in `WindowsScriptRunner.SecurityTests`; that project was run immediately and passed 20 of 20. Mistyped command status: **Failed**; required architecture validation status: **Passed**.
- Direct TTY launch and two alternative Web process-control harnesses were rejected by the command host before any process started. A supported long-running command session was used instead and all endpoints passed. Harness attempts: **Blocked**; Web validation: **Passed**.

## Failed, blocked, and not-run summary

- Required final checks: no Failed or Blocked items.
- Corrected or superseded intermediate items are recorded above and were not represented as passing.
- SQL Server, Entity Framework Core, migrations, repository implementations, PowerShell execution, authentication, authorization, APIs, UI additions, deployment, and all Phase 3 work: **NotRun** because they are outside this focused Phase 2 remediation.
- GitHub Actions: **NotRun** because PR #1 reports no checks; no CI success is claimed.
