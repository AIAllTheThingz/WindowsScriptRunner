# Domain

Contains the independent Phase 2 domain model: strongly typed ID classes, value objects, script and job aggregates, explicit binding-only job parameters with aggregate-controlled idempotent clearing, atomic validation-before-mutation rules, requested-phase lifecycle policy, execution-outcome terminalization, defined-enum guards, worker metadata, audit events, and credential references. Null, empty, and whitespace parameter values are represented by no explicit binding. It has no infrastructure dependency.
