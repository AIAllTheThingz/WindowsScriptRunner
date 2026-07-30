# Validation report

Validation date: 2026-07-28

Unless otherwise noted, commands ran from the repository root.

## Prerequisites

### Git

- Command: `git --version`
- Working directory: original workspace root
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
- Working directory: original workspace root
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

Validation date: 2026-07-28. All times are America/Chicago (`-05:00`). Every command below ran from the repository root.

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

Validation date: 2026-07-28. All times are America/Chicago (`-05:00`). Every repository command ran from the repository root.

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

# Phase 2 Fifth Review Remediation

All commands in this section ran from the repository root on branch `agent/phase-2-domain-application-contracts`. This remediation fixes optional parameter clearing and atomic script-definition detail updates without adding persistence, PowerShell execution, authentication, API, UI, deployment, or Phase 3 behavior.

## Baseline before editing

- Command: `dotnet restore`
- Start time: `2026-07-28T22:22:56.0081628-05:00`
- End time: `2026-07-28T22:22:58.6427169-05:00`
- Outcome: All projects were already restored.
- Status: **Passed**

- Command: `dotnet build --configuration Release`
- Start time: `2026-07-28T22:22:58.6595759-05:00`
- End time: `2026-07-28T22:23:09.5218543-05:00`
- Outcome: Build succeeded with 0 warnings and 0 errors.
- Status: **Passed**

- Command: `dotnet test --configuration Release`
- Start time: `2026-07-28T22:23:09.5232281-05:00`
- End time: `2026-07-28T22:23:18.0498613-05:00`
- Outcome: 222 tests passed: Unit 188, Security 22, Integration 3, Worker 7, PowerShell 2; 0 failed and 0 skipped.
- Status: **Passed**

- Command: `dotnet format --verify-no-changes`
- Start time: `2026-07-28T22:23:18.0508756-05:00`
- End time: `2026-07-28T22:23:38.8227851-05:00`
- Outcome: No formatting changes were required.
- Status: **Passed**

## Findings and adjacent mutation audit

- Optional-parameter clearing: null, empty, and whitespace are canonical absence. The pinned `ScriptParameterDefinition` accepts or rejects absence before any type parsing or credential lookup. Accepted absence removes the explicit binding through draft-only `Job.ClearParameterValue`; a clear is intentionally timestamped/audited even when already absent. Required absence without a default fails before mutation, repository update, success audit, commit, or credential lookup. A definition-owned default is never copied into `JobParameter`.
- SecureReference ordering: only a present, pinned-definition-valid canonical non-empty `CredentialReferenceId` reaches repository lookup. Missing and disabled references remain rejected. Clear audit data contains only parameter name, pinned type/sensitivity, binding-existed, and false value/reference-present flags.
- Atomic details: `ScriptDefinition.UpdateDetails` validates timestamp, display name, and description into locals before assigning any property. Description remains optional; null normalizes to empty under the existing contract.
- Additional confirmed same-family defect fixed: direct `Job.SetParameterValue` calls could retain canonical absence as an explicit binding, and `JobParameter` could represent an absent explicit value. `SetParameterValue` now routes absence through aggregate clearing, while `JobParameter` rejects absent explicit bindings.
- Methods reviewed and changed: `ScriptDefinition.UpdateDetails`, `Job.SetParameterValue`; `Job.ClearParameterValue` was added.
- Methods reviewed with no change required: `ScriptDefinition.Enable`, `Disable`, and `AddVersion`; `ScriptVersion.AddParameterDefinition` and `Publish`; `Job.AddTarget`, `RemoveTarget`, `RemoveParameter`, `UpdateDescription`, `SetChangeReference`, `Submit`, every explicit lifecycle operation, `RecordApproval`, `RecordRejection`, `StartExecutionAttempt`, and `RecordTerminalExecutionOutcome`; `JobExecution.Start` and `Complete`; `WorkerNode.RegisterCapability`, `Enable`, `Disable`, and `RecordHeartbeat`; `CredentialReference.Enable` and `Disable`. These methods already validate all potentially throwing inputs before scalar, collection, status, timestamp, or child-state mutation.
- Parameter trust boundary reconfirmed: `JobParameter` contains only `Name` and `SerializedValue`; no public constructor accepts `ScriptParameterType` or sensitivity; set/query handlers use the pinned version; sensitive draft/submitted/terminal values remain redacted; inconsistent bindings fail closed; `ToString` omits values.
- Status: **Passed**

## Focused remediation coverage

- Command: `dotnet test .\tests\WindowsScriptRunner.UnitTests\WindowsScriptRunner.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~Parameter|FullyQualifiedName~SecureReference|FullyQualifiedName~Clear"`
- Initial start/end: `2026-07-28T22:28:49.7418553-05:00` to `2026-07-28T22:29:00.1774481-05:00`
- Initial outcome: 65 tests passed before the explicit definition-default restoration test was added.
- Final start/end: `2026-07-28T22:30:41.8820752-05:00` to `2026-07-28T22:30:50.4489949-05:00`
- Final outcome: 66 tests passed, 0 failed, 0 skipped.
- Important coverage: null/empty/whitespace optional SecureReference clearing; no credential lookup; response/audit omission of prior IDs; all optional parameter types; idempotent clearing; definition-owned defaults; required SecureReference no-mutation rejection; draft-only clearing; canonical present IDs; existing missing/disabled/enabled reference behavior; cancellation propagation; bounded audits.
- Status: **Passed**

- Command: `dotnet test .\tests\WindowsScriptRunner.UnitTests\WindowsScriptRunner.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~ScriptDefinition|FullyQualifiedName~UpdateDetails|FullyQualifiedName~Atomic"`
- Initial start/end: `2026-07-28T22:29:05.8883814-05:00` to `2026-07-28T22:29:13.4694656-05:00`
- Final start/end: `2026-07-28T22:30:50.4652415-05:00` to `2026-07-28T22:30:58.0881853-05:00`
- Outcome: 13 tests passed on both runs, 0 failed, 0 skipped.
- Important coverage: valid atomic updates; null/empty/whitespace and oversized display names; valid display plus oversized description; optional null description; backward timestamps; preservation of enabled state, versions, risk, creator/creation time, and prior details; valid update after a failed attempt.
- Status: **Passed**

- Security tests: `2026-07-28T22:30:58.0895872-05:00` to `2026-07-28T22:31:06.5486720-05:00`; 22 passed, 0 failed, 0 skipped. **Passed**
- Integration tests: `2026-07-28T22:31:06.5496654-05:00` to `2026-07-28T22:31:13.1210089-05:00`; 3 passed, 0 failed, 0 skipped. **Passed**
- Worker tests: `2026-07-28T22:31:13.1219963-05:00` to `2026-07-28T22:31:20.5484588-05:00`; 7 passed, 0 failed, 0 skipped. **Passed**
- PowerShell boundary tests: `2026-07-28T22:31:20.5493612-05:00` to `2026-07-28T22:31:27.1586246-05:00`; 2 passed, 0 failed, 0 skipped. **Passed**

## Final restore, build, test, and formatting

- Command: `dotnet restore`
- Start/end: `2026-07-28T22:31:38.1434752-05:00` to `2026-07-28T22:31:39.9370352-05:00`
- Outcome: All projects were already restored.
- Status: **Passed**

- Command: `dotnet build --configuration Release`
- Start/end: `2026-07-28T22:31:39.9506730-05:00` to `2026-07-28T22:31:43.9438473-05:00`
- Outcome: Build succeeded with 0 warnings and 0 errors.
- Status: **Passed**

- Command: `dotnet test --configuration Release`
- Start/end: `2026-07-28T22:31:43.9452724-05:00` to `2026-07-28T22:31:51.9733500-05:00`
- Outcome: 267 tests passed: Unit 233, Security 22, Integration 3, Worker 7, PowerShell 2; 0 failed and 0 skipped.
- Status: **Passed**

- Command: `dotnet format`
- Start/end: `2026-07-28T22:31:51.9743815-05:00` to `2026-07-28T22:32:13.0275684-05:00`
- Outcome: Formatting completed successfully.
- Status: **Passed**

- Command: `dotnet format --verify-no-changes`
- Start/end: `2026-07-28T22:32:13.0286189-05:00` to `2026-07-28T22:32:34.0153143-05:00`
- Outcome: No formatting changes remained.
- Status: **Passed**

- Command: `dotnet build --configuration Release --no-restore`
- Start/end: `2026-07-28T22:32:34.0162519-05:00` to `2026-07-28T22:32:37.8855862-05:00`
- Outcome: Build succeeded with 0 warnings and 0 errors.
- Status: **Passed**

- Command: `dotnet test --configuration Release --no-build`
- Start/end: `2026-07-28T22:32:37.8865343-05:00` to `2026-07-28T22:32:41.9010234-05:00`
- Outcome: The same 267 tests passed with 0 failed and 0 skipped.
- Status: **Passed**

## Web startup and endpoints

- Command: `dotnet run --project .\src\WindowsScriptRunner.Web\WindowsScriptRunner.Web.csproj`
- Recorded start/end: `2026-07-28T22:36:06.8227011-05:00` to `2026-07-28T22:36:29.8166207-05:00`
- Outcome: `/`, `/Scripts`, `/Jobs`, `/Workers`, `/Audit`, `/Administration`, and `/health` each returned HTTP 200. Startup output contained only normal hosting messages plus the expected HTTPS-port warning; it contained no parameter value, credential value, database access, or PowerShell launch. The verified Web PID was stopped and no port 5093 listener remained.
- Limitations: TTY creation was blocked with Windows `Access is denied`, and this backend cannot deliver Ctrl+C to the non-interactive process. Graceful cancellation was therefore **Blocked**; exact-process cleanup passed. The `dotnet run` wrapper exited nonzero after its verified child was stopped.
- Status: **Passed**

## Worker startup and shutdown

- Command: `dotnet run --project .\src\WindowsScriptRunner.Worker\WindowsScriptRunner.Worker.csproj`, with validation-only `Worker__HeartbeatIntervalSeconds=1`
- Recorded start/end: `2026-07-28T22:36:36.9249245-05:00` to `2026-07-28T22:37:04.5623596-05:00`
- Outcome: Worker logged `Windows Script Runner worker started.`, the phase-neutral `Job execution is not implemented in the current scaffold.`, and repeated heartbeat messages. It did not access a database, launch PowerShell, claim work, or execute a job. The verified Worker PID was stopped and no matching Worker process remained.
- Limitations: The backend cannot deliver Ctrl+C to the non-interactive process. Graceful cancellation was **Blocked**; existing cancellation and unexpected-failure Worker tests passed, and exact-process cleanup passed. The `dotnet run` wrapper exited nonzero after its verified child was stopped.
- Status: **Passed**

## Architecture and source/process validation

- Command: production/source trust-boundary scan, runtime process scan, and `git diff --check`
- Start/end: `2026-07-28T22:37:23.8077230-05:00` to `2026-07-28T22:37:25.1815499-05:00`
- Outcome: No production source match was found for SQL/EF/database implementations, `System.Management.Automation`, or process-launch APIs. No stale `JobParameter.IsSensitive`, `JobParameter.ParameterType`, `GetSafeDisplayValue`, definition-accepting set method, or three-argument `JobParameter` construction was found. The expected pinned lookup/set/clear APIs were present. Port 5093 listeners, Web processes, and Worker processes were all zero. `git diff --check` passed with only normal LF-to-CRLF working-copy warnings.
- Status: **Passed**

## Corrected intermediate failures and blocked items

- The first bundled GitHub thread fetch used the Windows cp1252 default and failed to decode `gh` output. Forcing `PYTHONUTF8=1` corrected the harness issue; the authoritative fetch then confirmed exactly two unresolved current review threads. Initial fetch: **Failed**; corrected fetch: **Passed**.
- The first TTY Web launch was rejected before process creation with `Access is denied`. The non-interactive launch and all routes passed. TTY launch: **Blocked**.
- An auxiliary Web process-inspection expression repeated a no-listener CIM error after the already-verified Web PID had been stopped. It eventually exited, and the dedicated source/process scan confirmed zero listeners/processes. Auxiliary inspection: **Failed**; final cleanup verification: **Passed**.
- A hidden `Start-Process` validation wrapper was rejected by execution policy before launch; no log file or process was created. The timestamped non-interactive validation replaced it. Wrapper: **Blocked**.
- Graceful Ctrl+C delivery for Web and Worker: **Blocked** by the command backend. Exact verified-process cleanup: **Passed**.
- Product code/tests: no failed items remain.
- GitHub Actions: **NotRun** because PR #1 has no attached checks; no CI success is claimed.
- SQL Server, Entity Framework Core, migrations, repository implementations, job polling/claiming, PowerShell execution, authentication, authorization, external vault retrieval, APIs, UI features, deployment automation, and all Phase 3 work: **NotRun** because they are outside this remediation scope.

# Phase 2 Second Review Remediation

Validation date: 2026-07-28. All times are America/Chicago (`-05:00`). Commands ran from the repository root unless otherwise noted.

## Baseline before editing

- Command: `dotnet restore`
- Outcome: All projects were up to date.
- Important output: `All projects are up-to-date for restore.`
- Limitations: Exact start/end timestamps were not printed by the initial shell wrapper for this baseline command.
- Status: **Passed**

- Command: `dotnet build --configuration Release`
- Outcome: All projects compiled before remediation edits.
- Important output: `Build succeeded. 0 Warning(s), 0 Error(s).`
- Limitations: Exact start/end timestamps were not printed by the initial shell wrapper for this baseline command.
- Status: **Passed**

- Command: `dotnet test --configuration Release`
- Outcome: The pre-remediation suite passed with 111 tests.
- Important output: Unit 79, Integration 3, Worker 7, Security 20, PowerShell boundary 2; 0 failed and 0 skipped.
- Limitations: Exact start/end timestamps were not printed by the initial shell wrapper for this baseline command.
- Status: **Passed**

- Command: `dotnet format --verify-no-changes`
- Outcome: No formatting changes were required before remediation edits.
- Important output: Exit code 0 with no diagnostics.
- Limitations: Exact start/end timestamps were not printed by the initial shell wrapper for this baseline command.
- Status: **Passed**

## Focused test runs

- Command: `dotnet test .\tests\WindowsScriptRunner.UnitTests\WindowsScriptRunner.UnitTests.csproj --configuration Release`
- Outcome: Passed after correcting old approval tests to create Execute-requested jobs for approval-only scenarios.
- Important output: 103 passed, 0 failed, 0 skipped.
- Limitations: The first focused unit run after code changes failed 5 approval tests because their setup still used DryRun requests; that failure was corrected by making the requested phase explicit.
- Status: **Passed**

- Command: `dotnet test .\tests\WindowsScriptRunner.SecurityTests\WindowsScriptRunner.SecurityTests.csproj --configuration Release`
- Outcome: Passed.
- Important output: 20 passed, 0 failed, 0 skipped.
- Limitations: Architecture/security tests do not claim production authentication, SQL security, or runtime sandboxing.
- Status: **Passed**

- Command: `dotnet test .\tests\WindowsScriptRunner.WorkerTests\WindowsScriptRunner.WorkerTests.csproj --configuration Release`
- Outcome: Passed.
- Important output: 7 passed, 0 failed, 0 skipped.
- Limitations: Tests validate worker heartbeat/cancellation/failure behavior, not real job processing.
- Status: **Passed**

- Command: `dotnet test .\tests\WindowsScriptRunner.IntegrationTests\WindowsScriptRunner.IntegrationTests.csproj --configuration Release`
- Outcome: Passed.
- Important output: 3 passed, 0 failed, 0 skipped.
- Limitations: Integration tests remain in-memory and database-free.
- Status: **Passed**

## Final restore

- Command: `dotnet restore`
- Start time: `2026-07-28T17:07:31.6663530-05:00`
- End time: `2026-07-28T17:07:33.4990032-05:00`
- Outcome: All projects were up to date.
- Important output: `All projects are up-to-date for restore.`
- Limitations: Restore validates dependencies, not runtime behavior.
- Status: **Passed**

## Final Release build

- Command: `dotnet build --configuration Release`
- Start time: `2026-07-28T17:07:39.1208598-05:00`
- End time: `2026-07-28T17:07:43.5606898-05:00`
- Outcome: All solution projects compiled.
- Important output: `Build succeeded. 0 Warning(s), 0 Error(s).`
- Limitations: No deployment or external infrastructure was exercised.
- Status: **Passed**

## Final test suite

- Command: `dotnet test --configuration Release`
- Start time: `2026-07-28T17:07:48.8589145-05:00`
- End time: `2026-07-28T17:07:57.3061697-05:00`
- Outcome: All 135 tests passed with 0 failures and 0 skips.
- Important output: Unit 103, Integration 3, Worker 7, Security 20, and PowerShell boundary 2.
- Limitations: No SQL, authentication, PowerShell execution, or external service integration is claimed.
- Status: **Passed**

## Formatting

- Command: `dotnet format`
- Start time: `2026-07-28T17:08:02.9891935-05:00`
- End time: `2026-07-28T17:08:24.5118019-05:00`
- Outcome: Formatting completed with exit code 0.
- Important output: No diagnostics were emitted.
- Limitations: Markdown prose is reviewed separately.
- Status: **Passed**

- Command: `dotnet format --verify-no-changes`
- Start time: `2026-07-28T17:08:29.6021841-05:00`
- End time: `2026-07-28T17:08:50.6895115-05:00`
- Outcome: No formatting changes were required.
- Important output: Exit code 0 with no diagnostics.
- Limitations: None beyond formatter scope.
- Status: **Passed**

## Post-format build and test confirmation

- Command: `dotnet build --configuration Release --no-restore`
- Start time: `2026-07-28T17:08:57.2979583-05:00`
- End time: `2026-07-28T17:09:00.5356426-05:00`
- Outcome: All projects compiled after formatting.
- Important output: `Build succeeded. 0 Warning(s), 0 Error(s).`
- Limitations: No restore was attempted by design.
- Status: **Passed**

- Command: `dotnet test --configuration Release --no-build`
- Start time: `2026-07-28T17:09:05.3577811-05:00`
- End time: `2026-07-28T17:09:08.8121034-05:00`
- Outcome: All 135 tests passed again with 0 failures and 0 skips.
- Important output: Unit 103, Integration 3, Worker 7, Security 20, and PowerShell boundary 2.
- Limitations: Tests used the immediately preceding Release build.
- Status: **Passed**

## Web startup and endpoints

- Command: `dotnet run --no-launch-profile --project .\src\WindowsScriptRunner.Web\WindowsScriptRunner.Web.csproj`, with validation-only `ASPNETCORE_URLS=http://127.0.0.1:5094`
- Outcome: Web started and listened on `http://127.0.0.1:5094`. `/`, `/Scripts`, `/Jobs`, `/Workers`, `/Audit`, `/Administration`, and `/health` each returned HTTP 200; health returned 20 bytes.
- Important output: `Now listening on: http://127.0.0.1:5094`; endpoint checks returned status 200 for every requested path.
- Limitations: The command backend could not deliver Ctrl+C, so the exact listening process was resolved by port and stopped after successful endpoint validation; the `dotnet run` wrapper consequently returned exit code 1. The existing HTTPS redirect warning appeared under the validation-only HTTP URL.
- Status: **Passed**

No database connection, PowerShell child process, or sensitive value appeared in startup output. The validation listener was not left running.

## Worker startup and shutdown

- Command: `dotnet run --no-launch-profile --project .\src\WindowsScriptRunner.Worker\WindowsScriptRunner.Worker.csproj`, launched by an in-memory Python process-control harness
- Start time: `2026-07-28T17:10:15.3782448-05:00`
- End time: `2026-07-28T17:10:21.4509137-05:00`
- Outcome: Worker started, logged the Phase 1 no-execution limitation, emitted a heartbeat, received Ctrl+Break cancellation, logged clean shutdown, and exited with code 0.
- Important output: `Worker heartbeat`, `Worker cancellation requested.`, `Windows Script Runner worker stopped cleanly.`, and `WORKER_EXIT_CODE 0`.
- Limitations: An environment-only one-second heartbeat interval was used. Checked-in configuration remains unchanged.
- Status: **Passed**

No database access, PowerShell child process, job claim, or script execution occurred.

## Architecture and remediation coverage

- Command/evidence: Final Security and Unit test results plus source/process inspection
- Outcome: Source `.csproj` allowlists remain enforced; Domain and Contracts have no project references; Web has no Worker or PowerShell reference; no database or PowerShell process-launch implementation was found. Tests cover case-insensitive identity equality, requested-phase enforcement, secure credential-reference validation, bounded audit metadata, strong ID default-value protection, duplicate script-version IDs, disabled submission, and the prior remediation protections.
- Important output: Security 20 passed and Unit 103 passed.
- Limitations: These tests do not claim Phase 3 persistence, production authentication/authorization, external vault retrieval, script execution, or runtime governance for already-submitted jobs after script disable.
- Status: **Passed**

## Failed, blocked, and not-run summary

- Required final checks: no Failed or Blocked items.
- Corrected intermediate item: one focused unit run failed because existing approval tests still created DryRun-requested jobs; tests were corrected to request Execute for approval scenarios and the unit suite then passed.
- Web process graceful Ctrl+C delivery through the command backend was **Blocked**; endpoint validation passed and the exact process was stopped by resolved listening PID.
- SQL Server, Entity Framework Core, migrations, repository implementations, PowerShell execution, authentication, authorization, APIs, Razor Page feature work, deployment, and all Phase 3 work: **NotRun** because they are outside this remediation scope.
- GitHub Actions: **NotRun** because PR #1 reports no checks; no CI success is claimed.

# Phase 2 Third Review Remediation

Validation date: 2026-07-28. All times are America/Chicago (`-05:00`). Commands ran from the repository root unless otherwise noted.

## Baseline before editing

- Command: `dotnet restore`
- Start time: `2026-07-28T17:31:20.8718920-05:00`
- End time: `2026-07-28T17:31:23.5514117-05:00`
- Outcome: All projects were up to date before third-review remediation edits.
- Important output: `All projects are up-to-date for restore.`
- Limitations: Restore validates dependencies, not runtime behavior.
- Status: **Passed**

- Command: `dotnet build --configuration Release`
- Start time: `2026-07-28T17:31:27.3168095-05:00`
- End time: `2026-07-28T17:31:38.3608159-05:00`
- Outcome: All projects compiled before third-review remediation edits.
- Important output: `Build succeeded. 0 Warning(s), 0 Error(s).`
- Limitations: Compilation is not deployment validation.
- Status: **Passed**

- Command: `dotnet test --configuration Release`
- Start time: `2026-07-28T17:31:42.8665896-05:00`
- End time: `2026-07-28T17:31:51.4252208-05:00`
- Outcome: The pre-remediation suite passed with 135 tests.
- Important output: Unit 103, Integration 3, Worker 7, Security 20, and PowerShell boundary 2; 0 failed and 0 skipped.
- Limitations: This baseline validates the reviewed commit before the third-review fixes.
- Status: **Passed**

- Command: `dotnet format --verify-no-changes`
- Start time: `2026-07-28T17:31:55.4518574-05:00`
- End time: `2026-07-28T17:32:16.5567515-05:00`
- Outcome: No formatting changes were required before remediation edits.
- Important output: Exit code 0 with no diagnostics.
- Limitations: Markdown prose is reviewed separately.
- Status: **Passed**

## Focused remediation coverage

- Command: `dotnet test .\tests\WindowsScriptRunner.UnitTests\WindowsScriptRunner.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~Execution|FullyQualifiedName~Transition|FullyQualifiedName~ApplicationHandler"`
- Outcome: Passed.
- Important output: 66 passed, 0 failed, 0 skipped.
- Coverage: Active execution attempts cannot be orphaned by direct terminal transitions; terminal execution outcomes complete the active `JobExecution` and parent `Job` together; application handlers reject generic terminalization for active execution attempts.
- Limitations: Focused filter does not include all enum or script-version tests.
- Status: **Passed**

- Command: `dotnet test .\tests\WindowsScriptRunner.UnitTests\WindowsScriptRunner.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~Script|FullyQualifiedName~Enum|FullyQualifiedName~Identifier"`
- Outcome: Passed.
- Important output: 170 passed, 0 failed, 0 skipped.
- Coverage: Execute-capable script versions require DryRun support before publication and submission; undefined `RiskLevel`, `ExecutionPhase`, `ReportFormat`, `ScriptParameterType`, `ApprovalDecision`, `ExecutionOutcome`, and `JobStatus` values are rejected at domain or application boundaries before mutation.
- Limitations: Focused filter intentionally overlaps broader unit coverage.
- Status: **Passed**

- Command: `dotnet test .\tests\WindowsScriptRunner.IntegrationTests\WindowsScriptRunner.IntegrationTests.csproj --configuration Release`
- Start time: `2026-07-28T17:42:59.6043867-05:00`
- End time: `2026-07-28T17:43:06.2294612-05:00`
- Outcome: Passed.
- Important output: 3 passed, 0 failed, 0 skipped.
- Limitations: Integration tests remain in-memory and database-free.
- Status: **Passed**

- Command: `dotnet test .\tests\WindowsScriptRunner.WorkerTests\WindowsScriptRunner.WorkerTests.csproj --configuration Release`
- Start time: `2026-07-28T17:43:12.1664832-05:00`
- End time: `2026-07-28T17:43:19.5653910-05:00`
- Outcome: Passed.
- Important output: 7 passed, 0 failed, 0 skipped.
- Limitations: Tests validate worker heartbeat/cancellation/failure behavior, not real job processing.
- Status: **Passed**

- Command: `dotnet test .\tests\WindowsScriptRunner.SecurityTests\WindowsScriptRunner.SecurityTests.csproj --configuration Release`
- Start time: `2026-07-28T17:43:23.9164122-05:00`
- End time: `2026-07-28T17:43:32.1571718-05:00`
- Outcome: Passed.
- Important output: 20 passed, 0 failed, 0 skipped.
- Limitations: Architecture/security tests do not claim production authentication, SQL security, or runtime sandboxing.
- Status: **Passed**

- Command: `dotnet test .\tests\WindowsScriptRunner.PowerShellTests\WindowsScriptRunner.PowerShellTests.csproj --configuration Release`
- Start time: `2026-07-28T17:43:36.3882439-05:00`
- End time: `2026-07-28T17:43:42.9758210-05:00`
- Outcome: Passed.
- Important output: 2 passed, 0 failed, 0 skipped.
- Limitations: No script execution is implemented or claimed.
- Status: **Passed**

## Final restore, build, test, and formatting

- Command: `dotnet restore`
- Start time: `2026-07-28T17:43:49.1042827-05:00`
- End time: `2026-07-28T17:43:50.9314859-05:00`
- Outcome: All projects were up to date.
- Important output: `All projects are up-to-date for restore.`
- Limitations: Restore validates dependencies, not runtime behavior.
- Status: **Passed**

- Command: `dotnet build --configuration Release`
- Start time: `2026-07-28T17:43:54.6499405-05:00`
- End time: `2026-07-28T17:43:58.6363462-05:00`
- Outcome: All solution projects compiled.
- Important output: `Build succeeded. 0 Warning(s), 0 Error(s).`
- Limitations: No deployment or external infrastructure was exercised.
- Status: **Passed**

- Command: `dotnet test --configuration Release`
- Start time: `2026-07-28T17:44:02.6817605-05:00`
- End time: `2026-07-28T17:44:10.6732324-05:00`
- Outcome: All 202 tests passed with 0 failures and 0 skips.
- Important output: Unit 170, Integration 3, Worker 7, Security 20, and PowerShell boundary 2.
- Limitations: No SQL, authentication, PowerShell execution, or external service integration is claimed.
- Status: **Passed**

- Command: `dotnet format`
- Outcome: Formatting completed with exit code 0 after line-ending normalization.
- Important output: No diagnostics were emitted.
- Limitations: Markdown prose is reviewed separately.
- Status: **Passed**

- Command: `dotnet format --verify-no-changes`
- Start time: `2026-07-28T17:45:08.3911219-05:00`
- End time: `2026-07-28T17:45:30.0774449-05:00`
- Outcome: No formatting changes were required after normalization.
- Important output: Exit code 0 with no diagnostics.
- Limitations: None beyond formatter scope.
- Status: **Passed**

## Post-format and post-Razor-label confirmation

- Command: `dotnet build --configuration Release --no-restore`
- Start time: `2026-07-28T17:48:13.0118652-05:00`
- End time: `2026-07-28T17:48:16.3949988-05:00`
- Outcome: All projects compiled after formatting and the phase-label wording update.
- Important output: `Build succeeded. 0 Warning(s), 0 Error(s).`
- Limitations: No restore was attempted by design.
- Status: **Passed**

- Command: `dotnet test --configuration Release --no-build`
- Start time: `2026-07-28T17:48:20.1430710-05:00`
- End time: `2026-07-28T17:48:23.6155306-05:00`
- Outcome: All 202 tests passed again with 0 failures and 0 skips.
- Important output: Unit 170, Integration 3, Worker 7, Security 20, and PowerShell boundary 2.
- Limitations: Tests used the immediately preceding Release build.
- Status: **Passed**

- Command: `dotnet format --verify-no-changes`
- Start time: `2026-07-28T17:48:28.2379159-05:00`
- End time: `2026-07-28T17:48:49.7126089-05:00`
- Outcome: No formatting changes were required.
- Important output: Exit code 0 with no diagnostics.
- Limitations: None beyond formatter scope.
- Status: **Passed**

## Web startup and endpoints

- Command: `dotnet run --no-launch-profile --project .\src\WindowsScriptRunner.Web\WindowsScriptRunner.Web.csproj`, with validation-only `ASPNETCORE_URLS=http://127.0.0.1:5094`
- Outcome: Web started and listened on `http://127.0.0.1:5094`. `/health` returned HTTP 200 with `{"status":"Healthy"}` and `/` returned HTTP 200 with 4,996 bytes.
- Important output: `Now listening on: http://127.0.0.1:5094`; health returned 20 bytes.
- Limitations: Direct TTY launch was rejected by the command host before the app started. The command backend could not deliver Ctrl+C to the non-interactive process, so the exact listener PID was resolved by port and stopped after successful endpoint validation; the `dotnet run` wrapper consequently returned exit code 1. The expected HTTPS redirect warning appeared under the validation-only HTTP URL. One first landing-page request command used reserved PowerShell variable `$HOME`, so only the health request succeeded in that command; the landing-page request was immediately rerun with a safe variable and passed.
- Status: **Passed**

No database connection, PowerShell child process, or sensitive value appeared in startup output. The validation listener was not left running.

## Worker startup and shutdown

- Command: `dotnet run --no-launch-profile --project .\src\WindowsScriptRunner.Worker\WindowsScriptRunner.Worker.csproj`, with validation-only `Worker__HeartbeatIntervalSeconds=1`
- Outcome: Worker started, logged the phase-neutral no-execution limitation, and emitted repeated heartbeat messages.
- Important output: `Windows Script Runner worker started.`, `Job execution is not implemented in the current scaffold.`, and `Worker heartbeat`.
- Limitations: The command backend could not deliver Ctrl+C to the non-interactive process. The worker validation process was stopped and a follow-up process check confirmed no Worker validation process remained. One first stop command used an overly broad command-line match and exited nonzero after matching its own shell process; a narrower follow-up check confirmed no actual Worker executable or dotnet run wrapper remained.
- Status: **Passed**

No database access, PowerShell child process, job claim, or script execution occurred.

## Architecture and remediation coverage

- Command/evidence: Final Security and Unit test results plus source/process inspection.
- Start time: Source/process inspection started `2026-07-28T17:48:04.8130298-05:00`
- End time: Source/process inspection ended `2026-07-28T17:48:07.1929143-05:00`
- Outcome: Source `.csproj` allowlists remain enforced; Domain and Contracts have no project references; Web has no Worker or PowerShell reference; no database or PowerShell process-launch implementation was found. Tests cover active execution attempt terminalization, Execute-with-DryRun enforcement, defined-enum guards, requested-phase enforcement, trusted policy capture, secure credential-reference validation, bounded audit metadata, strong ID protections, and the prior remediation protections.
- Important output: Security 20 passed, Unit 170 passed, no web validation listener remained, and no worker validation process remained. The only source scan matches for SQL-client names are the explicit denylist strings in security tests; the `System.Management.Automation` match is historical validation-report prose.
- Limitations: These tests do not claim Phase 3 persistence, production authentication/authorization, external vault retrieval, script execution, or runtime governance for already-submitted jobs after script disable.
- Status: **Passed**

## Corrected intermediate failures and blocked harness attempts

- The first intermediate build after making `JobExecution.Start` and `JobExecution.Complete` internal failed because an existing unit test still called `Complete` directly. The test was corrected to exercise completion through `Job.RecordTerminalExecutionOutcome`. Initial status: **Failed**; corrected status: **Passed**.
- A second intermediate build failed because that converted test call initially omitted the acting user argument. The argument was added and the subsequent build passed. Initial status: **Failed**; corrected status: **Passed**.
- The first `dotnet format --verify-no-changes` after edits failed on line-ending diagnostics introduced by the patched blocks. `dotnet format` normalized the files, and verify then passed. Initial status: **Failed**; corrected status: **Passed**.
- Direct TTY Web launch and Ctrl+C delivery for non-interactive Web/Worker validation processes were **Blocked** by the command backend; startup and endpoint/heartbeat validation passed, and exact process cleanup was confirmed.

## Failed, blocked, and not-run summary

- Required final checks: no Failed items remain.
- Blocked harness capabilities are recorded above and were worked around without weakening product code.
- SQL Server, Entity Framework Core, migrations, repository implementations, PowerShell execution, authentication, authorization, APIs, Razor Page feature work beyond stale phase-label wording, deployment, and all Phase 3 work: **NotRun** because they are outside this remediation scope.
- GitHub Actions: **NotRun** because PR #1 reports no checks; no CI success is claimed.

# Phase 2 Fourth Review Remediation

Validation date: 2026-07-28. All times are America/Chicago (`-05:00`). Commands ran from the repository root unless otherwise noted.

## Baseline before editing

- Command: `dotnet restore`
- Start time: `2026-07-28T20:55:10.7054013-05:00`
- End time: `2026-07-28T20:55:13.4485771-05:00`
- Outcome: All projects were up to date before fourth-review remediation edits.
- Important output: `All projects are up-to-date for restore.`
- Limitations: Restore validates dependencies, not runtime behavior.
- Status: **Passed**

- Command: `dotnet build --configuration Release`
- Start time: `2026-07-28T20:55:20.2367213-05:00`
- End time: `2026-07-28T20:55:31.6495797-05:00`
- Outcome: All projects compiled before fourth-review remediation edits.
- Important output: `Build succeeded. 0 Warning(s), 0 Error(s).`
- Limitations: Compilation is not deployment validation.
- Status: **Passed**

- Command: `dotnet test --configuration Release`
- Start time: `2026-07-28T20:55:38.5276316-05:00`
- End time: `2026-07-28T20:55:47.3676488-05:00`
- Outcome: The pre-remediation suite passed with 202 tests.
- Important output: Unit 170, Integration 3, Worker 7, Security 20, and PowerShell boundary 2; 0 failed and 0 skipped.
- Limitations: This baseline validates the reviewed commit before the fourth-review fixes.
- Status: **Passed**

- Command: `dotnet format --verify-no-changes`
- Start time: `2026-07-28T20:55:52.2703171-05:00`
- End time: `2026-07-28T20:56:13.9573019-05:00`
- Outcome: No formatting changes were required before remediation edits.
- Important output: Exit code 0 with no diagnostics.
- Limitations: Markdown prose is reviewed separately.
- Status: **Passed**

## Focused remediation coverage

- Command: `dotnet test .\tests\WindowsScriptRunner.UnitTests\WindowsScriptRunner.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~Parameter|FullyQualifiedName~GetJob|FullyQualifiedName~ApplicationHandler"`
- Outcome: Passed.
- Important output: 73 passed, 0 failed, 0 skipped.
- Coverage: Tests cover binding-only `JobParameter` storage, absence of public security-metadata constructors, pinned-definition write validation/audit classification, the reviewed metadata-spoofing exploit, draft/submitted/terminal redaction, non-sensitive display, SecureReference redaction, fail-closed unknown/invalid parameter bindings, no-mutation invalid writes, and cancellation propagation to job/script repositories.
- Limitations: Exact start/end timestamps were not printed for this focused command; the complete final suite below has timestamped evidence.
- Status: **Passed**

- Command: `dotnet test .\tests\WindowsScriptRunner.SecurityTests\WindowsScriptRunner.SecurityTests.csproj --configuration Release`
- Outcome: Passed.
- Important output: 22 passed, 0 failed, 0 skipped.
- Coverage: Reflection checks verify `JobParameter` has no public constructor accepting sensitivity or parameter type and `GetJobHandler` depends on the trusted script repository.
- Limitations: Exact start/end timestamps were not printed for this focused command; the complete final suite below has timestamped evidence.
- Status: **Passed**

- Command: `dotnet test .\tests\WindowsScriptRunner.IntegrationTests\WindowsScriptRunner.IntegrationTests.csproj --configuration Release`
- Outcome: Passed.
- Important output: 3 passed, 0 failed, 0 skipped.
- Limitations: Integration tests remain in-memory and database-free.
- Status: **Passed**

- Command: `dotnet test .\tests\WindowsScriptRunner.WorkerTests\WindowsScriptRunner.WorkerTests.csproj --configuration Release`
- Outcome: Passed.
- Important output: 7 passed, 0 failed, 0 skipped.
- Limitations: Tests validate worker heartbeat/cancellation/failure behavior, not real job processing.
- Status: **Passed**

- Command: `dotnet test .\tests\WindowsScriptRunner.PowerShellTests\WindowsScriptRunner.PowerShellTests.csproj --configuration Release`
- Outcome: Passed.
- Important output: 2 passed, 0 failed, 0 skipped.
- Limitations: No script execution is implemented or claimed.
- Status: **Passed**

## Final restore, build, test, and formatting

- Command: `dotnet restore`
- Start time: `2026-07-28T21:03:47.2382151-05:00`
- End time: `2026-07-28T21:03:49.0509678-05:00`
- Outcome: All projects were up to date.
- Important output: `All projects are up-to-date for restore.`
- Limitations: Restore validates dependencies, not runtime behavior.
- Status: **Passed**

- Command: `dotnet build --configuration Release`
- Start time: `2026-07-28T21:03:53.7273957-05:00`
- End time: `2026-07-28T21:03:57.8077375-05:00`
- Outcome: All solution projects compiled.
- Important output: `Build succeeded. 0 Warning(s), 0 Error(s).`
- Limitations: No deployment or external infrastructure was exercised.
- Status: **Passed**

- Command: `dotnet test --configuration Release`
- Start time: `2026-07-28T21:04:02.4559456-05:00`
- End time: `2026-07-28T21:04:10.7381238-05:00`
- Outcome: All 222 tests passed with 0 failures and 0 skips.
- Important output: Unit 188, Security 22, Integration 3, Worker 7, and PowerShell boundary 2.
- Limitations: No SQL, authentication, PowerShell execution, or external service integration is claimed.
- Status: **Passed**

- Command: `dotnet format`
- Start time: `2026-07-28T21:04:16.0446208-05:00`
- End time: `2026-07-28T21:04:37.7319198-05:00`
- Outcome: Formatting completed with exit code 0.
- Important output: No diagnostics were emitted.
- Limitations: Markdown prose is reviewed separately.
- Status: **Passed**

- Command: `dotnet format --verify-no-changes`
- Start time: `2026-07-28T21:04:41.9048222-05:00`
- End time: `2026-07-28T21:05:03.6183075-05:00`
- Outcome: No formatting changes were required.
- Important output: Exit code 0 with no diagnostics.
- Limitations: None beyond formatter scope.
- Status: **Passed**

- Command: `dotnet build --configuration Release --no-restore`
- Start time: `2026-07-28T21:05:11.3644131-05:00`
- End time: `2026-07-28T21:05:15.2157164-05:00`
- Outcome: All projects compiled after formatting.
- Important output: `Build succeeded. 0 Warning(s), 0 Error(s).`
- Limitations: No restore was attempted by design.
- Status: **Passed**

- Command: `dotnet test --configuration Release --no-build`
- Start time: `2026-07-28T21:05:20.3589619-05:00`
- End time: `2026-07-28T21:05:24.4034382-05:00`
- Outcome: All 222 tests passed again with 0 failures and 0 skips.
- Important output: Unit 188, Security 22, Integration 3, Worker 7, and PowerShell boundary 2.
- Limitations: Tests used the immediately preceding Release build.
- Status: **Passed**

## Web startup and endpoints

- Command: `dotnet run --project .\src\WindowsScriptRunner.Web\WindowsScriptRunner.Web.csproj`
- Outcome: Web started and listened on `http://localhost:5093` through launch settings. `/`, `/Scripts`, `/Jobs`, `/Workers`, `/Audit`, `/Administration`, and `/health` each returned HTTP 200.
- Important output: `/` returned 4,997 bytes; `/Scripts` 4,694 bytes; `/Jobs` 4,687 bytes; `/Workers` 4,695 bytes; `/Audit` 4,674 bytes; `/Administration` 4,694 bytes; `/health` 20 bytes.
- Limitations: The command backend could not deliver Ctrl+C to the non-interactive process. The listener disappeared between PID resolution and stop, causing one stale-PID stop attempt to fail after endpoint validation; a follow-up check confirmed no listener remained on port 5093. The expected HTTPS redirect warning appeared under the validation HTTP URL.
- Status: **Passed**

No database connection, PowerShell child process, parameter value, credential-reference value, or sensitive value appeared in startup output. The validation listener was not left running.

## Worker startup and shutdown

- Command: `dotnet run --project .\src\WindowsScriptRunner.Worker\WindowsScriptRunner.Worker.csproj`, with validation-only `Worker__HeartbeatIntervalSeconds=1`
- Outcome: Worker started, logged the phase-neutral no-execution limitation, and emitted repeated heartbeat messages.
- Important output: `Windows Script Runner worker started.`, `Job execution is not implemented in the current scaffold.`, and `Worker heartbeat`.
- Limitations: The command backend could not deliver Ctrl+C to the non-interactive process. The exact Worker executable PID was stopped after successful heartbeat validation, so the wrapper exited nonzero; a follow-up process check confirmed no Worker validation process remained.
- Status: **Passed**

No database access, PowerShell child process, job claim, or script execution occurred.

## Architecture and remediation coverage

- Command/evidence: Final Security and Unit test results plus source/process inspection.
- Start time: Source/process inspection started `2026-07-28T21:07:05.9092490-05:00`
- End time: Source/process inspection ended `2026-07-28T21:07:08.7723520-05:00`
- Outcome: Source `.csproj` allowlists remain enforced; Domain and Contracts have no project references; Web has no Worker or PowerShell reference; no database or PowerShell process-launch implementation was found. `JobParameter` no longer stores `ParameterType` or `IsSensitive`, `GetJobHandler` loads the pinned script version for response classification, and no production code references stale `JobParameter.IsSensitive`, `JobParameter.ParameterType`, or `GetSafeDisplayValue` patterns.
- Important output: Unit 188 passed, Security 22 passed, no web validation listener remained, and no worker validation process remained. The only source scan matches for SQL-client names are explicit denylist strings in security tests; the `System.Management.Automation` matches are historical validation-report prose.
- Limitations: These tests do not claim Phase 3 persistence, production authentication/authorization, external vault retrieval, script execution, or runtime governance for already-submitted jobs after script disable.
- Status: **Passed**

## Corrected intermediate failures and blocked harness attempts

- No product-code or test failures occurred during focused or full validation after the trust-boundary redesign.
- Ctrl+C delivery for non-interactive Web/Worker validation processes was **Blocked** by the command backend; endpoint and heartbeat validation passed, and process cleanup was confirmed.
- One Web cleanup attempt failed because the resolved listener PID no longer existed by the time `Stop-Process` ran. A follow-up port check confirmed no listener remained. Initial cleanup status: **Failed**; final cleanup status: **Passed**.

## Failed, blocked, and not-run summary

- Required final checks: no Failed items remain.
- Blocked harness capabilities are recorded above and were worked around without weakening product code.
- SQL Server, Entity Framework Core, migrations, repository implementations, PowerShell execution, authentication, authorization, APIs, Razor Page feature work, deployment, and all Phase 3 work: **NotRun** because they are outside this remediation scope.
- GitHub Actions: **NotRun** because PR #1 reports no checks; no CI success is claimed.

# Phase 3 SQL Server Persistence

Validation date: 2026-07-29. Times are America/Chicago (`-05:00`). Commands ran from the repository root unless noted.

## Starting gate and baseline

- Phase 2 merge commit `bc7489517097f038fd7b048cfd3df11fdc230c36` was an ancestor of updated `main`; the ancestry command exited 0.
- PR #1 was merged, no Phase 2 PR remained open, `main` matched `origin/main`, and the starting worktree was clean.
- Phase 3 branch: `agent/phase-3-sql-server-persistence`, created from merge commit `bc74895`.
- Baseline `dotnet restore`: `2026-07-29T12:38:58-05:00` to `2026-07-29T12:39:01-05:00`, exit 0, Passed.
- Baseline `dotnet build --configuration Release`: `2026-07-29T12:39:01-05:00` to `2026-07-29T12:39:12-05:00`, exit 0, 0 warnings and 0 errors, Passed.
- Baseline `dotnet test --configuration Release`: `2026-07-29T12:39:12-05:00` to `2026-07-29T12:39:21-05:00`, exit 0, 310 passed, 0 failed, 0 skipped. Counts: Unit 276, Security 22, Integration 3, Worker 7, PowerShell boundary 2.
- Baseline `dotnet format --verify-no-changes`: `2026-07-29T12:39:21-05:00` to `2026-07-29T12:39:43-05:00`, exit 0, Passed.

## Packages and tooling

- EF Core, EF Core Design, EF Core SQL Server, EF health checks, and repository-local `dotnet-ef`: 10.0.10.
- `Microsoft.Data.SqlClient`: 6.1.1, directly referenced only where SQL error classification or real SQL test setup uses its APIs.
- Microsoft configuration, dependency injection, hosting, logging, and options packages added at 10.0.10 through central package management.
- `.config/dotnet-tools.json` is the repository-local tool manifest. No global tool was installed.
- Source boundary tests prove EF/SQL provider packages remain in Infrastructure, Domain and Contracts have no EF attributes, Application/Web/Worker have no `DbContext`, Web/Worker have no direct SqlClient package, and Worker/Infrastructure/PowerShell references remain isolated.

## Schema and persistence design

- `WindowsScriptRunnerDbContext` uses SQL Server, default schema `wsr`, and migration history `wsr.__EFMigrationsHistory`.
- Separate internal persistence entities and explicit Fluent API mappings cover 16 tables: `ScriptDefinitions`, `ScriptVersions`, `ScriptVersionPhases`, `ScriptVersionReportFormats`, `ScriptParameterDefinitions`, `ScriptParameterAllowedValues`, `Jobs`, `JobTargets`, `JobParameters`, `JobExecutions`, `JobApprovals`, `WorkerNodes`, `WorkerCapabilities`, `CredentialReferences`, `AuditEvents`, and `AuditEventProperties`.
- All strong IDs use `uniqueidentifier`; timestamps use `datetimeoffset(7)` and are normalized to UTC on write; SHA-256 metadata uses fixed-length storage; enums use checked stable strings.
- Mutable roots `ScriptDefinitions`, `Jobs`, `WorkerNodes`, and `CredentialReferences` use SQL Server `rowversion`.
- Aggregate-owned children cascade. A composite Job-to-version relationship guarantees the pinned version belongs to the pinned script; Job-to-script, Job-to-version, and execution-to-worker relationships use `NO ACTION`.
- Unique, check, and filtered indexes cover normalized identities, semantic versions, parameter/target/capability/property names, credential provider/hash pairs, job access patterns, audit access patterns, and one active execution per job.
- SQL triggers enforce published Execute-with-DryRun and allowed-values-only-for-Enum rules that cross table boundaries.
- No production seed data, raw credential column, Phase 4 queue table, polling index, lease, claim, or scheduling construct was added.

## Migration generation and inspection

- Migration: `20260729175606_InitialSqlServerPersistence` / `InitialSqlServerPersistence`.
- The generated migration, designer, and model snapshot were inspected for schema, keys, delete behavior, indexes, filter, rowversion, constraints, enum storage, lengths, nullability, triggers, and migration history.
- `dotnet tool run dotnet-ef migrations has-pending-model-changes ... --no-build`: `2026-07-29T13:40:09.7330855-05:00` to `2026-07-29T13:40:14.6204033-05:00`, exit 0, `No changes have been made to the model since the last migration.`
- Repository policy ignores generated `artifacts/`, so the idempotent deployment SQL is intentionally not committed. The SQL tests generate the idempotent script from the migration assembly, verify it contains no server or credentials, and apply it twice.

## Real SQL Server runtime and test databases

- Runtime: installed SQL Server LocalDB instance `(localdb)\MSSQLLocalDB`, accessed with Windows integrated authentication.
- `sqlcmd` and `sqllocaldb` were available. Docker client was present, but the Docker daemon was unavailable; Testcontainers was not needed because real LocalDB was available.
- Each SQL test creates a GUID-named disposable database, applies the real migration, uses isolated contexts/scopes, and deletes the database afterward.
- SQLite and EF InMemory were not used as SQL Server evidence.

## SQL Server integration results

- Explicit command: `dotnet test .\tests\WindowsScriptRunner.SqlServerTests\WindowsScriptRunner.SqlServerTests.csproj --configuration Release --no-build`
- Start: `2026-07-29T13:39:50.0962250-05:00`
- End: `2026-07-29T13:40:00.3671648-05:00`
- Exit: 0
- Result: 19 passed, 0 failed, 0 skipped.
- Migration tests: 2 passed. Empty-database apply, second apply no-op, `wsr` history, table/index/filter/rowversion/FK/trigger metadata, rollback to zero, generated idempotent SQL applied twice, and migration restore all passed.
- Repository and mapping tests: 7 SQL tests plus 7 focused unit mapping tests passed. They cover complete script graphs, exact strong IDs and fields, graph updates without duplicate rows, complete terminal jobs, secure-reference redaction, draft/submitted/approved/executing/terminal states, Validation-only/DryRun-only/ReadOnly completion, optional parameter row removal, Worker, CredentialReference duplicate/collision handling, and append-only audit staging.
- Transaction tests: aggregate plus audit atomic commit and forced audit-constraint rollback both passed. The rollback left neither the aggregate update nor failed audit data.
- Concurrency tests: stale Job, ScriptDefinition, and WorkerNode writes produced bounded `ApplicationConflictException`; winning state remained, and a stale Job audit did not commit.
- Constraint test: one real-SQL test executed 17 rejection scenarios covering case-only duplicate names, semantic versions, parameter/target/binding/execution/capability/property duplicates, one active execution, incomplete execution output, normalized Worker name, invalid enum, timestamp order, partial policy, Enum-only allowed values, and Execute-with-DryRun publication.
- Cancellation, query behavior, composition, and health tests passed. Representative aggregate SQL was bounded, parameterized, and split; readiness was healthy when migrated, unhealthy when migrations were pending, and unhealthy when the database was unavailable.

## Final build, test, and formatting

- `dotnet tool restore`: `2026-07-29T13:37:31.3882708-05:00` to `2026-07-29T13:37:31.9148944-05:00`, exit 0, dotnet-ef 10.0.10 restored.
- `dotnet restore`: `2026-07-29T13:37:37.9751320-05:00` to `2026-07-29T13:37:39.8481196-05:00`, exit 0, all projects up to date.
- `dotnet build --configuration Release`: `2026-07-29T13:37:44.4571597-05:00` to `2026-07-29T13:37:49.2328916-05:00`, exit 0, 0 warnings and 0 errors.
- `dotnet test --configuration Release`: `2026-07-29T13:37:55.7914356-05:00` to `2026-07-29T13:38:10.6788120-05:00`, exit 0, 349 passed, 0 failed, 0 skipped. Counts: Unit 283, Security 35, SQL Server 19, Integration 3, Worker 7, PowerShell boundary 2.
- `dotnet format`: `2026-07-29T13:38:16.6917400-05:00` to `2026-07-29T13:38:41.9290103-05:00`, exit 0.
- `dotnet format --verify-no-changes`: `2026-07-29T13:38:47.9571098-05:00` to `2026-07-29T13:39:13.2315808-05:00`, exit 0.
- `dotnet build --configuration Release --no-restore`: `2026-07-29T13:39:21.1724388-05:00` to `2026-07-29T13:39:25.0247762-05:00`, exit 0, 0 warnings and 0 errors.
- `dotnet test --configuration Release --no-build`: `2026-07-29T13:39:30.4900528-05:00` to `2026-07-29T13:39:41.5073024-05:00`, exit 0, the same 349 passed with 0 failed and 0 skipped.
- Review-remediation revalidation on `2026-07-29T16:36:17-05:00`: Release build passed with 0 warnings and 0 errors; the full suite passed 362 tests (Unit 286, Security 35, SQL Server 29, Integration 3, Worker 7, PowerShell boundary 2); formatting verification passed; and EF reported no pending model changes.

## Web, Worker, and health validation

- A migrated disposable LocalDB database was configured with `ApplyMigrationsOnStartup=false`.
- Web ran from its project content root in Production on validation-only HTTP port 5096. `/`, `/Scripts`, `/Jobs`, `/Workers`, `/Audit`, `/Administration`, `/health`, `/health/live`, and `/health/ready` each returned HTTP 200.
- Migration history remained exactly one after Web startup, proving startup did not reapply a migration.
- After the exact disposable database was dropped, `/health/ready` returned 503 while `/health/live` remained 200.
- Web had zero child processes and zero PowerShell children. Logs contained generic SQL command/health output without connection strings, parameter values, credentials, or the database name.
- Worker ran from its project content root in Production with a one-second validation heartbeat. It resolved Infrastructure, logged the existing no-execution limitation, and emitted heartbeats.
- Worker migration history remained exactly one. It had zero child processes and zero PowerShell children; no polling, claiming, script execution, or database command appeared.
- Existing seven Worker cancellation, pacing, clean-stop, option-validation, and unexpected-failure tests passed in the final suite.
- The exact Web and Worker executable PIDs were resolved and stopped. Port 5096 had no listener, no Worker process remained, and both named disposable databases were dropped.

## Source and security inspection

- Production `SaveChangesAsync` exists only in `SqlUnitOfWork`; repositories and `SqlAuditWriter` never save.
- Sensitive-data logging is explicitly false. `UseSqlServer`, SqlClient APIs, and `DbContext` occur only in Infrastructure production source.
- Production source contains no `EnsureCreated`, `EnsureDeleted`, `HasData`, embedded SQL credentials, direct Web/Worker SQL package, or Phase 4 process-launch implementation.
- The only credential-marker scan matches are intentional domain/security denylist strings and test assertions. The vendor `wwwroot` match is unrelated minified JavaScript.
- No connection string, password, generated database log, environment-specific server, or SQL artifact is staged for commit.

## Corrected intermediate failures and blocked harness attempts

- The first migration command using Web as startup project failed because Web correctly does not reference EF Design. The Infrastructure design-time factory was used instead; migration generation and all migration tests passed.
- Generated migration formatting initially failed the repository file-scoped namespace rule. `dotnet format` corrected it before final validation.
- Initial migration tests exposed a metadata collation conflict and trigger DDL placement inside the idempotent wrapper. The metadata query and trigger creation form were corrected, then migration tests passed.
- A query-shape test initially asserted a provider-generated parameter name. It was corrected to assert parameter presence and absence of literal IDs; it passed.
- A strengthened idempotent-script assertion caught one stale locally generated artifact after a `--no-build` command. The artifact was regenerated for inspection, and the committed test was improved to generate and validate idempotent SQL directly from the migration assembly. Final migration tests passed from repository sources alone.
- One new credential-collision test first failed compilation on xUnit analyzer rule xUnit2029. The assertion form was corrected; the focused and full suites passed.
- One new source-security test initially matched intentional credential-marker rejection strings in Domain. Its scope was corrected to production persistence/composition source while retaining repository-wide creation-shortcut checks; all 35 security tests passed.
- The command host rejected one combined Web launch/cleanup script before it executed. The validation was split into exact create, launch, probe, stop, and drop commands and passed.
- Direct Ctrl+C delivery was unavailable for the non-interactive live processes. Exact executable paths/PIDs were verified and stopped; wrapper exit code 1 after forced stop is a harness artifact, and final cleanup checks passed.

## Failed, blocked, and NotRun summary

- Failed required final items: none.
- Blocked required items: none.
- Blocked optional environment: Docker daemon/Testcontainers. Real SQL Server validation was not blocked because LocalDB was available and all 19 SQL tests executed.
- NotRun by Phase 3 scope: Phase 4 polling/claiming/leasing/scheduling, PowerShell execution, script discovery/manifest loading, reporting, REST APIs, new Razor features, authentication, authorization, external secret retrieval, notifications, deployment automation, containers, Kubernetes, and production installation.
- NotRun environment claims: production SQL Server deployment, external SQL authentication, and production rollback. LocalDB migration rollback and idempotent application were validated.

# Phase 4 Worker Foundation and Queue Processing

Validation date: 2026-07-29. Times are America/Chicago (`-05:00`). Commands ran from the repository root.

## Starting gate and baseline

- Phase 3 merge commit `5a1e6de` was fetched to `main`, was an ancestor of the Phase 4 branch, and matched the required merged PR #2 state.
- PR #2 was merged; no open PR, issue, unresolved review thread, local/remote Phase 4 branch, or dirty starting file blocked Phase 4.
- Phase 4 branch: `agent/phase-4-worker-queue-processing`.
- Baseline tool restore: `2026-07-29T17:14:49-05:00`, exit 0, dotnet-ef 10.0.10.
- Baseline restore: `2026-07-29T17:14:49-05:00` to `17:14:52-05:00`, exit 0.
- Baseline Release build: `2026-07-29T17:14:52-05:00` to `17:15:06-05:00`, exit 0, 0 warnings/errors.
- Baseline full test: `2026-07-29T17:15:06-05:00` to `17:15:24-05:00`, exit 0, 362 passed: Unit 286, Security 35, SQL Server 29, Integration 3, Worker 7, PowerShell boundary 2.
- Baseline format verification: `2026-07-29T17:15:24-05:00` to `17:15:48-05:00`, exit 0.
- Baseline EF pending-model check: `2026-07-29T17:15:48-05:00` to `17:15:57-05:00`, exit 0, none pending.

## Domain lease model and Application contracts

- Added `JobWorkKind` (`DryRun`, `Execute`), strong `JobLeaseId`, fenced `JobLeaseCredentials`, aggregate-owned `JobLease`, timestamp/ownership invariants, renewal, safe release, and state-specific expiration recovery.
- DryRun acquisition remains `DryRunQueued`; Execute acquisition moves to `Claimed`. Active expiration records timed-out state/outcome. Completion removes the lease.
- Worker-controlled transitions require current lease ID, worker ID, and fencing token. Stale operations fail before mutation.
- `WorkerNode` now atomically synchronizes complete capability sets and computes liveness from enabled state plus heartbeat freshness.
- Added safe candidate/claimed-work DTOs; worker registration/heartbeat; acquisition, renewal, release, inspection, recovery, and lease-aware lifecycle handlers; and fencing/candidate source abstractions.
- Registration/capability changes and lease lifecycle events are bounded audits. Routine heartbeat and renewal are intentionally not audited.

## Persistence, migration, and query behavior

- Migration `20260729224310_AddWorkerQueueLeases` creates `wsr.JobLeaseFencingSequence`, `wsr.JobLeases`, rowversion, checks, ownership relationships, unique lease ID, and expiration/worker/work-kind indexes.
- Migration normalizes legacy `DryRunRunning`, `Claimed`, `Executing`, and `PostValidation` states before enforcing the lease requirement and records bounded migration audits.
- Queue discovery is a bounded parameterized projection, filters exact status/work kind plus lease absence, orders by `UpdatedUtc`, `CreatedUtc`, `JobId`, and returns no parameters or credential references.
- SQL fencing uses a fixed `SELECT NEXT VALUE FOR [wsr].[JobLeaseFencingSequence]` command through the current context connection/transaction.
- Real SQL migration tests cover apply, idempotency, rollback to Phase 3, Phase 4 reapply, table/sequence/check/index/FK metadata, and no pending model change.

## Worker behavior

- Startup owns a stable configured node identity, synchronizes capabilities, rejects disabled/name-mismatched identities, records the first heartbeat, and commits once.
- Persistent heartbeat uses a fresh scope; failure immediately pauses claims, retries with bounded backoff, and becomes fatal after the liveness window. Normal heartbeats write no audit.
- `JobWorkHandlerRegistry` rejects duplicates and determines the only supported candidate kinds. Production registers zero handlers.
- Queue polling is non-overlapping, bounded by available slots and candidate batch, tracks all dispatch tasks, observes exceptions/cancellation, and uses separate empty/persistence exponential backoffs with injected jitter.
- Renewal reuses lease ID/worker/fencing credentials through fresh scopes and cancels handlers on lease loss or when safe renewal can no longer be assured.
- Shutdown stops acquisition, cancels handlers, continues renewal during the configured drain, safely releases only unstarted work, and leaves active work for expiration recovery after timeout.
- Expired recovery uses bounded discovery and fresh scopes, ignores expected stale races, and atomically writes expired/recovered audits.
- Built-in metrics cover queue polls/claims/conflicts/empty polls, dispatch outcomes, lease renewal/loss/recovery, heartbeat outcomes, active dispatches, and available slots.

## Focused and real SQL results

- Domain/Application focused Unit tests passed, including acquisition, renewal, release, stale fencing, recovery, capability synchronization, registration, heartbeat, and cancellation.
- Worker focused suite: 37 passed, 0 failed, 0 skipped. Coverage includes empty/persistence backoff separation and reset, jitter bounds, handler registry, zero-handler behavior, supported-kind filtering, concurrency, completion slot release, observed exception/cancellation, renewal, immediate post-backoff renewal retry, lost-lease cancellation, invariant release, shutdown drain/completion/timeout, liveness-bounded heartbeat failure, development-only ephemeral identity, malformed capability validation, dispatch-task fault containment, and validated backoff bounds.
- Security focused suite: 42 passed, 0 failed, 0 skipped. Source/reflection tests cover PowerShell/process absence, safe descriptors, non-secret leases, fenced commands, persistence boundaries, and zero production handler implementations.
- SQL Server focused suite: 43 passed, 0 failed, 0 skipped against SQL Server LocalDB. Multi-worker coverage includes two-worker/one-job and four-worker/30-job races with unique ownership/fencing. SQL Server UTC coordination and expired-lease discovery use the shared database clock. Renewal/recovery and duplicate-recovery races each produce one valid winner, concurrent renewal and execution start both commit without a false job rowversion conflict, and terminal outcome retries safely after renewal commits first. Active DryRun/Execute/PostValidation recovery and stale completion rejection passed.
- Query interception proves bounded `TOP`, parameters, exact filters, deterministic ordering, lease absence, safe projection, and bounded command count.

## Final build, test, formatting, and model validation

- `dotnet tool restore`: `2026-07-29T18:10:53.5021690-05:00` to `18:10:54.0544942-05:00`, exit 0, dotnet-ef 10.0.10 restored.
- `dotnet restore`: `2026-07-29T18:10:59.1265510-05:00` to `18:11:00.9805573-05:00`, exit 0.
- `dotnet build --configuration Release`: `2026-07-29T18:11:08.7731545-05:00` to `18:11:13.2695465-05:00`, exit 0, 0 warnings/errors.
- `dotnet test --configuration Release`: `2026-07-29T18:11:18.3354210-05:00` to `18:11:37.2215913-05:00`, exit 0, 431 passed, 0 failed, 0 skipped: Unit 312, Security 42, SQL Server 40, Worker 32, Integration 3, PowerShell boundary 2.
- `dotnet format`: `2026-07-29T18:11:42.7776216-05:00` to `18:12:08.7482721-05:00`, exit 0.
- `dotnet format --verify-no-changes`: `2026-07-29T18:12:17.8645441-05:00` to `18:12:43.6955101-05:00`, exit 0.
- `dotnet build --configuration Release --no-restore`: `2026-07-29T18:12:49.1365159-05:00` to `18:12:53.0936227-05:00`, exit 0, 0 warnings/errors.
- `dotnet test --configuration Release --no-build`: `2026-07-29T18:12:58.5896094-05:00` to `18:13:14.1668063-05:00`, exit 0, the same 431 tests passed.
- `dotnet tool run dotnet-ef migrations has-pending-model-changes ...`: `2026-07-29T18:13:19.6617271-05:00` to `18:13:27.9592647-05:00`, exit 0, no pending changes.
- Final post-inspection revalidation after recovery-actor and option-bound hardening: Release no-restore build `2026-07-29T18:20:46.5640560-05:00` to `18:20:50.9969892-05:00`, exit 0, 0 warnings/errors; no-build full test `2026-07-29T18:20:51.0122599-05:00` to `18:21:06.0397188-05:00`, exit 0, 432 passed (Unit 312, Security 42, SQL Server 40, Worker 33, Integration 3, PowerShell boundary 2); formatter and verification exit 0; EF still reports no pending model changes.
- Post-review revalidation after the bounded PR feedback fixes: tool restore, restore, Release build, formatter, formatting verification, and Release no-restore build exited 0 with 0 warnings/errors; both full no-build runs passed 435 tests (Unit 312, Security 42, SQL Server 40, Worker 36, Integration 3, PowerShell boundary 2); the real SQL rollback regression passed with a persisted `FENCINGTOKEN` audit property; EF reported no pending model changes.
- Final review closeout revalidation: tool restore, restore, Release build, formatter, formatting verification, and Release no-restore build exited 0 with 0 warnings/errors; both full no-build runs passed 437 tests (Unit 312, Security 42, SQL Server 41, Worker 37, Integration 3, PowerShell boundary 2); focused regressions proved SQL-authoritative worker coordination time, immediate post-backoff renewal retry, and heartbeat failure bounded by the liveness window; EF reported no pending model changes.
- Post-review leased-execution clock closeout: formatting verification and the Release no-restore build exited 0 with 0 warnings/errors; the full no-build run passed 438 tests (Unit 313, Security 42, SQL Server 41, Worker 37, Integration 3, PowerShell boundary 2); a focused skew regression proved execution start and terminal outcome use the SQL-authoritative worker coordination clock.
- Post-review lease-renewal concurrency closeout: formatting verification and the Release no-restore build exited 0 with 0 warnings/errors, EF reported no pending model changes, and the focused real-SQL race plus full no-build run passed 439 tests (Unit 313, Security 42, SQL Server 42, Worker 37, Integration 3, PowerShell boundary 2); renewal and execution start committed concurrently without a false job rowversion conflict.
- Current-head Codex review closeout: formatting verification and the Release no-restore build exited 0 with 0 warnings/errors, EF reported no pending model changes, focused regressions proved SQL-authoritative queue-entry timing despite application-host skew and bounded terminal-resolution retry after renewal wins the lease-row race, and the full no-build run passed 441 tests (Unit 314, Security 42, SQL Server 43, Worker 37, Integration 3, PowerShell boundary 2).

## Runtime validation

- A GUID-scoped disposable SQL Server LocalDB database was migrated through both migrations with startup migration disabled.
- Web ran in Production on `http://127.0.0.1:5097`. `/`, `/Scripts`, `/Jobs`, `/Workers`, `/Audit`, `/Administration`, `/health`, `/health/live`, and `/health/ready` returned HTTP 200.
- Migration history remained two. With the exact disposable database offline, readiness returned 503 while liveness remained 200; readiness recovered to 200 after SQL returned.
- Worker ran in Production with stable ID `44444444-4444-4444-8444-444444444444`, test name, and OS capability. Registration persisted once, the heartbeat advanced, the queue logged zero supported work kinds, the pre-seeded `DryRunQueued` job remained unchanged, and `JobLeases` remained empty.
- Web and Worker each had zero child processes and zero PowerShell children. Provider logs parameterized values as `?`; no parameter, credential, connection string, or authentication value appeared.
- A focused test-hosted Worker run passed four fake-handler scenarios proving zero-handler/no-claim, acquisition/fencing, renewal with the same token, fake invocation, explicit lifecycle completion, and safe invariant release. No production executor participated.
- The command backend could not deliver Ctrl+C to non-interactive runtime processes. Their exact executable paths/PIDs were verified and stopped, producing wrapper exit 1 as a harness artifact. Unit/Worker hosted-service tests validate clean cancellation and drain where cancellation delivery is controllable.
- Port 5097 had no listener, no validation Web/Worker process remained, and the exact disposable database was dropped.

## Security inspection

- Production source scan found no `Process.Start`, `System.Diagnostics.Process`, or `System.Management.Automation`.
- Production source contains no `IJobWorkHandler` implementation or registration.
- `SaveChangesAsync` occurs only in `SqlUnitOfWork`; README mentions are documentation only.
- Security tests prove candidate and claimed descriptors contain no parameter/credential fields; lease state contains no secret material; all worker-controlled commands carry fenced credentials; DbContext/provider APIs remain Infrastructure-only; repositories do not save; and lease audits do not read job parameters.
- `git diff --check` passed. No connection string, raw credential, fake executor, generated SQL artifact, or runtime log is committed.

## Corrected, failed, blocked, and NotRun work

- Corrected: EF's composable raw-query shape could not wrap `NEXT VALUE FOR`; fencing was changed to a fixed scalar command on the context connection.
- Corrected: migration metadata tests initially exposed SQL collation and `sql_variant` conversion assumptions; queries were made explicit and then passed.
- Corrected: a four-worker SQL stress run exposed deadlock victim 1205. It is now translated as bounded persistence unavailability, and queue acquisition uses the dedicated persistence backoff; the full SQL suite passed.
- Corrected: initial `System.Diagnostics.Metrics` tag calls were overload-ambiguous; explicit key/value tags compile with 0 warnings.
- Corrected: one adapted legacy test briefly expected the wrong exception boundary; the exact Domain/Application expectations were restored and all 312 Unit tests passed.
- Failed required final items: none.
- Blocked required items: none.
- Blocked harness capabilities: hidden `Start-Process` launch and Ctrl+C delivery for non-interactive runtime processes. Foreground sessions, exact PID verification, endpoint/database evidence, forced cleanup, and hosted-service cancellation tests covered the required outcomes without changing product behavior.
- NotRun: production SQL Server deployment/rollback, production authentication/authorization, external secret retrieval, PowerShell execution, script process isolation, paid telemetry, deployment automation, containers, and Phase 5. No production execution evidence is claimed.

# Phase 5 PowerShell Execution Boundary

Validation date: 2026-07-29. Times are America/Chicago (`-05:00`). Commands ran from `<repo-root>`.

## Starting gate and baseline

- Starting commit: `a18f7ee57376cbe4a48be27085dd9b6576fcd4e1`, the Phase 4 review-fix merge on `main`.
- Phase 4 PRs #3 and #4 were merged into `main`. Their 15 total review threads had zero unresolved threads. No open PR, open issue, dirty file, or existing Phase 5 branch blocked the start.
- Phase 5 branch: `agent/phase-5-powershell-execution-boundary`.
- Baseline tool restore: `2026-07-29T22:42:46.6801751-05:00` to `22:42:47.2200310-05:00`, exit 0, dotnet-ef 10.0.10.
- Baseline restore: `2026-07-29T22:42:47.2348416-05:00` to `22:42:49.1537823-05:00`, exit 0.
- Baseline Release build: `2026-07-29T22:42:49.1548838-05:00` to `22:42:53.6405676-05:00`, exit 0, 0 warnings/errors.
- Baseline full test: `2026-07-29T22:42:53.6412917-05:00` to `22:43:13.7718070-05:00`, exit 0, 441 passed, 0 failed, 0 skipped: Unit 314, Security 42, SQL Server 43, Worker 37, Integration 3, PowerShell boundary 2.
- Baseline format verification: `2026-07-29T22:43:13.7726875-05:00` to `22:43:39.8253891-05:00`, exit 0.
- Baseline pending-model check: `2026-07-29T22:43:39.8261089-05:00` to `22:43:47.9790202-05:00`, exit 0, no pending model changes.

## Runtime and trust validation

- Discovery order validated: configured absolute path, `WINDOWSSCRIPTRUNNER_PWSH_PATH`, PATH inspected through file APIs, then `%ProgramFiles%\PowerShell` stable locations. Duplicate candidates are removed case-insensitively, stable runtimes are preferred to previews, compatible stable versions are ordered highest first, and a successful selection is cached.
- Selected executable: `C:\Program Files\WindowsApps\Microsoft.PowerShell_7.6.4.0_x64__8wekyb3d8bbwe\pwsh.exe`.
- Fixed-probe metadata: version 7.6.4, PSEdition Core, platform Win32NT, architecture X64. PATH inspection produced both WindowsApps candidates; deterministic path ordering selected the package installation path shown above.
- The real-runtime integration test was executed, not skipped. Missing/relative/configured legacy executable, below-minimum version, Desktop edition, malformed JSON, disallowed preview, and non-64-bit metadata tests passed. Preview opt-in passed separately.
- Controlled fixture: `<repo-root>\tests\WindowsScriptRunner.PowerShellTests\Fixtures\ControlledExecutionFixture.ps1`.
- The test copies the fixture beneath a unique allowed root, computes SHA-256, creates the internal trusted artifact, and hashes again immediately before launch. Valid hash, post-trust tampering, outside-root sibling prefix, traversal, UNC, device, alternate-stream, and actual NTFS junction-component tests passed.
- The production assembly has no public trusted-artifact constructor or resolver and exposes no raw script-path, command-line, command-text, or script-text execution method.

## Execution behavior

- Literal argument tests passed for plain text, spaces, double/single quotes, semicolons, ampersands, pipes, backticks, `$()`, `${}`, wildcards, redirection, Unicode, newlines, empty string, and the injection marker. The Base64-decoded fixture value matched exactly and no marker executed.
- Parameter allowlist, case-insensitive duplicate, NUL, oversized value, excessive count, and sensitive-classification rejection tests passed before working-directory creation.
- Concurrent UTF-8 stream tests passed with 512 stdout and 512 stderr markers, exact byte counts, Unicode preservation, exit 0, and nonzero exit 1. Nonzero exits 1 and 37 returned exactly without throwing.
- Timeout returned `TimedOut`, no fabricated exit code, finalized streams, terminated the tracked PID, remained bounded, and removed the execution directory.
- In-flight cancellation used `SpawnChild`, waited for parent and child marker files, cancelled after startup, terminated both tracked PIDs, drained streams, deleted the directory, and threw `OperationCanceledException`.
- The timeout process-tree scenario captured parent and fixed-command child PIDs and verified both exited. Post-test inspection found no tracked fixture PID alive. Existing unrelated PowerShell sessions were not changed.
- Stdout, stderr, and combined overflow scenarios returned `OutputLimitExceeded`, set truncation, kept captured UTF-8 content within configured stream/combined bounds, terminated the tracked root, and removed the working directory.
- Parent values for `WSR_PRIVATE_SENTINEL`, `OPENAI_API_KEY`, and `ConnectionStrings__WindowsScriptRunner` were absent in the fixture. Required `SystemRoot` and `TEMP` remained available. Sentinel values were neither returned nor logged.
- Two working-directory executions reported distinct `<working-root>\<execution-id>` directories, not the script directory or repository, and both directories were deleted after completion. Trust failure, timeout, overflow, and cancellation cleanup tests also passed.
- Captured logs contained execution ID, artifact, runtime version, duration, exit code, termination reason, byte counts, and truncation status. They omitted parameter values, stdout/stderr, fixture path/content, complete command line, environment values, connection strings, and credential identifiers.

## Focused validation

- PowerShell focused suite: 81 passed, 0 failed, 0 skipped against the actual runtime.
- PowerShell/Process/TrustedScript Unit filter: 4 passed, 0 failed, 0 skipped.
- Security focused suite: 48 passed, 0 failed, 0 skipped.
- Worker regression suite: 37 passed, 0 failed, 0 skipped. Production registration contains neither an executable work handler nor a PowerShell service.
- SQL Server regression suite: 43 passed, 0 failed, 0 skipped against LocalDB.
- Source inspection found process APIs and Job Object interop only in `WindowsScriptRunner.PowerShell`; no production `System.Management.Automation`, PowerShell SDK, `powershell.exe`, command shell, `Invoke-Expression`, unsafe `Arguments`, shell execution, caller-controlled `-Command`, or execution-policy bypass.
- Web and Worker have no PowerShell reference or registration. No production `IJobWorkHandler`, persistence migration, table, or model change was added.

## Final build, test, formatting, and model validation

- `dotnet tool restore`: `2026-07-29T23:23:00.0034043-05:00` to `23:23:00.5260266-05:00`, exit 0.
- `dotnet restore`: `2026-07-29T23:23:00.5273690-05:00` to `23:23:02.3627572-05:00`, exit 0.
- `dotnet build --configuration Release`: `2026-07-29T23:23:02.3638824-05:00` to `23:23:06.5964292-05:00`, exit 0, 0 warnings/errors.
- `dotnet test --configuration Release`: `2026-07-29T23:23:06.5972920-05:00` to `23:23:38.1525373-05:00`, exit 0, 530 passed, 0 failed, 0 skipped: Unit 318, Security 48, SQL Server 43, Worker 37, Integration 3, PowerShell boundary 81.
- `dotnet format`: `2026-07-29T23:23:38.1533349-05:00` to `23:24:05.4504895-05:00`, exit 0.
- `dotnet format --verify-no-changes`: `2026-07-29T23:24:05.4512453-05:00` to `23:24:32.3863914-05:00`, exit 0.
- `dotnet build --configuration Release --no-restore`: `2026-07-29T23:24:32.3871193-05:00` to `23:24:35.7217455-05:00`, exit 0, 0 warnings/errors.
- `dotnet test --configuration Release --no-build`: `2026-07-29T23:24:35.7224085-05:00` to `23:25:04.7804403-05:00`, exit 0, the same 530 tests passed.
- `dotnet tool run dotnet-ef migrations has-pending-model-changes ...`: `2026-07-29T23:25:04.7811357-05:00` to `23:25:12.3927279-05:00`, exit 0, no pending model changes.

## Corrected, failed, blocked, NotRun, and limitations

- Corrected: the first validation wrapper captured its own pipeline output and returned a harness exit 1; every exact baseline command was rerun with visible timestamps and passed.
- Corrected: the first focused build exposed source-generated Job Object interop safe-handle return requirements. The raw handle is now wrapped immediately in `SafeJobHandle`; the focused build then passed with zero warnings.
- Corrected: the initial real-process run had a timeout shorter than the fixture PID flush and an empty-string negative log assertion. The test timing/assertion was corrected without changing boundary behavior; all focused PowerShell tests passed.
- Corrected: the environment did not grant symbolic-link creation privilege. A privilege-independent NTFS junction fixture was added through test-only native setup, and the actual reparse-component rejection passed. An initial recursive cleanup attempt traversed the junction; cleanup now deletes the junction itself first and left no test directory.
- Corrected: the prescribed Unit filter initially matched no existing test name. Four public PowerShell contract tests were added to the Unit project, and the exact filter passed.
- Failed required final items: none.
- Blocked required items: none.
- NotRun: production queue-to-PowerShell dispatch, production trusted-artifact resolution, arbitrary script selection/upload, script manifests/packages, credential retrieval/injection, remoting, report parsing/persistence, production SQL deployment, authentication/authorization, and Phase 6 behavior. These are outside Phase 5.
- Remaining limitations: Job Objects govern lifetime rather than filesystem, registry, network, language, token, or privilege access. Process startup precedes Job Object assignment, and the SHA-256 file handle closes before process startup, leaving small documented races. Command-line values are OS-visible, so secrets are prohibited. The boundary is validated only against the controlled fixture and is not an operating-system sandbox.

## PR #5 review corrections

Validation date: 2026-07-30. Times are America/Chicago (`-05:00`).

- Clarified that PATH inspection produced both WindowsApps runtime candidates and deterministic path ordering selected the package installation path.
- Synchronized bounded-capture disposal with in-flight storage. If process-tree termination fails, redirected readers are closed and both pump tasks are observed without concealing the termination exception.
- Wrapped working-root attribute inspection failures in `PowerShellExecutionException`.
- Rejected leading-hyphen values after real-runtime verification showed that `-Verbose` is interpreted as a common parameter in `-File` mode.
- Published the immutable cached runtime through `Volatile.Read` and `Volatile.Write`; concurrent callers share one probed instance.
- Rejected equal or nested trusted-script and working roots in either direction.
- Tightened the injection-marker assertion and made PID marker reads wait for non-empty content.
- Added focused regressions for capture disposal, termination failure, concurrent runtime caching, nested roots, and leading-hyphen values.
- Focused PowerShell tests: 87 passed, 0 failed, 0 skipped against the actual runtime.
- Filtered PowerShell Unit tests: 4 passed, 0 failed, 0 skipped.
- Security tests: 48 passed, 0 failed, 0 skipped.
- `dotnet tool restore`: `2026-07-30T08:42:05.8636964-05:00` to `08:42:06.4945626-05:00`, exit 0.
- `dotnet restore`: `2026-07-30T08:42:06.4959968-05:00` to `08:42:09.3779583-05:00`, exit 0.
- `dotnet build --configuration Release`: `2026-07-30T08:42:09.3790950-05:00` to `08:42:14.8293917-05:00`, exit 0, 0 warnings/errors.
- `dotnet test --configuration Release`: `2026-07-30T08:42:14.8302978-05:00` to `08:42:49.6932093-05:00`, exit 0, 536 passed, 0 failed, 0 skipped: Unit 318, Security 48, SQL Server 43, Worker 37, Integration 3, PowerShell boundary 87.
- `dotnet format`: `2026-07-30T08:42:49.6941322-05:00` to `08:43:19.2486690-05:00`, exit 0.
- `dotnet format --verify-no-changes`: `2026-07-30T08:43:19.2494101-05:00` to `08:43:48.0107491-05:00`, exit 0.
- `dotnet build --configuration Release --no-restore`: `2026-07-30T08:43:48.0115846-05:00` to `08:43:51.8276819-05:00`, exit 0, 0 warnings/errors.
- `dotnet test --configuration Release --no-build`: `2026-07-30T08:43:51.8284079-05:00` to `08:44:24.2637894-05:00`, exit 0, the same 536 tests passed.
- `dotnet tool run dotnet-ef migrations has-pending-model-changes ...`: `2026-07-30T08:44:24.2647168-05:00` to `08:44:33.4747390-05:00`, exit 0, no pending model changes.
- No migration, production queue wiring, handler, arbitrary script surface, credential path, or Phase 6 behavior was added.
