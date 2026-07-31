using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using WindowsScriptRunner.Contracts.Jobs;
using WindowsScriptRunner.Domain;

namespace WindowsScriptRunner.Web.Security;

public sealed record ViewJobRequirement : IAuthorizationRequirement;

public sealed record ModifyDraftJobRequirement : IAuthorizationRequirement;

public sealed record ViewReportRequirement : IAuthorizationRequirement;

public sealed record ReviewApprovalRequirement : IAuthorizationRequirement;

public sealed record DecideApprovalRequirement : IAuthorizationRequirement;

public sealed class JobResourceAuthorizationHandler(
    IAuthenticatedPrincipalMapper principalMapper,
    IOptions<WindowsAuthorizationOptions> options) : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Resource is not IJobAuthorizationResource job || !TryMap(context, out var principal))
        {
            return Task.CompletedTask;
        }

        foreach (var requirement in context.Requirements)
        {
            switch (requirement)
            {
                case ViewJobRequirement when IsRequester(principal, job) || HasAnyCapability(
                    principal,
                    WindowsAuthorizationCapability.ReportReader,
                    WindowsAuthorizationCapability.Approver,
                    WindowsAuthorizationCapability.Administrator):
                    context.Succeed(requirement);
                    break;
                case ModifyDraftJobRequirement when
                    string.Equals(job.Status, nameof(JobStatus.Draft), StringComparison.Ordinal) &&
                    IsRequester(principal, job) &&
                    HasAnyCapability(
                        principal,
                        WindowsAuthorizationCapability.Operator,
                        WindowsAuthorizationCapability.Administrator):
                    context.Succeed(requirement);
                    break;
                case ViewReportRequirement when IsRequester(principal, job) || HasAnyCapability(
                    principal,
                    WindowsAuthorizationCapability.ReportReader,
                    WindowsAuthorizationCapability.Approver,
                    WindowsAuthorizationCapability.Administrator):
                    context.Succeed(requirement);
                    break;
                case ReviewApprovalRequirement when CanActOnApproval(principal, job):
                case DecideApprovalRequirement when CanActOnApproval(principal, job):
                    context.Succeed(requirement);
                    break;
            }
        }

        return Task.CompletedTask;
    }

    private bool TryMap(AuthorizationHandlerContext context, out AuthenticatedPrincipal principal)
    {
        try
        {
            principal = principalMapper.Map(context.User);
            return true;
        }
        catch (AuthenticationMappingException)
        {
            principal = null!;
            return false;
        }
    }

    private bool HasAnyCapability(
        AuthenticatedPrincipal principal,
        params WindowsAuthorizationCapability[] capabilities)
    {
        var configuredGroupSids = WindowsGroupMembershipHandler.GetConfiguredGroupSids(
            options.Value,
            capabilities);
        return principal.GroupSids.Overlaps(configuredGroupSids);
    }

    private bool CanActOnApproval(AuthenticatedPrincipal principal, IJobAuthorizationResource job) =>
        string.Equals(job.Status, nameof(JobStatus.AwaitingApproval), StringComparison.Ordinal) &&
        HasAnyCapability(
            principal,
            WindowsAuthorizationCapability.Approver,
            WindowsAuthorizationCapability.Administrator);

    private static bool IsRequester(AuthenticatedPrincipal principal, IJobAuthorizationResource job) =>
        StringComparer.OrdinalIgnoreCase.Equals(principal.User.Value, job.RequestedBy);
}
