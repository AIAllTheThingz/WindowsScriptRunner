# Phase 9 deployment runbook

Phase 9B is blocked pending an approved representative environment and accountable production
readiness approval. This runbook defines the evidence and operator sequence; it does not claim that
deployment has occurred.

## Required acceptance record

Create one change record for the exact release and representative environment. Keep unknown values
as open decisions until an accountable owner supplies them:

| Decision | Required record |
| --- | --- |
| Environment | Windows Server edition/version/build/install option and lifecycle review, domain or workgroup, IIS and .NET Hosting Bundle versions, SQL Server/instance/version, topology, hostnames, and target owners |
| Identities | Web application-pool identity, Worker service identity, database principal, filesystem/service/IIS/SQL permissions, private group-SID record, and approving owner |
| Release | Source commit, Web and Worker publish roots, migration-script identity separate from runtime identity, reviewed inventory artifact path, SHA-256 hashes, and configuration class |
| Configuration | Existing standard environment provider, approved process/service/IIS configuration management, private configuration record, certificate renewal owner, and secret rotation procedure; never record secret values or actual private identifiers here |
| Worker binding | Approved Worker's stable `Worker:NodeId`, matching `Automation:LocalHostInventory:ApprovedWorkerNodeId` (environment key `Automation__LocalHostInventory__ApprovedWorkerNodeId`), registered/enabled status, and one resulting `worker:<canonical-guid>` target; retain actual identifiers only in the protected record |
| Operations | Health signals, telemetry destination, alert thresholds and routes, retention, redaction, escalation, support owner, certificate renewal owner, and maintenance window |
| Recovery | Backup location, restore authority, migration/upgrade owner, rollback trigger, rollback authority, and post-restore verification owner |
| Approval | Security, database, operations, support, and production-readiness reviewers and their recorded decision |

The application must use the existing standard .NET environment-variable provider through approved
process, service, or IIS configuration management. The default hosts do not load an arbitrary
configuration file outside the release. An external file or vault requires an approved provider and
explicit integration, which is not implemented by this repository. Do not invent a provider,
hostname, identity, group SID, certificate identifier, or secret value for this record; keep actual
private identifiers in the protected change record.

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

## Representative deployment and acceptance sequence

1. Record the acceptance record above and obtain the maintenance-window authorization.
2. Build and publish Web and Worker from the exact source commit; record artifact hashes and retain
   the reviewed PowerShell artifact hash separately from publish output.
3. Generate and independently review an idempotent EF migration script from that exact release.
4. Perform prerequisite discovery, run every mutating script with `-WhatIf`, and confirm backup
   restore viability and the approved recovery target before changing the database. Supply the
   privately recorded `ExpectedMachineGuid` to every mutating deployment script.
5. Back up SQL Server and apply the reviewed script with
   [`Invoke-ReviewedMigration.ps1`](../deployment/sql/Invoke-ReviewedMigration.ps1).
6. Record the verified backup, migration state, and readiness result before continuing.
7. Install the reviewed PowerShell artifact under a separate trusted root with
   [`Install-ReviewedAutomationArtifact.ps1`](../deployment/powershell/Install-ReviewedAutomationArtifact.ps1).
8. Install or upgrade the Worker with an explicit service identity using
   [`Install-WindowsScriptRunnerWorker.ps1`](../deployment/windows-service/Install-WindowsScriptRunnerWorker.ps1).
9. Configure protected Worker and Web settings, including the SQL connection string, stable Worker
   node ID, `Automation:LocalHostInventory:ApprovedWorkerNodeId`, trusted/working roots, and
   approved Windows group SIDs. Confirm the configured value matches the approved enabled Worker
   registration. Quiesce queue processing and update every Worker to the same release before
   enabling requests; do not use a mixed-version rollout because older Workers do not enforce this
   binding.
10. Configure the HTTPS IIS site with
   [`Install-WindowsScriptRunnerWeb.ps1`](../deployment/iis/Install-WindowsScriptRunnerWeb.ps1).
11. Verify service, site, certificate binding, authentication/authorization outcomes, and
    `/health/ready` before enabling the reviewed automation package.
12. Exercise the authorized submission path for the existing pinned local, parameterless,
    ReadOnly/DryRun package; verify it stores one `worker:<canonical-guid>` target bound to the
    approved Worker, fails closed before writes for missing/invalid binding or disabled registration,
    and record requester, target, audit, and typed-report evidence. Do not silently retarget legacy
    `local-worker` jobs.
13. Exercise health, alert, failure, recovery, upgrade, and rollback procedures, then obtain the
    separate production-readiness decision.

Every mutating script supports `-WhatIf`. The SQL script uses Windows-integrated `sqlcmd`, takes a
`COPY_ONLY` backup against a local SQL Server topology and a local absolute backup path, and does not
attempt an automatic rollback. Confirm that the SQL Server service account can write to the backup
directory before execution. A rollback restores the approved backup and then repeats readiness and
migration-state verification.

The IIS installer and verifier use the native Windows PowerShell 5.1 `WebAdministration` module on
the deployment host and require a 40-character `CertificateThumbprint`; the verifier accepts an
optional `ReadinessTimeoutSeconds` (default 30 seconds) when probing readiness. The Worker verifier requires exact
`ExpectedServiceAccount`. The reviewed automation artifact and application execution boundary remain
PowerShell 7.4+; do not substitute the automation runtime for IIS administration.

## Native DBA recovery and upgrade procedure

The database owner must use the reviewed migration script and native SQL Server tooling for each
representative rehearsal. Quiesce both Web and Worker hosts before rollback so no concurrent jobs or
writes can occur:

1. Confirm the target instance, database, maintenance window, backup directory, restore authority,
   and migration script hash in the acceptance record.
2. Take the script's copy-only backup and run `RESTORE VERIFYONLY` before applying migrations.
   Record the backup path, timestamp, database identity, verification result, and operator; do not
   place connection strings or credentials in logs.
3. Apply the reviewed idempotent migration script and record the exit status, migration history,
   and readiness result.
4. For rollback, stop and quiesce both hosts, run `RESTORE FILELISTONLY`, and restore into a
   separately approved disposable recovery database with explicit `MOVE` clauses. Do not use blind
   `REPLACE` against the target database.
5. Run `DBCC CHECKDB` on the restored database, verify migration history, then restore the previous
   binaries and protected settings. Verify Web/Worker identity, readiness, report integrity, and
   absence of unexpected data loss.
6. Stop on any failed verification, identity mismatch, unexpected data loss, or concurrent-write
   condition; escalate to the rollback authority. Rollback does not automatically reapply the
   migration.
7. Only as a separate upgrade rehearsal after rollback verification passes, deploy the next
   immutable Web/Worker release, reapply the reviewed migration, and verify startup, health,
   submission, typed reporting, and rollback-trigger behavior.

The DBA runbook is evidence only when the target identity, operator, timestamps, hashes, native
tool output, and pass/fail decision are retained in the change record.

## Operational evidence

Before readiness approval, record the selected telemetry destination and the following decisions:

- liveness, readiness, Worker heartbeat, queue, job, authorization, audit, migration, backup,
  certificate, and service health signals;
- alert thresholds, severity, route, acknowledgment owner, escalation time, and stop criteria;
- retention period, access control, redaction rule, and deletion authority for logs, audit data,
  reports, and backup evidence; and
- support owner, incident path, maintenance window, and rollback authority.

Health and telemetry evidence must use the exact deployed artifact and configuration class. A local
build, test run, or script `-WhatIf` result does not establish operational readiness.

## Phase 9B blockers

Production readiness remains blocked until the change record supplies all of these missing decisions
and evidence:

- an approved representative Windows Server/IIS and SQL Server target with topology and owners;
- provisioned identities, permissions, certificate/SPN/Kerberos/browser-zone decisions, and Hosting
  Bundle evidence;
- an approved existing .NET configuration-provider path for protected settings and secret rotation;
- immutable release, migration-script, and reviewed-artifact source/hash records;
- telemetry, health, alert, retention, escalation, and support decisions;
- backup verification, restore, migration, upgrade, and rollback evidence;
- the approved Worker binding, matching configuration, enabled registration, and target-to-host
  identity evidence;
- an authorized submission path exercised for the existing package, followed by accountable
  production-readiness approval.
