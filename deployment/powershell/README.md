# PowerShell deployment

## Status

The isolated PowerShell 7 execution boundary and one reviewed production artifact are implemented. Production artifact installation and host-hardening guidance are planned for Phase 9.

The reviewed source artifact is:

`src/WindowsScriptRunner.Automation/Artifacts/windows.local-host-inventory/1.0.0/Collect-LocalHostInventory.ps1`

The Automation project copies it to:

`automation/windows.local-host-inventory/1.0.0/Collect-LocalHostInventory.ps1`

under Worker build and publish output. The compiled catalog pins its relative path and SHA-256. Runtime configuration may select only the absolute trusted root and a separate working root; it cannot replace the package identity, path, hash, phases, or parameter allowlist.

This directory does not yet provide installation, ACL, integrity-monitoring, PowerShell-runtime installation, upgrade, rollback, or verification scripts. Operators must not copy or modify the artifact independently of its reviewed catalog metadata.

See [PowerShell execution boundary](../../docs/powershell-execution-boundary.md) and [security](../../docs/security.md).
