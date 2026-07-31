# PowerShell deployment

## Status

The isolated PowerShell 7 execution boundary and one reviewed production artifact are implemented. Phase 9 now provides `Install-ReviewedAutomationArtifact.ps1`, which verifies the compile-pinned SHA-256 and atomically installs the artifact under a separate absolute root. A production install has not been performed.

The reviewed source artifact is:

`src/WindowsScriptRunner.Automation/Artifacts/windows.local-host-inventory/1.0.0/Collect-LocalHostInventory.ps1`

The Automation project copies it to:

`automation/windows.local-host-inventory/1.0.0/Collect-LocalHostInventory.ps1`

under Worker build and publish output. The compiled catalog pins its relative path and SHA-256. Runtime configuration may select only the absolute trusted root and a separate working root; it cannot replace the package identity, path, hash, phases, or parameter allowlist.

The installer provides installation, upgrade protection, staging, hash verification, and read/execute ACL setup. It does not install PowerShell, alter execution policy, retrieve secrets, or perform an automatic rollback. Runtime installation, integrity monitoring, working-root provisioning, and representative-host verification remain outstanding. Operators must not copy or modify the artifact independently of its reviewed catalog metadata.

See [PowerShell execution boundary](../../docs/powershell-execution-boundary.md) and [security](../../docs/security.md).
