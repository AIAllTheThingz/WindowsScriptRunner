# ADR 0001: Strongly typed identifiers

Status: Accepted

Each primary domain concept uses a dedicated immutable GUID-backed sealed record class. Construction rejects `Guid.Empty`, `New()` makes creation explicit, there is no public parameterless constructor, and external DTOs expose GUID values.

This prevents accidental cross-aggregate ID substitution without adding a third-party package and closes the default-struct path that could silently create an ID around `Guid.Empty`. Aggregate and entity boundaries also reject null IDs. Persistence conversion is deferred to Phase 3.
