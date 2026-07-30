# Contracts

Contracts contains immutable, behavior-free transport DTOs and has no solution-project references.

Current contracts cover scripts, jobs, workers, errors, and the typed Local Host Inventory report response. Sensitive job parameters are represented only through redaction metadata; raw sensitive values, credential material, EF entities, Domain entities, audit internals, stdout, stderr, and arbitrary JSON never cross this boundary.

The Local Host Inventory response contains a typed report envelope, bounded provenance, timestamps, inventory fields, and deterministic digest. Authentication and authorization for exposing that response through Web belong to Phase 8.

See [Application contracts](../../docs/application-contracts.md).
