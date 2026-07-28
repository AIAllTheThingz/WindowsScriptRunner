# ADR 0001: Strongly typed identifiers

Status: Accepted

Each primary domain concept uses a dedicated immutable GUID-backed record struct. Construction rejects `Guid.Empty`, `New()` makes creation explicit, and external DTOs expose GUID values.

This prevents accidental cross-aggregate ID substitution without adding a third-party package. Persistence conversion is deferred to Phase 3.
