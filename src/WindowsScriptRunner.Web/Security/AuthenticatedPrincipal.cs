using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Web.Security;

public sealed record AuthenticatedPrincipal(
    UserIdentity User,
    string DisplayName,
    IReadOnlySet<string> GroupSids);
