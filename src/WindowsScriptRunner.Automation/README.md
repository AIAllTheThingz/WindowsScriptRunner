# Automation

Automation owns exactly one reviewed production package: `windows.local-host-inventory` version `1.0.0`. Its identity, stable IDs, path, SHA-256, empty parameter allowlist, ReadOnly risk, DryRun-only phase, JSON format, minimum PowerShell version, and timeout are compile-pinned.

The production handler validates current fenced ownership and the pinned script, starts DryRun, invokes only the PowerShell boundary, and maps controlled failures. For code-zero results it adapts the complete bounded result to Reporting. A strict parse failure terminalizes as failed without a report. A valid typed inventory value is submitted to the package-specific atomic Application completion command. Raw stdout and stderr remain in memory only until parsing finishes.

Automation is not a plugin system and exposes no arbitrary script, parameter, report type, schema, or output persistence surface.
