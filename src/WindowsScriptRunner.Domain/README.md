# Domain

Domain is the independent policy core and has no solution-project or infrastructure dependency.

It owns:

- strongly typed identifiers and validated value objects;
- script definitions, immutable published versions, and parameter definitions;
- the Job aggregate, targets, explicit parameter bindings, policy snapshots, approvals, executions, status transitions, and fenced leases;
- Worker nodes and capabilities;
- credential references that contain no raw credential material;
- bounded audit events; and
- the immutable `JobReport` envelope with typed `LocalHostInventoryReportPayload`.

All aggregate operations validate proposed state before mutation. Null, empty, and whitespace parameter input means no explicit binding. Active execution attempts cannot be orphaned by generic terminal transitions.

Report construction enforces the only supported package/type/schema/format, provenance identifiers, positive fencing, timestamp ordering, bounded typed inventory, supported architecture and versions, lowercase SHA-256, and deterministic `JobReportId`.

See [Domain model](../../docs/domain-model.md) and [Job lifecycle](../../docs/job-lifecycle.md).
