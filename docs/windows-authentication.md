# Windows authentication

Phase 8 adds the first protected portal for the local/server-hosted Windows application. It uses ASP.NET Core Negotiate authentication only. Every non-static, non-health endpoint is protected by the fallback authorization policy; static assets and `/health`, `/health/live`, and `/health/ready` are explicitly anonymous.

## Stable identity mapping

`WindowsPrincipalMapper` creates the Application `UserIdentity` from one authenticated Windows user SID and stores it as `sid:<canonical-sid>`. It never derives the identity from a browser-supplied display name, a role claim, an approval form field, or a separation-of-duties assertion.

The mapper accepts exactly one user SID in this order:

1. one authenticated `PrimarySid` claim;
2. one authenticated `Sid` claim when `PrimarySid` is absent; or
3. one `WindowsIdentity.User` SID from the authenticated Windows token when no SID claim is present.

Ambiguous, malformed, missing, control-character, or built-in-group user SIDs fail closed. Display name is presentation-only and is not used for ownership or approval decisions.

`CreateDraftJobHandler` obtains `RequestedBy` only from this mapped `ICurrentUser`; the create command has no requester field. Execute approval, rejection, and entry into the approval state additionally require the persisted requester to use the canonical `sid:S-1-...` representation. The evidence migration terminalizes legacy pending Execute jobs instead of allowing a historical non-SID requester representation to bypass separation of duties.

Completed Phase 7 records can retain a non-SID `RequestedBy` value. The portal deliberately does not infer a SID from such a name, display name, UPN, or browser claim: that would turn an unverifiable historical string into an ownership grant. Those records remain available to configured report-reader, approver, and administrator groups, but not as self-service ownership for an ordinary requester. A controlled offline data-reconciliation process, using an authoritative directory record and an auditable migration plan, is deferred; it is not a Phase 8 browser feature.

Group membership is a set of canonical Windows SIDs. Valid authenticated `GroupSid` claims are combined with `WindowsIdentity.Groups` from the authenticated Windows token. The token-group fallback is Windows-only, excludes the user SID, and does not use names. Invalid claims and a claim equal to the user SID grant nothing.

## Group configuration

The `WindowsAuthorization` section holds only role-to-group SID mappings:

```text
WindowsAuthorization__OperatorGroupSids__0=<operator-group-sid>
WindowsAuthorization__ReportReaderGroupSids__0=<report-reader-group-sid>
WindowsAuthorization__ApproverGroupSids__0=<approver-group-sid>
WindowsAuthorization__AdministratorGroupSids__0=<administrator-group-sid>
```

The placeholders are intentionally not real SIDs. Obtain real domain or local group SIDs through an approved Windows administration process and keep environment-specific values out of source control. Startup validates every configured value as a SID, normalizes it, rejects duplicates, and rejects Everyone, Anonymous, and service-account SIDs. At least one administrator group is required outside the `Testing` environment. Group names, user names, and role claims are not configuration substitutes.

`ConnectionStrings__WindowsScriptRunner` is likewise supplied through a protected environment configuration source, user secrets, or an approved external configuration provider; it is never committed.

## Sign-in, sign-out, and denial behavior

There is no application password, cookie, or bootstrap account. A browser reaching a protected route receives a Negotiate challenge and Windows authenticates the request. `/AccessDenied` returns a protected 403 page for an authenticated but unauthorized user.

`/Account/SignOut` explains the safe sign-out behavior. Negotiate uses the browser and operating-system Windows session rather than an application session, so the portal has no server-side session to revoke. To use another account, follow the host policy to end or change the browser/Windows session. Phase 8 does not add a password flow, impersonation, account provisioning, or credential storage.

## Test-only synthetic identities

Web integration tests use a synthetic authentication scheme defined exclusively in `tests/WindowsScriptRunner.SecurityTests`. It supplies deterministic test SIDs and groups to an in-memory test host because Negotiate requires a real Windows-capable server connection feature. The production Web project contains no test or synthetic authentication scheme and always registers Negotiate.

Synthetic test claims prove portal policy behavior; they are not an Active Directory, Kerberos, constrained-delegation, IIS, Kestrel, SPN, browser-zone, or domain-trust validation. Those operational concerns, together with HTTPS and IIS hosting hardening, remain Phase 9 work.
