# Approval workflow

Phase 8 presents the existing Domain/Application approval and rejection workflow through a thin authenticated Razor Pages interface. Web does not change job state directly, calculate policy, create an audit event, or acquire/release a lease.

## Review and decision sequence

1. Application returns at most 100 jobs whose persisted state is `AwaitingApproval` for `/Approvals`.
2. An Approver or Administrator requests a review. Web loads the typed job detail and server-calculated expected fingerprint, then applies both Review and Decide resource requirements.
3. The review view shows the safe job fields, targets, and already-redacted parameter display values. It renders the expected fingerprint only as an antiforgery-protected form value; it does not treat it as browser authority.
4. An approve or reject POST first reloads and reauthorizes the review. ASP.NET Core antiforgery validation runs before the handler.
5. Web sends only `JobId`, the browser's expected fingerprint echo, and an optional bounded comment to the Application handler. The authenticated actor comes from `ICurrentUser`, which maps the Negotiate principal's stable SID.
6. Application reloads the job, recomputes the trusted fingerprint, compares in constant time, and delegates the decision to Domain. It writes the bounded audit event and commits through the existing unit of work.
7. A successful decision uses POST-Redirect-GET to `/Approvals`. A stale fingerprint, stale state, invalid decision, or separation-of-duties conflict returns the review page with one generic conflict message and no Domain or SQL detail.

Replaying a completed decision cannot record a second approval: the job is no longer `AwaitingApproval`, so resource authorization prevents a subsequent decision request. A valid antiforgery token does not bypass that status, identity, fingerprint, or Domain check.

## Separation of duties

`RequestedBy` is persisted only from the stable SID identity supplied by `ICurrentUser` at draft creation; the create command has no requester field. `ApproveJobHandler` does not accept an actor in its command and obtains the current authenticated identity through `ICurrentUser`. Domain requires the persisted Execute requester to be in canonical `sid:S-1-...` form before it can enter or receive an approval decision, and rejects approval by the requester for Medium, High, and Critical policy snapshots. A user cannot change this outcome by posting a different requester, approver, role, risk, or separation-of-duties field.

The Domain rule remains authoritative because its policy snapshot was captured from the pinned published script at submission. Web group membership only grants permission to attempt review or decision; it does not rewrite policy or prove independence.

## Scope and safety

The workflow exposes no script content, raw stdout/stderr, arbitrary JSON, lease credentials, fencing token, execution working files, credential reference, or secret. It creates no generic approval API and no browser-supplied approval fingerprint authority. Existing job-state, audit, and lease invariants remain in Domain/Application/Infrastructure.
