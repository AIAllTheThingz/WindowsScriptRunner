# ADR 0002: Job state machine

Status: Accepted

`JobStatusPolicy` is an internal transition engine, and only `Job` mutates status. The aggregate exposes intention-revealing operations rather than a public generic transition method. Submitted, Approved, Rejected, Executing, Completed, and CompletedWithWarnings are protected by the operations that validate or create their required evidence. Invalid, self, skipped, and terminal-origin transitions throw `InvalidJobStateTransitionException`.

Validation occurs before state or child-collection mutation. Keeping policy in Domain makes the same lifecycle apply to Web, Worker, and future persistence. Infrastructure must never update status around this policy.

Requested phase is part of the aggregate and is not caller-overridable after draft creation. Phase 2 supports submission for `Validation`, `DryRun`, and `Execute`; other enum values are rejected during submission, and undefined enum values are rejected before mutation. Validation-only and dry-run-only terminal paths are explicit, and only Execute requests can reach approval, queueing, claiming, execution, post-validation, or execution outcomes.

Active execution attempts are not terminalized through direct job-status operations. The aggregate requires a single active `JobExecution` to be completed through the execution-outcome operation, which records the outcome on the attempt and moves the parent job to the corresponding terminal status atomically.
