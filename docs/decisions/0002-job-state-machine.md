# ADR 0002: Job state machine

Status: Accepted

`JobStatusPolicy` is an internal transition engine, and only `Job` mutates status. The aggregate exposes intention-revealing operations rather than a public generic transition method. Submitted, Approved, Rejected, Executing, Completed, and CompletedWithWarnings are protected by the operations that validate or create their required evidence. Invalid, self, skipped, and terminal-origin transitions throw `InvalidJobStateTransitionException`.

Validation occurs before state or child-collection mutation. Keeping policy in Domain makes the same lifecycle apply to Web, Worker, and future persistence. Infrastructure must never update status around this policy.
