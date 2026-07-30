# Reporting

Reporting is the focused Phase 7 trust boundary for `windows.local-host-inventory` version `1.0.0`. It has no solution-project dependencies.

`LocalHostInventoryReportParser` accepts only a complete successful process-result abstraction. It enforces an 8 KiB strict UTF-8 limit, exact case-sensitive JSON schema, duplicate and unknown property rejection at every object level, bounded safe strings, conservative computer-name validation, supported architecture, invariant OS and PowerShell versions, PowerShell `>= 7.4.0`, whitespace-only success stderr, and an offset-bearing round-trip collection timestamp within a five-second execution-window tolerance.

The parser returns one immutable `ValidatedLocalHostInventoryReport`. `LocalHostInventoryCanonicalizer` creates a deterministic SHA-256 over stable provenance and typed inventory values. Neither component logs, persists, or returns raw stdout, stderr, or arbitrary JSON.

Reporting is not a generic report engine. It does not support CSV, HTML, text reports, uploads, user schemas, arbitrary payloads, visual design, additional packages, or Web presentation.
