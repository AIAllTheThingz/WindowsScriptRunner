using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Jobs;
using WindowsScriptRunner.Application.Reports;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Web.Security;

namespace WindowsScriptRunner.Web.Pages.Reports.LocalHostInventory;

[Authorize]
public sealed class DetailsModel(
    GetLocalHostInventoryReportHandler getReportHandler,
    GetJobHandler getJobHandler,
    IAuthorizationService authorizationService) : PageModel
{
    public LocalHostInventoryReportView Report { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid reportId, CancellationToken cancellationToken)
    {
        if (reportId == Guid.Empty)
        {
            return NotFound();
        }

        try
        {
            var response = await getReportHandler.HandleAsync(
                new GetLocalHostInventoryReportByIdQuery(new JobReportId(reportId)),
                cancellationToken);
            var job = await getJobHandler.HandleAsync(
                new GetJobQuery(new JobId(response.JobId)),
                cancellationToken);
            var authorized = await authorizationService.AuthorizeAsync(
                User,
                job,
                [new ViewReportRequirement()]);
            if (!authorized.Succeeded)
            {
                return Forbid();
            }

            Report = LocalHostInventoryReportView.FromResponse(response);
            return Page();
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }
    }
}
