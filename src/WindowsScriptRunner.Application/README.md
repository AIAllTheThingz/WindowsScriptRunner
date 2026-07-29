# Application

Contains Phase 2 use-case handlers plus focused repository, credential-reference, audit, clock, current-user, fingerprint, and unit-of-work abstractions. Handlers validate related references, derive job-parameter classification and absent-value policy from pinned script definitions for writes and reads, skip credential lookup for accepted absence, emit bounded clear/set audit events, route active execution completion through a dedicated outcome handler, and leave persistence implementations intentionally absent.
