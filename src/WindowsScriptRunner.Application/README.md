# Application

Contains Phase 2 use-case handlers plus focused repository, credential-reference, audit, clock, current-user, fingerprint, and unit-of-work abstractions. Handlers validate related references, derive job-parameter classification from pinned script definitions for writes and reads, route active execution completion through a dedicated outcome handler, use safe audit metadata, and leave persistence implementations intentionally absent.
