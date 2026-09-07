# Web

Web is the ASP.NET Core Razor Pages composition root. It registers Application and Infrastructure, serves protected portal views and static assets, and exposes:

- `/health` — process liveness
- `/health/live` — process liveness
- `/health/ready` — SQL connectivity and migration readiness

Windows Negotiate is the only production authentication scheme. The fallback policy protects all non-static, non-health routes. `WindowsPrincipalMapper` maps the authenticated Windows user to `sid:<canonical-sid>` and group policies use only configured Windows group SIDs. Configure `WindowsAuthorization` from protected environment-specific configuration; group names and user names are not accepted substitutes.

Protected portal pages include:

- `/Account/SignOut` and `/AccessDenied` safe session/denial guidance;
- `/Administration` under the Administrator policy, with no administrative mutation;
- `/Jobs/Details/{jobId:guid}` after job resource authorization;
- `/Jobs` for an antiforgery-protected `WindowsScriptRunner.JobOperator` request of the fixed local,
  parameterless, ReadOnly/DryRun Local Host Inventory package; the canonical authenticated actor is
  supplied by `ICurrentUser` and the request queues the eligible local Worker atomically;
- `/Reports/LocalHostInventory` and `/Reports/LocalHostInventory/Details/{reportId:guid}` for safe typed inventory list, lookup, and detail; and
- `/Approvals` and `/Approvals/Review/{jobId:guid}` for authorized antiforgery-protected approval/rejection review.

Job submission and approval POSTs delegate to Application. The authenticated actor comes from
`ICurrentUser`; the fixed request uses no browser-supplied package, target, or parameters, and the
Application handler commits the job and audit event atomically. A successful request redirects to job
details. Approval handlers recompute trusted fingerprint evidence and Domain retains separation of
duties rules. A successful decision redirects to the queue; stale or invalid decisions show a generic
error.

Web does not:

- execute PowerShell;
- reference Worker, Automation, PowerShell, or Reporting;
- expose raw stdout/stderr, arbitrary JSON, report downloads, lease/fencing/working-file provenance, or secure references;
- trust browser user names, roles, fingerprints, or separation-of-duties claims;
- upload or select arbitrary scripts, inject credentials, retrieve secrets, or dispatch arbitrary
  work; or
- include production IIS configuration.

The local Worker host is the eligible queue claimant; the request does not run on the IIS or browser
machine. No generic package discovery or upload exists. Negotiate sign-out is a Windows/browser-session
concern, not an application cookie operation. IIS/TLS/SPN/Kerberos/browser-zone validation and
deployment hardening remain Phase 9 work. See [Windows authentication](../../docs/windows-authentication.md),
[authorization matrix](../../docs/authorization-matrix.md), and [approval workflow](../../docs/approval-workflow.md).

See [Development setup](../../docs/development-setup.md), [Architecture](../../docs/architecture.md), and [IIS deployment status](../../deployment/iis/README.md).
