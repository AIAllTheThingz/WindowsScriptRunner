using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Jobs;
using WindowsScriptRunner.Web.Security;

namespace WindowsScriptRunner.Web.Pages;

[Authorize(Policy = AuthorizationPolicies.JobOperator)]
public sealed class JobsModel(RequestLocalHostInventoryHandler requestHandler) : PageModel
{
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        try
        {
            var jobId = await requestHandler.HandleAsync(
                new RequestLocalHostInventoryCommand(),
                cancellationToken);
            return RedirectToPage("/Jobs/Details", new { jobId = jobId.Value });
        }
        catch (ApplicationConflictException)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            ModelState.AddModelError(
                string.Empty,
                "The reviewed Local Host Inventory package is unavailable. Contact an administrator before trying again.");
            return Page();
        }
    }
}
