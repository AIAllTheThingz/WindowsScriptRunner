using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Jobs;
using WindowsScriptRunner.Contracts.Jobs;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Web.Security;

namespace WindowsScriptRunner.Web.Pages.Jobs;

[Authorize]
public sealed class DetailsModel(
    GetJobHandler getJobHandler,
    IAuthorizationService authorizationService) : PageModel
{
    public JobDetailResponse Job { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty)
        {
            return NotFound();
        }

        try
        {
            Job = await getJobHandler.HandleAsync(
                new GetJobQuery(new JobId(jobId)),
                cancellationToken);
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }

        var result = await authorizationService.AuthorizeAsync(
            User,
            Job,
            [new ViewJobRequirement()]);
        return result.Succeeded ? Page() : Forbid();
    }
}
