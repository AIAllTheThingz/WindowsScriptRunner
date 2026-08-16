# WindowsScriptRunner Agent Instructions

## Adopted engineering baseline

This repository adopts **Public Access Agents `v0.10.0`** from `AIAllTheThingz/Public-AI-Governance`, source commit `83c73f3ab9a049ff2321d463164fcf98fb453a9c`. The generated manifest, traceable composition, and adoption evidence are under `docs/public-ai-governance/`.

Repository facts and explicit local design decisions remain authoritative. The external standards baseline strengthens review, safety, testing, and evidence behavior but does not authorize production deployment or invent system facts.

## Project facts

- Windows-hosted .NET 10 application for controlled automation.
- Razor Pages web portal plus worker/service execution paths.
- C#/.NET application code and PowerShell 7.4+ out-of-process automation boundary.
- SQL Server persistence, queue coordination, transactional auditing, and typed report storage.
- Windows Negotiate authentication with SID-based authorization.
- Production automation is intentionally constrained to reviewed, versioned, hash-pinned packages; arbitrary script upload/execution is not a supported capability.
- Phase 9 deployment hardening is in progress; production rollout is not complete.

## Working rules

- Preserve the .NET 10, PowerShell 7.4+, SQL Server, Windows authentication, and reviewed-package boundaries unless migration is explicitly in scope.
- Treat authorization, job approval, package identity/hash, worker leases, SQL transactions, trusted paths, process execution, reporting, and deployment configuration as security-sensitive.
- Do not widen the system into generic arbitrary-script execution without explicit architecture, threat, compatibility, and authorization review.
- Keep secrets and environment-specific credentials outside source and generated evidence.
- Require target identity and explicit authorization before consequential automation.
- Preserve safe defaults: production automation remains disabled until explicitly enabled and configured; startup migrations remain off by default outside controlled environments.
- Record validation that actually ran. Environment-dependent checks remain `NotRun` or `Blocked` when unavailable.
- A successful build, test subset, or standards composition does not establish production readiness.

## Validation baseline

Prefer the repository-defined validation sequence and report actual outcomes:

```powershell
dotnet tool restore
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
dotnet format --verify-no-changes
```

Run Windows/IIS/SQL/PowerShell integration and deployment checks only where the required environment exists. A Linux runner failure in a Windows-only test is evidence of an environment boundary, not proof that the Windows behavior failed.

## Profile composition note

This project genuinely spans `INTERNAL_AUTOMATION` and `WEB_APPLICATION`. The `v0.10.0` adoption pilot demonstrated that `generate-manifest --include-profile-required` expands required disciplines from the primary profile but not from selected secondary profiles. Until the upstream generator is corrected, reviewers must explicitly evaluate required overlays from both profiles rather than assuming the generated discipline list is complete. The defect is tracked in `AIAllTheThingz/Public-AI-Governance#66`.

## Precedence

More-specific `AGENTS.md` files may strengthen these requirements for their subtree. They must not silently weaken security, validation, evidence, compatibility, or authorization requirements.
