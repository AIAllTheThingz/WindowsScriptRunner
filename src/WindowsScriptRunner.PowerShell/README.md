# PowerShell

Phase 5 implements the isolated PowerShell 7 child-process boundary. This project owns `pwsh.exe` discovery and probing, trusted local `.ps1` path and SHA-256 validation, literal `ArgumentList` construction, a minimized environment, per-execution working directories, concurrent bounded stream capture, timeout/cancellation distinction, and Windows Job Object process-tree containment.

The public boundary accepts only an internally created `TrustedPowerShellScript`; it has no arbitrary path, command-text, or script-text API. Phase 6 adds a narrow reviewed-artifact factory used only by `WindowsScriptRunner.Automation`. That project pins the identity, path, SHA-256, and empty parameter allowlist for `windows.local-host-inventory` `1.0.0`. Web and Worker do not reference this project directly; enabled Worker-side Automation composition registers it.

See [PowerShell execution boundary](../../docs/powershell-execution-boundary.md) and [ADR 0006](../../docs/decisions/0006-powershell-child-process-boundary.md).
