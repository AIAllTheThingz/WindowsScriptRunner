# ADR 0009: Windows SID identity and trusted approval evidence

- Status: Accepted
- Date: 2026-07-30

## Context

Phase 7 stored safe typed inventory reports but had no authenticated Web portal. Approval commands previously needed a trusted actor and evidence binding to prevent a browser from changing reviewed content, impersonating a requester, or asserting its own separation of duties. The application is Windows-hosted and currently has no password, token issuer, external identity provider, or application session requirement.

## Decision

Use ASP.NET Core Negotiate authentication with an authenticated-by-default Web fallback policy. Map the authenticated Windows principal to `UserIdentity` as `sid:<canonical-user-sid>`. Resolve exactly one user SID from authenticated `PrimarySid`, then `Sid`, then the trusted `WindowsIdentity.User` fallback. Combine valid authenticated group SID claims with canonical `WindowsIdentity.Groups`; never map a user name or role claim into identity or group authority. Validate configured Operator, ReportReader, Approver, and Administrator group SIDs at startup, rejecting broad/service SIDs and duplicates.

Authorize portal capabilities through group policies and job-resource requirements. Ownership compares stable requester SID values. Draft modification requires ownership even for administrators. Approval review/decision requires an awaiting-approval job and an Approver or Administrator; Domain retains risk-aware separation-of-duties enforcement.

Create approval evidence in Application, not Web. The fingerprint service canonicalizes the pinned script/policy, job details, sorted targets and parameters, execution window, and accepted dry-run evidence, hashes the exact bytes with SHA-256, and validates a browser echo in constant time at decision time. Approval/rejection commands receive no caller actor or risk.

## Consequences

- Typed report views can be presented only after authenticated policy and resource checks, using an explicit safe view model.
- Approval, audit, job-state, and lease mutations remain in Domain/Application/Infrastructure; Web is an authenticated adapter.
- Browser form fields cannot grant role, ownership, identity, policy, fingerprint, or separation-of-duties authority.
- There is no application sign-out because Negotiate uses the Windows/browser session; the portal provides safe guidance instead.
- Test-only synthetic Windows identities are isolated to the test project. Production always uses Negotiate.
- This decision does not add IIS configuration, HTTPS configuration, service accounts, SPNs, Kerberos/delegation validation, external identity federation, credential retrieval, or deployment. Those are Phase 9 concerns.
