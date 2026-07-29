# ADR 0004: SQL Server persistence

- Status: Accepted
- Date: 2026-07-29

## Context

Application already defines aggregate-specific repositories, append-only audit writing, and a unit-of-work boundary. Persistence must preserve Domain independence, commit aggregate and audit changes atomically, reject stale writes, avoid secret leakage, and support controlled production deployment.

## Decision

Use EF Core with the SQL Server provider in Infrastructure. Map internal persistence row types explicitly to the `wsr` schema and reconstruct aggregates through internal validated Domain factories. Share one scoped context across repositories, audit writer, and unit of work. Only the unit of work saves. Use SQL Server rowversion for mutable roots, database constraints and triggers for storage-level integrity, and a dedicated migration history table in `wsr`.

Disable startup migration by default. Expose SQL-dependent readiness separately from process liveness. Validate migrations and persistence behavior against real SQL Server rather than EF InMemory or SQLite.

## Consequences

Domain and Application remain free of EF and SQL dependencies. Persistence adds mapping code and requires SQL Server for integration tests. Deployments must apply reviewed migrations before application rollout. Database constraints intentionally duplicate selected domain invariants as defense in depth.
