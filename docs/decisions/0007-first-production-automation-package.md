# ADR 0007: First production automation package

## Status

Accepted for Phase 6.

## Context

Phase 5 provides a secure PowerShell child-process boundary, while Phase 4 provides fenced, at-least-once Worker leases. Production needs one useful automation path without introducing arbitrary script selection, general package discovery, secret injection, remoting, or report storage.

## Decision

Create `WindowsScriptRunner.Automation` as the only bridge from Worker composition to PowerShell. It contains exactly one reviewed artifact:

- package `windows.local-host-inventory`;
- version `1.0.0`;
- ReadOnly risk;
- local Worker execution;
- DryRun only;
- no parameters or credentials; and
- one bounded JSON document containing schema version, computer name, OS description/version/architecture, PowerShell version, and UTC collection time.

Compile-pin stable definition/version IDs, relative path, SHA-256, phases, format, timeout, minimum runtime, and the empty parameter allowlist. Configuration may only enable the package, opt into exact idempotent registration, and select the trusted and working roots.

Extend queue routing from work kind alone to `(JobWorkKind, ScriptVersionId)`. Candidate discovery filters this exact route in SQL, and claimed descriptors carry only the pinned version ID plus existing fenced lease metadata.

Register a singleton package handler that creates fresh scopes for ownership inspection, aggregate loading, and every lifecycle mutation. Resolve the exact immutable script definition/version against the catalog, create the trusted artifact through the PowerShell-owned reviewed factory, derive execution identity from `JobId`, and invoke only `IPowerShellExecutionBoundary`.

On success, atomically complete the read-only DryRun and remove the lease. Map nonzero exit, timeout, output overflow, trust failure, runtime/startup failure, and caller cancellation to bounded controlled outcomes. Never mutate terminal state with stale lease credentials. Do not log or persist stdout, stderr, inventory JSON, parameters, or secrets.

## Consequences

- A real queued job can safely reach the Phase 5 boundary.
- Unknown or tampered packages fail before process launch and are not repeatedly claimed.
- Phase 5 path, hash, reparse, containment, timeout, cancellation, and output limits remain authoritative.
- Registration is explicit, transactional, auditable, idempotent, and disabled by default.
- The package is externally safe to retry, but execution remains at least once rather than exactly once.
- Operators receive no durable inventory report in Phase 6; output persistence is deferred to Phase 7.
- This does not provide authentication, authorization, deployment readiness, an operating-system sandbox, general package plugins, remoting, or secret delivery.

## Alternatives not selected

- Supporting Execute would force approval/execution states that do not fit this read-only, no-side-effect package. DryRun-only uses the existing read-only completion invariant.
- Routing by `JobWorkKind` alone would claim unsupported versions and repeatedly release them.
- Configuration-defined manifests, paths, hashes, or parameter allowlists would move trust to mutable runtime state.
- Putting orchestration in Worker or Web would collapse project boundaries and expose execution to the wrong composition root.
- Persisting raw stdout as a report would introduce a reporting trust and storage design outside Phase 6.
