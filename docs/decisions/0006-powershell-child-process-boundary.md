# ADR 0006: PowerShell child-process boundary

## Status

Accepted for Phase 5; production integration is deferred to Phase 6.

## Context

Windows Script Runner needs eventual PowerShell automation without allowing arbitrary command execution, in-process engine state, unbounded output, inherited secrets, or orphaned descendants. Phase 5 needs a testable boundary but must not connect the production queue.

## Decision

Run compatible Windows PowerShell Core through a discovered and fixed `pwsh.exe` child process. Keep all process APIs, fixed runtime probing, trusted-path and hash validation, `ArgumentList` construction, minimized environment, isolated working directories, concurrent bounded capture, timeout/cancellation handling, and Windows Job Object containment in `WindowsScriptRunner.PowerShell`.

Expose one request-based interface whose script artifact cannot be publicly constructed from strings. Trust only the controlled test fixture in Phase 5. Keep Web and Worker independent of the PowerShell project and register the boundary only when explicitly requested by tests or a future reviewed composition root.

## Consequences

The application has no PowerShell SDK or runspace dependency, caller values never become command text, process output is bounded, and timeout/cancellation can terminate descendants. Nonzero exits remain ordinary structured results.

Command-line values remain visible to OS inspection, so secrets are prohibited. Hash validation and Job Object assignment each retain a small race. Job Objects do not sandbox filesystem, registry, network, privileges, or PowerShell language behavior. Phase 6 must design production artifact resolution, secret delivery, and queue integration separately.
