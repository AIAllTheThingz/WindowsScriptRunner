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
public sealed class IndexModel(
    GetLocalHostInventoryReportHandler getReportHandler,
    ListLocalHostInventoryReportsHandler listReportsHandler,
    GetJobHandler getJobHandler,
    IAuthorizationService authorizationService) : PageModel
{
    private const int MaximumReportCount = 100;

    [BindProperty(SupportsGet = true)]
    public Guid? JobId { get; set; }

    public IReadOnlyList<LocalHostInventoryReportView> Reports { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (JobId is not null)
        {
            try
            {
                var report = await getReportHandler.HandleAsync(
                    new GetLocalHostInventoryReportByJobIdQuery(new JobId(JobId.Value)),
                    cancellationToken);
                var authorized = await AuthorizeReportAsync(report.JobId, cancellationToken);
                if (!authorized)
                {
                    return Forbid();
                }

                Reports = [LocalHostInventoryReportView.FromResponse(report)];
                return Page();
            }
            catch (EntityNotFoundException)
            {
                return NotFound();
            }
        }

        var reports = await listReportsHandler.HandleAsync(
            new ListLocalHostInventoryReportsQuery(MaximumReportCount),
            cancellationToken);
        var authorizedReports = new List<LocalHostInventoryReportView>();
        foreach (var report in reports)
        {
            if (await AuthorizeReportAsync(report.JobId, cancellationToken))
            {
                authorizedReports.Add(LocalHostInventoryReportView.FromResponse(report));
            }
        }

        Reports = authorizedReports;
        return Page();
    }

    private async Task<bool> AuthorizeReportAsync(Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            var job = await getJobHandler.HandleAsync(
                new GetJobQuery(new JobId(jobId)),
                cancellationToken);
            var authorization = await authorizationService.AuthorizeAsync(
                User,
                job,
                [new ViewReportRequirement()]);
            return authorization.Succeeded;
        }
        catch (EntityNotFoundException)
        {
            return false;
        }
    }
}
