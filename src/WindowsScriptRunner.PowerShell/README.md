# PowerShell

Phase 5 implements the isolated PowerShell 7 child-process boundary. This project owns `pwsh.exe` discovery and probing, trusted local `.ps1` path and SHA-256 validation, literal `ArgumentList` construction, a minimized environment, per-execution working directories, concurrent bounded stream capture, timeout/cancellation distinction, and Windows Job Object process-tree containment.

The public boundary accepts only an internally created `TrustedPowerShellScript`; it has no arbitrary path, command-text, or script-text API. The only artifact created in Phase 5 is `ControlledExecutionFixture.ps1` in the PowerShell integration tests. Web and Worker do not reference or register this project, and no production `IJobWorkHandler` invokes it.

See [PowerShell execution boundary](../../docs/powershell-execution-boundary.md) and [ADR 0006](../../docs/decisions/0006-powershell-child-process-boundary.md).
