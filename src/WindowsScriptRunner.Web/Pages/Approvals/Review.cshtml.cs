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

    public Task<IActionResult> OnGetAsync(Guid jobId, CancellationToken cancellationToken) =>
        LoadReviewAsync(jobId, cancellationToken);

    public Task<IActionResult> OnPostApproveAsync(
        Guid jobId,
        string? expectedFingerprint,
        CancellationToken cancellationToken) =>
        DecideAsync(jobId, expectedFingerprint, approve: true, cancellationToken);

    public Task<IActionResult> OnPostRejectAsync(
        Guid jobId,
        string? expectedFingerprint,
        CancellationToken cancellationToken) =>
        DecideAsync(jobId, expectedFingerprint, approve: false, cancellationToken);

    private async Task<IActionResult> DecideAsync(
        Guid jobId,
        string? expectedFingerprint,
        bool approve,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadReviewAsync(jobId, cancellationToken);
        if (loaded is not PageResult)
        {
            return loaded;
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
            return Page();
        }

        return RedirectToPage("/Approvals/Index");
    }

    private async Task<IActionResult> LoadReviewAsync(Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            Review = await getApprovalReviewHandler.HandleAsync(
                new GetApprovalReviewQuery(new JobId(jobId)),
                cancellationToken);
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }
        catch (ApplicationConflictException)
        {
            return NotFound();
        }

        var authorization = await authorizationService.AuthorizeAsync(
            User,
            Review.Job,
            [new ReviewApprovalRequirement(), new DecideApprovalRequirement()]);
        return authorization.Succeeded ? Page() : Forbid();
    }
}
