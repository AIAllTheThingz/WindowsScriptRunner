using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace WindowsScriptRunner.Web.Security;

public static class AuthorizationPolicies
{
    public const string Authenticated = "WindowsScriptRunner.Authenticated";
    public const string JobOperator = "WindowsScriptRunner.JobOperator";
    public const string ReportReader = "WindowsScriptRunner.ReportReader";
    public const string Approver = "WindowsScriptRunner.Approver";
    public const string Administrator = "WindowsScriptRunner.Administrator";
}

public enum WindowsAuthorizationCapability
{
    Operator,
    ReportReader,
    Approver,
    Administrator,
}

public sealed record WindowsGroupMembershipRequirement(
    IReadOnlySet<WindowsAuthorizationCapability> Capabilities) : IAuthorizationRequirement;

public sealed class WindowsGroupMembershipHandler(
    IAuthenticatedPrincipalMapper principalMapper,
    IOptions<WindowsAuthorizationOptions> options)
    : AuthorizationHandler<WindowsGroupMembershipRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        WindowsGroupMembershipRequirement requirement)
    {
        AuthenticatedPrincipal principal;
        try
        {
            principal = principalMapper.Map(context.User);
        }
        catch (AuthenticationMappingException)
        {
            return Task.CompletedTask;
        }

        var configuredGroups = GetConfiguredGroupSids(options.Value, requirement.Capabilities);
        if (principal.GroupSids.Overlaps(configuredGroups))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    public static IReadOnlySet<string> GetConfiguredGroupSids(
        WindowsAuthorizationOptions options,
        IEnumerable<WindowsAuthorizationCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(capabilities);

        var groupSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var capability in capabilities)
        {
            foreach (var sid in capability switch
            {
                WindowsAuthorizationCapability.Operator => options.OperatorGroupSids,
                WindowsAuthorizationCapability.ReportReader => options.ReportReaderGroupSids,
                WindowsAuthorizationCapability.Approver => options.ApproverGroupSids,
                WindowsAuthorizationCapability.Administrator => options.AdministratorGroupSids,
                _ => [],
            })
            {
                groupSids.Add(sid);
            }
        }

        return groupSids;
    }
}
