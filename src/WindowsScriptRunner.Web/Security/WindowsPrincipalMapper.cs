using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Principal;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Web.Security;

public interface IAuthenticatedPrincipalMapper
{
    AuthenticatedPrincipal Map(ClaimsPrincipal principal);
}

public sealed class AuthenticationMappingException : Exception
{
    public AuthenticationMappingException(string message)
        : base(message)
    {
    }
}

public sealed class WindowsPrincipalMapper : IAuthenticatedPrincipalMapper
{
    private const string StableIdentityPrefix = "sid:";
    private const string FallbackDisplayName = "Authenticated Windows user";
    private const int MaximumDisplayNameLength = 256;

    public AuthenticatedPrincipal Map(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (!OperatingSystem.IsWindows())
        {
            throw new AuthenticationMappingException("Windows SID authentication is unavailable on this host.");
        }

        return MapWindowsPrincipal(principal);
    }

    [SupportedOSPlatform("windows")]
    private static AuthenticatedPrincipal MapWindowsPrincipal(ClaimsPrincipal principal)
    {
        var authenticatedIdentities = principal.Identities
            .Where(identity => identity.IsAuthenticated)
            .ToArray();
        if (authenticatedIdentities.Length == 0)
        {
            throw new AuthenticationMappingException("An authenticated Windows principal is required.");
        }

        var userSid = ResolveUserSid(authenticatedIdentities);
        var user = new UserIdentity(StableIdentityPrefix + userSid.Value);
        var displayName = ResolveDisplayName(authenticatedIdentities);
        var groupSids = ResolveGroupSids(authenticatedIdentities, userSid);

        return new AuthenticatedPrincipal(user, displayName, groupSids);
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier ResolveUserSid(
        IReadOnlyCollection<ClaimsIdentity> authenticatedIdentities)
    {
        var primarySidClaims = authenticatedIdentities
            .SelectMany(identity => identity.FindAll(ClaimTypes.PrimarySid))
            .Select(claim => claim.Value)
            .ToArray();
        if (primarySidClaims.Length > 0)
        {
            if (primarySidClaims.Length != 1)
            {
                throw new AuthenticationMappingException("The authenticated principal has an ambiguous primary SID.");
            }

            return ParseSid(primarySidClaims[0], "The authenticated principal has an invalid primary SID.");
        }

        var userSidClaims = authenticatedIdentities
            .SelectMany(identity => identity.FindAll(ClaimTypes.Sid))
            .Select(claim => claim.Value)
            .ToArray();
        if (userSidClaims.Length > 0)
        {
            if (userSidClaims.Length != 1)
            {
                throw new AuthenticationMappingException("The authenticated principal has an ambiguous user SID.");
            }

            return ParseSid(userSidClaims[0], "The authenticated principal has an invalid user SID.");
        }

        var windowsIdentities = authenticatedIdentities
            .OfType<WindowsIdentity>()
            .Select(identity => identity.User)
            .Where(sid => sid is not null)
            .Cast<SecurityIdentifier>()
            .Distinct(SecurityIdentifierComparer.Instance)
            .ToArray();
        if (windowsIdentities.Length != 1)
        {
            throw new AuthenticationMappingException("The authenticated principal does not expose one Windows user SID.");
        }

        return ParseSid(windowsIdentities[0].Value, "The authenticated principal has an invalid Windows user SID.");
    }

    private static string ResolveDisplayName(IReadOnlyCollection<ClaimsIdentity> authenticatedIdentities)
    {
        var displayName = authenticatedIdentities
            .Select(identity => identity.Name)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return FallbackDisplayName;
        }

        var normalized = displayName.Trim();
        if (normalized.Length > MaximumDisplayNameLength || normalized.Any(char.IsControl))
        {
            throw new AuthenticationMappingException("The authenticated principal has an invalid display name.");
        }

        return normalized;
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlySet<string> ResolveGroupSids(
        IReadOnlyCollection<ClaimsIdentity> authenticatedIdentities,
        SecurityIdentifier userSid)
    {
        var groupSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claim in authenticatedIdentities
                     .SelectMany(identity => identity.FindAll(ClaimTypes.GroupSid)))
        {
            if (!TryParseSid(claim.Value, out var groupSid) ||
                SecurityIdentifierComparer.Instance.Equals(groupSid, userSid))
            {
                continue;
            }

            groupSids.Add(groupSid.Value);
        }

        foreach (var windowsIdentity in authenticatedIdentities.OfType<WindowsIdentity>())
        {
            foreach (var groupSid in windowsIdentity.Groups?.OfType<SecurityIdentifier>() ?? [])
            {
                if (!SecurityIdentifierComparer.Instance.Equals(groupSid, userSid))
                {
                    groupSids.Add(groupSid.Value);
                }
            }
        }

        return groupSids;
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier ParseSid(string? value, string errorMessage)
    {
        if (!TryParseSid(value, out var sid))
        {
            throw new AuthenticationMappingException(errorMessage);
        }

        if (sid.Value.StartsWith("S-1-5-32-", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthenticationMappingException("The authenticated principal SID identifies a built-in group, not a user.");
        }

        return sid;
    }

    [SupportedOSPlatform("windows")]
    private static bool TryParseSid(string? value, out SecurityIdentifier sid)
    {
        sid = null!;
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            return false;
        }

        var normalized = value.Trim();
        if (!normalized.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            sid = new SecurityIdentifier(normalized);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed class SecurityIdentifierComparer : IEqualityComparer<SecurityIdentifier>
    {
        public static SecurityIdentifierComparer Instance { get; } = new();

        public bool Equals(SecurityIdentifier? left, SecurityIdentifier? right) =>
            left is null ? right is null : right is not null && left.Equals(right);

        public int GetHashCode(SecurityIdentifier value) => value.GetHashCode();
    }
}
