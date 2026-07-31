using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WindowsScriptRunner.Web.Security;

namespace WindowsScriptRunner.SecurityTests;

public sealed class WindowsIdentityAndAuthorizationTests
{
    private const string UserSid = "S-1-5-21-1001-1002-1003-1004";
    private const string OtherUserSid = "S-1-5-21-1001-1002-1003-1005";
    private const string OperatorGroupSid = "S-1-5-32-547";
    private const string ReportReaderGroupSid = "S-1-5-32-545";
    private const string ApproverGroupSid = "S-1-5-32-546";
    private const string AdministratorGroupSid = "S-1-5-32-544";

    [Fact]
    public void PrimarySidIsCanonicalStableIdentityAndDisplayNameRemainsSeparate()
    {
        var principal = CreatePrincipal(
            [
                new Claim(ClaimTypes.PrimarySid, UserSid),
                new Claim(ClaimTypes.Name, "CONTOSO\\Alice"),
                new Claim(ClaimTypes.GroupSid, AdministratorGroupSid),
            ]);

        var mapped = new WindowsPrincipalMapper().Map(principal);

        Assert.Equal($"sid:{UserSid}", mapped.User.Value);
        Assert.Equal("CONTOSO\\Alice", mapped.DisplayName);
        Assert.Contains(AdministratorGroupSid, mapped.GroupSids);
        Assert.DoesNotContain("CONTOSO\\Alice", mapped.GroupSids);
    }

    [Fact]
    public void ValidUserSidClaimIsUsedWhenPrimarySidIsUnavailable()
    {
        var principal = CreatePrincipal(
            [
                new Claim(ClaimTypes.Sid, UserSid),
                new Claim(ClaimTypes.Name, "CONTOSO\\Alice"),
            ]);

        var mapped = new WindowsPrincipalMapper().Map(principal);

        Assert.Equal($"sid:{UserSid}", mapped.User.Value);
    }

    [Fact]
    public void PrimarySidTakesPrecedenceOverAnotherValidUserSidClaim()
    {
        var principal = CreatePrincipal(
            [
                new Claim(ClaimTypes.PrimarySid, UserSid),
                new Claim(ClaimTypes.Sid, OtherUserSid),
            ]);

        var mapped = new WindowsPrincipalMapper().Map(principal);

        Assert.Equal($"sid:{UserSid}", mapped.User.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-sid")]
    [InlineData("S-1-5-21-1001-1002-1003-1004\u0001")]
    public void InvalidPrimarySidFailsClosed(string? sid)
    {
        Claim[] claims = sid is null
            ? [new Claim(ClaimTypes.Name, "CONTOSO\\Alice")]
            : [new Claim(ClaimTypes.PrimarySid, sid)];

        Assert.Throws<AuthenticationMappingException>(
            () => new WindowsPrincipalMapper().Map(CreatePrincipal(claims)));
    }

    [Fact]
    public void GroupSidCannotBeTreatedAsTheAuthenticatedUserSid()
    {
        var principal = CreatePrincipal(
            [new Claim(ClaimTypes.PrimarySid, AdministratorGroupSid)]);

        Assert.Throws<AuthenticationMappingException>(
            () => new WindowsPrincipalMapper().Map(principal));
    }

    [Fact]
    public void MultiplePrimarySidClaimsFailClosed()
    {
        var principal = CreatePrincipal(
            [
                new Claim(ClaimTypes.PrimarySid, UserSid),
                new Claim(ClaimTypes.PrimarySid, OtherUserSid),
            ]);

        Assert.Throws<AuthenticationMappingException>(
            () => new WindowsPrincipalMapper().Map(principal));
    }

    [Fact]
    public void NameAndRoleClaimsAreNeverUsedAsAStableIdentityFallback()
    {
        var principal = CreatePrincipal(
            [
                new Claim(ClaimTypes.Name, "CONTOSO\\Alice"),
                new Claim(ClaimTypes.Role, AdministratorGroupSid),
            ]);

        Assert.Throws<AuthenticationMappingException>(
            () => new WindowsPrincipalMapper().Map(principal));
    }

    [Fact]
    public void InvalidGroupClaimsDoNotGrantGroupMembership()
    {
        var principal = CreatePrincipal(
            [
                new Claim(ClaimTypes.PrimarySid, UserSid),
                new Claim(ClaimTypes.GroupSid, "not-a-sid"),
                new Claim(ClaimTypes.GroupSid, UserSid),
                new Claim(ClaimTypes.GroupSid, OperatorGroupSid),
            ]);

        var mapped = new WindowsPrincipalMapper().Map(principal);

        Assert.Equal([OperatorGroupSid], mapped.GroupSids);
    }

    [Fact]
    public void AuthenticatedWindowsTokenGroupsAreMergedAsCanonicalSidsWithoutTheUserSid()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AssertWindowsTokenGroupsAreMerged();
    }

    [SupportedOSPlatform("windows")]
    private static void AssertWindowsTokenGroupsAreMerged()
    {
        using var identity = WindowsIdentity.GetCurrent();
        Assert.NotNull(identity.User);
        var expectedGroupSids = (identity.Groups?.OfType<SecurityIdentifier>() ?? [])
            .Where(groupSid => !groupSid.Equals(identity.User))
            .Select(groupSid => groupSid.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var mapped = new WindowsPrincipalMapper().Map(new ClaimsPrincipal(identity));

        Assert.All(
            expectedGroupSids,
            groupSid => Assert.Contains(groupSid, mapped.GroupSids, StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(identity.User!.Value, mapped.GroupSids, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingHttpContextFailsClosedForCurrentUserResolution()
    {
        var currentUser = new HttpContextCurrentUser(
            new HttpContextAccessor(),
            new WindowsPrincipalMapper());

        Assert.Throws<AuthenticationMappingException>(() => _ = currentUser.User);
    }

    [Fact]
    public void CurrentUserUsesMappedSidInsteadOfTheDisplayName()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreatePrincipal(
                    [
                        new Claim(ClaimTypes.PrimarySid, UserSid),
                        new Claim(ClaimTypes.Name, "CONTOSO\\Alice"),
                    ]),
            },
        };
        var currentUser = new HttpContextCurrentUser(accessor, new WindowsPrincipalMapper());

        Assert.Equal($"sid:{UserSid}", currentUser.User.Value);
    }

    [Fact]
    public void ValidConfiguredGroupSidsAreAcceptedAndCanonicalized()
    {
        var options = CreateOptions();

        var result = CreateValidator(Environments.Production).Validate(null, options);

        Assert.True(result.Succeeded);
        Assert.Equal([OperatorGroupSid], options.OperatorGroupSids);
        Assert.Equal([ReportReaderGroupSid], options.ReportReaderGroupSids);
        Assert.Equal([ApproverGroupSid], options.ApproverGroupSids);
        Assert.Equal([AdministratorGroupSid], options.AdministratorGroupSids);
    }

    [Theory]
    [InlineData("S-1-1-0")]
    [InlineData("S-1-5-7")]
    [InlineData("not-a-sid")]
    [InlineData("\u0001")]
    public void InvalidOrBroadConfiguredGroupSidsFailValidation(string groupSid)
    {
        var options = CreateOptions();
        options.OperatorGroupSids = [groupSid];

        var result = CreateValidator(Environments.Production).Validate(null, options);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void WellKnownUserSidCannotBeUsedAsAConfiguredGroup()
    {
        var options = CreateOptions();
        options.OperatorGroupSids = ["S-1-5-18"];

        var result = CreateValidator(Environments.Production).Validate(null, options);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void DuplicateConfiguredGroupSidsFailValidation()
    {
        var options = CreateOptions();
        options.OperatorGroupSids = [OperatorGroupSid, OperatorGroupSid];

        var result = CreateValidator(Environments.Production).Validate(null, options);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void AdministratorGroupIsRequiredOutsideTheTestEnvironment()
    {
        var options = CreateOptions();
        options.AdministratorGroupSids = [];

        var result = CreateValidator(Environments.Production).Validate(null, options);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void EmptyAdministratorGroupsAreAllowedOnlyForTheTestEnvironment()
    {
        var options = CreateOptions();
        options.AdministratorGroupSids = [];

        var result = CreateValidator("Testing").Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task GroupAuthorizationUsesCanonicalGroupSidsAndConfiguredCapabilities()
    {
        var requirement = new WindowsGroupMembershipRequirement(
            new HashSet<WindowsAuthorizationCapability>
            {
                WindowsAuthorizationCapability.Operator,
                WindowsAuthorizationCapability.Administrator,
            });
        var context = new AuthorizationHandlerContext(
            [requirement],
            CreatePrincipal(
                [
                    new Claim(ClaimTypes.PrimarySid, UserSid),
                    new Claim(ClaimTypes.GroupSid, OperatorGroupSid),
                ]),
            null);
        var handler = new WindowsGroupMembershipHandler(
            new WindowsPrincipalMapper(),
            Options.Create(CreateOptions()));

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task InvalidGroupClaimsCannotSatisfyAuthorization()
    {
        var requirement = new WindowsGroupMembershipRequirement(
            new HashSet<WindowsAuthorizationCapability>
            {
                WindowsAuthorizationCapability.Operator,
            });
        var context = new AuthorizationHandlerContext(
            [requirement],
            CreatePrincipal(
                [
                    new Claim(ClaimTypes.PrimarySid, UserSid),
                    new Claim(ClaimTypes.GroupSid, "not-a-sid"),
                ]),
            null);
        var handler = new WindowsGroupMembershipHandler(
            new WindowsPrincipalMapper(),
            Options.Create(CreateOptions()));

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public void WebCompositionUsesNegotiateAuthenticationBeforeAuthorizationAndProtectsByDefault()
    {
        var program = ReadWebProgram();

        Assert.Contains("AddAuthentication(NegotiateDefaults.AuthenticationScheme)", program, StringComparison.Ordinal);
        Assert.Contains(".AddNegotiate()", program, StringComparison.Ordinal);
        Assert.Contains("options.FallbackPolicy", program, StringComparison.Ordinal);
        Assert.Contains("RequireAuthenticatedUser()", program, StringComparison.Ordinal);
        Assert.True(
            program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal) <
            program.IndexOf("app.UseAuthorization();", StringComparison.Ordinal));
    }

    [Fact]
    public void HealthEndpointsAndOnlyStaticAssetsHaveExplicitAnonymousAccess()
    {
        var program = ReadWebProgram();

        Assert.Contains("app.MapStaticAssets().AllowAnonymous();", program, StringComparison.Ordinal);
        Assert.All(
            new[] { "\"/health\"", "\"/health/live\"", "\"/health/ready\"" },
            route =>
            {
                var start = program.IndexOf(route, StringComparison.Ordinal);
                Assert.True(start >= 0, $"Expected health route {route}.");
                var end = program.IndexOf(".AllowAnonymous();", start, StringComparison.Ordinal);
                Assert.True(end >= start, $"Expected health route {route} to allow anonymous access.");
            });
        Assert.DoesNotContain("AllowAnonymous()", ReadWebSourceExcludingProgram(), StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionWebSourceContainsNoTestAuthenticationScheme()
    {
        var webSource = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(WebProjectDirectory, "*", SearchOption.AllDirectories)
                .Where(path =>
                    Path.GetExtension(path) is ".cs" or ".csproj" or ".json" &&
                    !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                    !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(File.ReadAllText));

        Assert.DoesNotContain("TestAuthentication", webSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SyntheticAuthentication", webSource, StringComparison.Ordinal);
    }

    private static string WebProjectDirectory => Path.Combine(
        FindRepositoryRoot(),
        "src",
        "WindowsScriptRunner.Web");

    private static ClaimsPrincipal CreatePrincipal(IEnumerable<Claim> claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "SyntheticWindows"));

    private static WindowsAuthorizationOptions CreateOptions() =>
        new()
        {
            OperatorGroupSids = [OperatorGroupSid],
            ReportReaderGroupSids = [ReportReaderGroupSid],
            ApproverGroupSids = [ApproverGroupSid],
            AdministratorGroupSids = [AdministratorGroupSid],
        };

    private static WindowsAuthorizationOptionsValidator CreateValidator(string environmentName) =>
        new(new TestHostEnvironment { EnvironmentName = environmentName });

    private static string ReadWebProgram() => File.ReadAllText(
        Path.Combine(WebProjectDirectory, "Program.cs"));

    private static string ReadWebSourceExcludingProgram() => string.Join(
        Environment.NewLine,
        Directory.EnumerateFiles(WebProjectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !Path.GetFileName(path).Equals("Program.cs", StringComparison.Ordinal) &&
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WindowsScriptRunner.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test execution directory.");
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "WindowsScriptRunner.SecurityTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
