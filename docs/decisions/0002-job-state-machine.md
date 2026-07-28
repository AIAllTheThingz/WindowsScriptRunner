# ADR 0002: Job state machine

Status: Accepted

`JobStatusPolicy` defines allowed transitions, and only `Job` mutates status. Invalid, self, skipped, and terminal-origin transitions throw `InvalidJobStateTransitionException`.

Keeping policy in Domain makes the same lifecycle apply to Web, Worker, and future persistence. Infrastructure must never update status around this policy.
