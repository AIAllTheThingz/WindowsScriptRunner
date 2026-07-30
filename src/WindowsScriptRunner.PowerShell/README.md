# PowerShell

PowerShell owns the isolated PowerShell 7 child-process boundary. It provides:

- `pwsh.exe` discovery and constant runtime probing;
- trusted local `.ps1` path, reparse-point, and SHA-256 validation;
- literal `ProcessStartInfo.ArgumentList` construction;
- a minimized child environment;
- per-execution working-directory reservation and cleanup;
- concurrent bounded stdout/stderr capture;
- distinct timeout, cancellation, output-overflow, and exit results; and
- Windows Job Object process-tree containment with a bounded fallback.

The public boundary accepts only an internally created `TrustedPowerShellScript`; it has no arbitrary path, command-text, or script-text API. A narrow reviewed-artifact factory is accessible only to Automation, which compile-pins the one supported package. Web and Worker do not reference PowerShell directly.

Successful package output is returned to Automation as bounded in-memory data. Reporting validates it, and Infrastructure persists only typed report fields; PowerShell never persists output.

See [PowerShell execution boundary](../../docs/powershell-execution-boundary.md), [security](../../docs/security.md), and [ADR 0006](../../docs/decisions/0006-powershell-child-process-boundary.md).
