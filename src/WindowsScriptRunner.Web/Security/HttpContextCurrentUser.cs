using Microsoft.AspNetCore.Http;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Web.Security;

public sealed class HttpContextCurrentUser(
    IHttpContextAccessor httpContextAccessor,
    IAuthenticatedPrincipalMapper principalMapper) : ICurrentUser
{
    private UserIdentity? _user;

    public UserIdentity User
    {
        get
        {
            var httpContext = httpContextAccessor.HttpContext
                ?? throw new AuthenticationMappingException("A current HTTP context is required.");
            return _user ??= principalMapper.Map(httpContext.User).User;
        }
    }
}
