using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WindowsScriptRunner.Application.Jobs;
using WindowsScriptRunner.Contracts.Jobs;
using WindowsScriptRunner.Web.Security;

namespace WindowsScriptRunner.Web.Pages.Approvals;

[Authorize(Policy = AuthorizationPolicies.Approver)]
public sealed class IndexModel(ListAwaitingApprovalJobsHandler listAwaitingApprovalJobsHandler) : PageModel
{
    public IReadOnlyList<JobDetailResponse> Jobs { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Jobs = await listAwaitingApprovalJobsHandler.HandleAsync(
            new ListAwaitingApprovalJobsQuery(100),
            cancellationToken);
    }
}
