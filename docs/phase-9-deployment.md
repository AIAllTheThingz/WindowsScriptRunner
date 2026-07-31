# Phase 9 deployment runbook

Phase 9 is in progress. This first slice establishes the deployment contract without claiming a
production rollout.

## Release layout

Publish the Web and Worker applications separately so each host receives only the files it needs:

```powershell
dotnet publish .\src\WindowsScriptRunner.Web\WindowsScriptRunner.Web.csproj `
  --configuration Release --runtime win-x64 --self-contained false `
  --output C:\ProgramData\WindowsScriptRunner\releases\<version>\web

dotnet publish .\src\WindowsScriptRunner.Worker\WindowsScriptRunner.Worker.csproj `
  --configuration Release --runtime win-x64 --self-contained false `
  --output C:\ProgramData\WindowsScriptRunner\releases\<version>\worker
```

The release must contain `web.config` for IIS and `WindowsScriptRunner.Worker.exe` for the Worker
service. Do not copy production connection strings, Windows group SIDs, certificate private keys,
or other secrets into the repository or publish directory.

## Deployment order

1. Generate and review an idempotent EF migration script from the exact release source.
2. Back up SQL Server and apply the reviewed script with
   [`Invoke-ReviewedMigration.ps1`](../deployment/sql/Invoke-ReviewedMigration.ps1).
3. Install the reviewed PowerShell artifact under a separate trusted root with
   [`Install-ReviewedAutomationArtifact.ps1`](../deployment/powershell/Install-ReviewedAutomationArtifact.ps1).
4. Install or upgrade the Worker with an explicit service identity using
   [`Install-WindowsScriptRunnerWorker.ps1`](../deployment/windows-service/Install-WindowsScriptRunnerWorker.ps1).
5. Configure protected Worker and Web settings, including the SQL connection string, stable Worker
   node ID, trusted/working roots, and approved Windows group SIDs.
6. Configure the HTTPS IIS site with
   [`Install-WindowsScriptRunnerWeb.ps1`](../deployment/iis/Install-WindowsScriptRunnerWeb.ps1).
7. Verify service, site, certificate binding, and `/health/ready` before enabling the reviewed
   automation package.

Every mutating script supports `-WhatIf`. The SQL script uses Windows-integrated `sqlcmd`, takes a
`COPY_ONLY` backup against a local SQL Server topology and a local absolute backup path, and does not
attempt an automatic rollback. Confirm that the SQL Server service account can write to the backup
directory before execution. A rollback restores the approved backup and then repeats readiness and
migration-state verification.

## Phase 9 boundary still outstanding

No production or representative-host deployment has been run. Certificate renewal, Hosting Bundle
preflight, SPN/Kerberos and browser-zone validation, service-account provisioning, secret-provider
integration, observability export, retention policy, backup/restore rehearsal, and upgrade/rollback
sign-off remain required before production readiness can be claimed.
