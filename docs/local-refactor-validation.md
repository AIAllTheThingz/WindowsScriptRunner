# Local refactor validation

Final validation was run on 2026-09-06 after the reviewed local refactor and web-asset reduction. This report records local checks only; it is not a production-readiness assessment.

## Setup

`global.json` selected SDK `10.0.302` from a user-local installation. The validation process used:

```powershell
$sdkRoot = Join-Path $env:USERPROFILE '.dotnet'
$env:DOTNET_ROOT = $sdkRoot
$env:PATH = "$sdkRoot;$env:PATH"
```

PowerShell integration tests used the bundled `pwsh.exe` 7.6.5. SQL Server tests used the available `MSSQLLocalDB` instance and their disposable database setup.

Raw validation logs are retained locally outside the repository. The basenames below identify those evidence files; they are not downloadable files in the repository.

## Results

| Check | Result | Evidence |
| --- | --- | --- |
| `dotnet tool restore` | Pass | `final-dotnet-tool-restore.log` |
| `dotnet restore` | Pass | `final-dotnet-restore.log` |
| `dotnet build --configuration Release` | Pass; 0 warnings, 0 errors | `final-corrected-dotnet-build-release.log` |
| `dotnet test --configuration Release --no-build` | Pass; 742 passed, 0 failed, 0 skipped | `final-corrected-dotnet-test-release-no-build.log` |
| `dotnet format --verify-no-changes` | Pass | `final-corrected-dotnet-format-verify.log` |
| EF pending-model check | Pass; no pending model changes | `final-dotnet-ef-pending-model.log` |

The EF check used:

```powershell
dotnet tool run dotnet-ef migrations has-pending-model-changes `
  --project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj `
  --startup-project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj `
  --configuration Release --no-build
```

The initial full run recorded three failures in `FallbackTerminatesDescendantAfterRootExits`, `TimeoutRemainsActiveAfterRootExitsWhileChildHoldsOutputPipes`, and `TimeoutTerminatesSpawnedChildProcessTree`. Each failed before the process-tree assertion when `ProcessTest.ParseProcessId` could not find the expected `PARENT_PID` or `CHILD_PID` marker at `tests/WindowsScriptRunner.PowerShellTests/PowerShellTestSupport.cs:281`; the initial run passed 739 tests, and its raw log remains `final-dotnet-test-release-no-build.log`. An isolated fallback run reproduced the marker failure with the bundled PowerShell runtime (`isolated-powershell-child-tree.log`).

As a bounded follow-up, the fallback test was given a five-second fixture-start budget and a pre-parse diagnostic while retaining its six-second duration bound and all timeout, exit, and child-cleanup assertions. The isolated test emitted both process IDs but measured 6.7213867 seconds, failing the retained six-second bound; this experiment is recorded at `experiment-powershell-build-release.log` and `experiment-fallback-5s.log`. The corrected final test change applies a five-second request timeout to the three `SpawnChild` cases, uses the five-second bounded overhead allowance for the two existing duration assertions, leaves the unrelated `Sleep` case unchanged, and retains all outcome, child-exit, and working-directory cleanup assertions. The three targeted cases passed in `corrected-powershell-targeted-tests.log`, followed by the final full solution result of 742 passed.

## Refactor evidence

The former public `StartExecutionAttemptCommand`/`StartExecutionAttemptHandler` and `RecordExecutionOutcomeCommand`/`RecordExecutionOutcomeHandler` pair was removed. Callers and tests use the fenced queue commands and handlers `StartLeasedExecutionCommand`/`StartLeasedExecutionHandler` and `RecordLeasedExecutionOutcomeCommand`/`RecordLeasedExecutionOutcomeHandler` in `src/WindowsScriptRunner.Application/Queue/`. The queue path retains lease fencing, SQL coordination-clock use, terminal retry behavior, and redacted outcome audit properties.

The measured web-asset reduction removed 52 tracked vendor files totaling 8,625,350 bytes: 40 unused Bootstrap variants and 12 unused jQuery/validation runtime files. The required Bootstrap license, minified CSS and bundle JavaScript with maps, and the three jQuery-family licenses were retained. The source/test refactor removed an estimated net 109 handwritten lines, excluding vendor assets and documentation; the shared test-fake helper adds 28 lines and is included in that estimate.

The pinned governance checkout matched source commit `83c73f3ab9a049ff2321d463164fcf98fb453a9c`. All 58 manifest sources were hash-verified and read, together with the applicable documentation supporting standards and the `INTERNAL_AUTOMATION`/`WEB_APPLICATION` profile, security, testing, accessibility, and ASP.NET Core overlays. The independent review recorded no introduced regressions in exception outcome grouping, lease credentials and SQL clock handling, redacted audit fields, terminal retry behavior, or migrated unit/SQL assertions. Risk remains moderate and limited to this local refactor; the review does not establish production readiness.

## Checks not run

IIS, deployment, production automation, and other environment-dependent rollout checks were not run. No persistent machine PATH change, production configuration change, database schema change, or deployment was performed. The initial failure and controlled-experiment logs are retained as diagnostic history alongside the passing final run.
