# Public-AI-Governance downstream adoption record

## Pilot identity

- Downstream repository: `AIAllTheThingz/WindowsScriptRunner`
- Pilot shape: mixed application/infrastructure system
- Downstream source revision before adoption: `8694698829aa33640abc2f798233f47d71a77e39`
- Public Access Agents release: `v0.10.0`
- Public Access Agents source commit: `83c73f3ab9a049ff2321d463164fcf98fb453a9c`
- Adoption date: 2026-08-16

## Selected composition

- Primary profile: `INTERNAL_AUTOMATION`
- Secondary profile: `WEB_APPLICATION`
- Languages: `csharp`, `dotnet`, `powershell`
- Framework: `aspnet-core`
- Operating-system boundary: `windows-server`
- Generated disciplines: `application-security`, `testing`, `documentation`, `observability`, `ci-cd`, `supply-chain`, `architecture`, `integration`, `release-engineering`, `sre`, `privacy`

The exact generated manifest is `project-manifest.json`. The traceable composition is retained under `standards-bundle/` without flattened upstream source copies.

## Commands exercised

- Published standards checkout at `v0.10.0`: **Passed**
- `tools/generate-manifest/generate_manifest.py`: **Passed**
- `tools/compose-agents/compose_agents.py --no-copy-sources`: **Passed**
- `dotnet --info`: **Passed**
- `dotnet tool restore`: **Passed**
- `dotnet restore`: **Passed**
- `dotnet build --configuration Release --no-restore`: **Passed**
- `dotnet format --verify-no-changes`: **Passed**
- PowerShell parser check under `pwsh`: **Passed**
- `dotnet test --configuration Release --no-build`: **Failed on the Ubuntu runner** in Windows-specific PowerShell trust/junction tests

The test output shows failures caused by the Windows-only execution boundary, including a required-Windows guard and `kernel32.dll` P/Invoke. This is recorded as an environment-specific validation failure. It is not converted to `NotRun`, because the command did run and returned failure, and it is not presented as proof that the same tests fail on their intended Windows environment.

## Adoption outcome

The published standards could be selected and composed for a real mixed application/infrastructure project. A root `AGENTS.md` plus tighter PowerShell and deployment scopes were tailored from actual repository facts. The adopted hierarchy explicitly preserves the reviewed-package trust model, out-of-process PowerShell boundary, Windows authentication, SQL transaction/lease model, safe production defaults, and unfinished deployment-hardening status.

## Friction and findings

1. **Upstream tooling defect:** `generate-manifest --include-profile-required` expands required disciplines from the primary profile only. The generated manifest retains `WEB_APPLICATION` under `secondaryProfiles`, but the secondary profile's required `accessibility` discipline is absent. Tracked upstream as `AIAllTheThingz/Public-AI-Governance#66`.
2. **Environment-specific validation boundary:** the repository builds and formats on Ubuntu, but the full test suite includes Windows-only trust and junction behavior. Linux execution failed in those tests. This is expected evidence that a mixed Windows system needs Windows validation for Windows-native trust behavior; the pilot does not reinterpret Linux failures as Windows failures or passing tests.
3. **Adopter-specific tailoring:** reviewed package hashes, trusted roots, SQL-authoritative leases, Windows Negotiate/SID authorization, typed reports, IIS/Kerberos/SPN deployment, and the intentionally disabled production-automation default are project facts that generic standards correctly do not infer.
4. **No package-selection failure:** manifest generation and composition resolved the selected profiles, languages, framework, OS package, and source hashes successfully.

## Limitations

- No production deployment, IIS configuration, Kerberos/SPN validation, Windows Service installation, production SQL rollout, or live automation target was exercised.
- No claim is made that the failed Ubuntu `dotnet test` result represents the expected Windows-run result.
- The adoption manifest is incomplete with respect to secondary-profile required disciplines until upstream issue #66 is fixed or the missing overlay is selected manually.
- This pilot does not by itself justify stable maturity promotion.

## Follow-up

- Correct Public-AI-Governance issue #66 so required disciplines are expanded for secondary profiles.
- Continue to run Windows-native integration/trust validation in an appropriate Windows environment before production-readiness claims.
- Reuse this record as the mixed-system downstream adoption input for Public-AI-Governance issue #41 and later maturity review while retaining failed/Blocked environment evidence exactly as observed.
