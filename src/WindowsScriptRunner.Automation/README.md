# Automation

Automation is the narrow reviewed bridge from Worker composition to PowerShell execution.

It owns exactly one production package:

- ID `windows.local-host-inventory`
- version `1.0.0`
- ReadOnly risk
- local Worker execution
- DryRun only
- JSON report format
- no parameters, credentials, remoting, network calls, or side effects

The catalog compile-pins the package identity, stable definition/version IDs, relative artifact path, SHA-256, empty parameter allowlist, minimum PowerShell version, and timeout. Configuration may only enable the package, opt into idempotent registration, and select trusted and working roots.

The handler validates current fenced ownership and the pinned script, starts DryRun, invokes only the PowerShell boundary, and maps controlled failures. A code-zero result must pass the strict Reporting parser. Parse failure completes as a controlled failure with no report; a valid typed value goes to the atomic Application completion handler. Raw stdout and stderr remain in memory only until parsing finishes.

Automation is not a plugin system and exposes no arbitrary script, path, command, parameter, report type, schema, or output-persistence surface.

See [ADR 0007](../../docs/decisions/0007-first-production-automation-package.md), [Worker queue](../../docs/worker-queue.md), and [Reporting](../WindowsScriptRunner.Reporting/README.md).
