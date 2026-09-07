# Authorization matrix

Phase 8 authorization is based on authenticated Windows token SIDs and configured group SIDs. Browser-provided names, role claims, or form fields do not grant access. All listed resource decisions use the `JobDetailResponse` produced by Application and fail closed when identity mapping or the resource is unavailable.

## Policies

| Policy | Required configured capability |
|---|---|
| Fallback / `WindowsScriptRunner.Authenticated` | Authenticated Negotiate principal |
| `WindowsScriptRunner.JobOperator` | Operator or Administrator |
| `WindowsScriptRunner.ReportReader` | ReportReader, Approver, or Administrator |
| `WindowsScriptRunner.Approver` | Approver or Administrator |
| `WindowsScriptRunner.Administrator` | Administrator |

Only static assets and the three health endpoints opt out of the fallback policy. `WindowsScriptRunner.Administrator` protects `/Administration`; Phase 8 exposes no administrative mutation from that page.

## Job resource requirements

| Requirement | Allowed identity/capability | Additional rule |
|---|---|---|
| View job | Requester SID, ReportReader, Approver, or Administrator | Requester comparison uses `sid:<canonical-sid>`, never display name. |
| Modify draft | Requester with Operator or Administrator | The job must be `Draft`; even an Administrator cannot mutate another requester's draft through this requirement. |
| View typed report | Requester SID, ReportReader, Approver, or Administrator | The report's owning job is authorized first. |
| Review approval | Approver or Administrator | Job must be `AwaitingApproval`. |
| Decide approval | Approver or Administrator | Job must be `AwaitingApproval`; Domain still enforces separation of duties. |

The review and decision requirements deliberately do not treat a group as evidence that a requester is independent. `Job.RecordApproval` rejects a Medium, High, or Critical requester's own approval using the authenticated stable identity. Rejection follows the Domain rule, which currently does not impose that same requester restriction.

## Protected portal routes

| Route | Protection and behavior |
|---|---|
| `GET /Account/SignOut` | Authenticated; displays safe Windows-session sign-out guidance. |
| `GET /AccessDenied` | Authenticated; returns 403 guidance. |
| `GET /Administration` | Administrator policy; no administrative mutation is exposed. |
| `GET /Jobs/Details/{jobId:guid}` | Authenticated plus View job resource requirement. |
| `GET /Jobs` | `WindowsScriptRunner.JobOperator`; presents the fixed Local Host Inventory request. |
| `POST /Jobs` | `WindowsScriptRunner.JobOperator` and antiforgery; atomically queues the fixed local, parameterless, ReadOnly/DryRun request using the canonical authenticated actor. |
| `GET /Reports/LocalHostInventory` | Authenticated; lists at most 100 typed reports and filters each through View typed report. |
| `GET /Reports/LocalHostInventory?JobId={jobId}` | Authenticated plus View typed report for that job. Unauthorized lookup is forbidden. |
| `GET /Reports/LocalHostInventory/Details/{reportId:guid}` | Authenticated plus View typed report for the report's job. |
| `GET /Approvals` | Approver policy; lists at most 100 `AwaitingApproval` jobs. |
| `GET /Approvals/Review/{jobId:guid}` | Approver policy plus Review and Decide resource requirements. |
| `POST /Approvals/Review/{jobId:guid}?handler=Approve` | Same review/decision authorization and ASP.NET Core antiforgery validation. |
| `POST /Approvals/Review/{jobId:guid}?handler=Reject` | Same review/decision authorization and ASP.NET Core antiforgery validation. |

`/health`, `/health/live`, `/health/ready`, and static assets are anonymous application surfaces; the
operator must explicitly verify these responses after IIS Windows Authentication is enabled. No route
uploads scripts, dispatches a worker directly, retrieves credentials, starts PowerShell, downloads raw
execution output, or supplies generic reporting. The Jobs request remains browser-parameterless and
targets exactly the approved Worker host whose stable `Worker:NodeId` matches
`Automation:LocalHostInventory:ApprovedWorkerNodeId` (environment key
`Automation__LocalHostInventory__ApprovedWorkerNodeId`). It stores one `worker:<canonical-guid>`
target and does not target the IIS or browser machine. Missing, empty, or invalid configuration, or
a missing or disabled registered Worker, fails closed before writes; a legacy `local-worker` job is
not silently retargeted.
