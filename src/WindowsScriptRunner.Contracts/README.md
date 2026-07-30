# Contracts

Contains immutable, behavior-free Phase 2 request and response DTOs. It does not reference Domain and never exposes raw sensitive parameter values.

Phase 7 adds one immutable Local Host Inventory report response with typed envelope, provenance, timestamps, inventory fields, and digest. It exposes no EF or Domain entity, raw stdout/stderr, audit internals, or arbitrary JSON.
