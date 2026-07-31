using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Jobs;
using WindowsScriptRunner.Contracts.Jobs;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Web.Security;

namespace WindowsScriptRunner.Web.Pages.Approvals;

[Authorize(Policy = AuthorizationPolicies.Approver)]
public sealed class ReviewModel(
    GetApprovalReviewHandler getApprovalReviewHandler,
    ApproveJobHandler approveJobHandler,
    RejectJobHandler rejectJobHandler,
    IAuthorizationService authorizationService) : PageModel
{
    [BindProperty]
    [StringLength(2000)]
    public string? Comment { get; set; }

    public ApprovalReviewResponse Review { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var (loaded, failure) = await TryLoadReviewAsync(jobId, cancellationToken);
        return loaded ? Page() : failure!;
    }

    public Task<IActionResult> OnPostApproveAsync(
        [FromRoute] Guid jobId,
        string? expectedFingerprint,
        CancellationToken cancellationToken) =>
        DecideAsync(jobId, expectedFingerprint, approve: true, cancellationToken);

    public Task<IActionResult> OnPostRejectAsync(
        [FromRoute] Guid jobId,
        string? expectedFingerprint,
        CancellationToken cancellationToken) =>
        DecideAsync(jobId, expectedFingerprint, approve: false, cancellationToken);

    private async Task<IActionResult> DecideAsync(
        Guid jobId,
        string? expectedFingerprint,
        bool approve,
        CancellationToken cancellationToken)
    {
        var (loaded, failure) = await TryLoadReviewAsync(jobId, cancellationToken);
        if (!loaded)
        {
            return failure!;
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            if (approve)
            {
                await approveJobHandler.HandleAsync(
                    new ApproveJobCommand(new JobId(jobId), expectedFingerprint ?? string.Empty, Comment),
                    cancellationToken);
            }
            else
            {
                await rejectJobHandler.HandleAsync(
                    new RejectJobCommand(new JobId(jobId), expectedFingerprint ?? string.Empty, Comment),
                    cancellationToken);
            }
        }
        catch (ApplicationConflictException)
        {
            ModelState.AddModelError(string.Empty, "The reviewed job changed or cannot be decided. Review the current state before trying again.");
            var (reloaded, reloadFailure) = await TryLoadReviewAsync(jobId, cancellationToken);
            return reloaded ? Page() : reloadFailure!;
        }

        return RedirectToPage("/Approvals/Index");
    }

    private async Task<(bool Loaded, IActionResult? Failure)> TryLoadReviewAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty)
        {
            return (false, NotFound());
        }

        try
        {
            Review = await getApprovalReviewHandler.HandleAsync(
                new GetApprovalReviewQuery(new JobId(jobId)),
                cancellationToken);
        }
        catch (EntityNotFoundException)
        {
            return (false, NotFound());
        }
        catch (ApplicationConflictException)
        {
            return (false, NotFound());
        }

        var authorization = await authorizationService.AuthorizeAsync(
            User,
            Review.Job,
            [new ReviewApprovalRequirement(), new DecideApprovalRequirement()]);
        return authorization.Succeeded ? (true, null) : (false, Forbid());
    }
}
