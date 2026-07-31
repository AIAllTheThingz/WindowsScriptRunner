# Phase 9 deployment foundation

Phase 9 is in progress. The repository now contains explicit operator-run deployment tooling for
the first production-hardening seam:

- `iis/Install-WindowsScriptRunnerWeb.ps1` configures an HTTPS IIS site, an application pool, the
  ASP.NET Core published output, and read/execute ACLs;
- `windows-service/Install-WindowsScriptRunnerWorker.ps1` installs or upgrades the Worker service,
  configures automatic restart actions, and grants the service identity read/execute access;
- `sql/Invoke-ReviewedMigration.ps1` takes a `COPY_ONLY` backup before applying an idempotent,
  reviewed migration script through Windows-integrated `sqlcmd`; and
- `powershell/Install-ReviewedAutomationArtifact.ps1` verifies the compile-pinned SHA-256 before
  atomically installing the reviewed inventory artifact.

All mutating scripts support `-WhatIf`. They require absolute local paths, avoid accepting database
passwords or application secrets, and do not enable application startup migrations. Production
operators must supply the service identity, certificate, protected configuration, backup location,
and maintenance-window approval from the target environment.

The scripts are deployment building blocks, not evidence of a completed production rollout. A
representative Windows Server/IIS host, SQL Server backup and restore rehearsal, certificate and
SPN/Kerberos validation, secret-provider integration, observability export, retention policy, and
end-to-end upgrade/rollback sign-off remain outstanding Phase 9 work.

See the component runbooks:

- [IIS](iis/README.md)
- [Windows Service](windows-service/README.md)
- [SQL Server](sql/README.md)
- [PowerShell artifact](powershell/README.md)
