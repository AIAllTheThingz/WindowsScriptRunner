using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using WindowsScriptRunner.Contracts.Jobs;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Web.Security;

namespace WindowsScriptRunner.SecurityTests;

public sealed class JobResourceAuthorizationTests
{
    private const string RequesterSid = "S-1-5-21-1001-1002-1003-1004";
    private const string OtherUserSid = "S-1-5-21-1001-1002-1003-1005";
    private const string OperatorGroupSid = "S-1-5-32-547";
    private const string ReportReaderGroupSid = "S-1-5-32-545";
    private const string ApproverGroupSid = "S-1-5-32-546";
    private const string AdministratorGroupSid = "S-1-5-32-544";

    [Fact]
    public async Task StableRequesterSidGrantsAccessButMatchingDisplayNameDoesNot()
    {
        var job = CreateJob(nameof(JobStatus.Submitted), $"sid:{RequesterSid}");

        var owner = await AuthorizeAsync(
            new ViewJobRequirement(),
            CreatePrincipal(RequesterSid, displayName: "CONTOSO\\Owner"),
            job);
        var sameDisplayNameDifferentSid = await AuthorizeAsync(
            new ViewJobRequirement(),
            CreatePrincipal(OtherUserSid, displayName: "CONTOSO\\Owner"),
            job);

        Assert.True(owner.HasSucceeded);
        Assert.False(sameDisplayNameDifferentSid.HasSucceeded);
        Assert.False(sameDisplayNameDifferentSid.HasFailed);
    }

    [Theory]
    [InlineData(ReportReaderGroupSid)]
    [InlineData(ApproverGroupSid)]
    [InlineData(AdministratorGroupSid)]
    public async Task ReportAccessGroupsCanViewAnyJobAndTypedReport(string groupSid)
    {
        var job = CreateJob(nameof(JobStatus.Completed), $"sid:{RequesterSid}");
        var principal = CreatePrincipal(OtherUserSid, [groupSid]);

        var viewJob = await AuthorizeAsync(new ViewJobRequirement(), principal, job);
        var viewReport = await AuthorizeAsync(new ViewReportRequirement(), principal, job);

        Assert.True(viewJob.HasSucceeded);
        Assert.True(viewReport.HasSucceeded);
    }

    [Fact]
    public async Task OperatorCannotViewAnotherRequestersJob()
    {
        var job = CreateJob(nameof(JobStatus.Completed), $"sid:{RequesterSid}");

        var result = await AuthorizeAsync(
            new ViewJobRequirement(),
            CreatePrincipal(OtherUserSid, [OperatorGroupSid]),
            job);

        Assert.False(result.HasSucceeded);
        Assert.False(result.HasFailed);
    }

    [Fact]
    public async Task OperatorCanModifyOnlyTheirOwnDraftAndAdministratorCannotCrossUserMutate()
    {
        var draft = CreateJob(nameof(JobStatus.Draft), $"sid:{RequesterSid}");

        var ownerOperator = await AuthorizeAsync(
            new ModifyDraftJobRequirement(),
            CreatePrincipal(RequesterSid, [OperatorGroupSid]),
            draft);
        var otherOperator = await AuthorizeAsync(
            new ModifyDraftJobRequirement(),
            CreatePrincipal(OtherUserSid, [OperatorGroupSid]),
            draft);
        var otherAdministrator = await AuthorizeAsync(
            new ModifyDraftJobRequirement(),
            CreatePrincipal(OtherUserSid, [AdministratorGroupSid]),
            draft);

        Assert.True(ownerOperator.HasSucceeded);
        Assert.False(otherOperator.HasSucceeded);
        Assert.False(otherAdministrator.HasSucceeded);
    }

    [Fact]
    public async Task DraftMutationRequiresDraftStatusEvenForTheRequester()
    {
        var submitted = CreateJob(nameof(JobStatus.Submitted), $"sid:{RequesterSid}");

        var result = await AuthorizeAsync(
            new ModifyDraftJobRequirement(),
            CreatePrincipal(RequesterSid, [OperatorGroupSid]),
            submitted);

        Assert.False(result.HasSucceeded);
    }

    [Theory]
    [InlineData(ApproverGroupSid)]
    [InlineData(AdministratorGroupSid)]
    public async Task OnlyApproversAndAdministratorsCanReviewAndDecideAwaitingApprovalJobs(string groupSid)
    {
        var awaitingApproval = CreateJob(nameof(JobStatus.AwaitingApproval), $"sid:{RequesterSid}");
        var principal = CreatePrincipal(OtherUserSid, [groupSid]);

        var review = await AuthorizeAsync(new ReviewApprovalRequirement(), principal, awaitingApproval);
        var decision = await AuthorizeAsync(new DecideApprovalRequirement(), principal, awaitingApproval);

        Assert.True(review.HasSucceeded);
        Assert.True(decision.HasSucceeded);
    }

    [Theory]
    [InlineData(OperatorGroupSid)]
    [InlineData(ReportReaderGroupSid)]
    public async Task NonApproverGroupsCannotReviewOrDecideApprovals(string groupSid)
    {
        var awaitingApproval = CreateJob(nameof(JobStatus.AwaitingApproval), $"sid:{RequesterSid}");
        var principal = CreatePrincipal(OtherUserSid, [groupSid]);

        var review = await AuthorizeAsync(new ReviewApprovalRequirement(), principal, awaitingApproval);
        var decision = await AuthorizeAsync(new DecideApprovalRequirement(), principal, awaitingApproval);

        Assert.False(review.HasSucceeded);
        Assert.False(decision.HasSucceeded);
        Assert.False(review.HasFailed);
        Assert.False(decision.HasFailed);
    }

    [Fact]
    public async Task ApprovalReviewAndDecisionAreDeniedOutsideAwaitingApprovalStatus()
    {
        var approved = CreateJob(nameof(JobStatus.Approved), $"sid:{RequesterSid}");
        var principal = CreatePrincipal(OtherUserSid, [AdministratorGroupSid]);

        var review = await AuthorizeAsync(new ReviewApprovalRequirement(), principal, approved);
        var decision = await AuthorizeAsync(new DecideApprovalRequirement(), principal, approved);

        Assert.False(review.HasSucceeded);
        Assert.False(decision.HasSucceeded);
    }

    [Fact]
    public async Task RoleClaimsAndAnonymousPrincipalsCannotBypassResourceAuthorization()
    {
        var job = CreateJob(nameof(JobStatus.Completed), $"sid:{RequesterSid}");
        var roleClaimPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, AdministratorGroupSid)],
            authenticationType: "SyntheticWindows"));
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var roleClaim = await AuthorizeAsync(new ViewJobRequirement(), roleClaimPrincipal, job);
        var anonymousAccess = await AuthorizeAsync(new ViewJobRequirement(), anonymous, job);

        Assert.False(roleClaim.HasSucceeded);
        Assert.False(anonymousAccess.HasSucceeded);
        Assert.False(roleClaim.HasFailed);
        Assert.False(anonymousAccess.HasFailed);
    }

    private static async Task<AuthorizationHandlerContext> AuthorizeAsync(
        IAuthorizationRequirement requirement,
        ClaimsPrincipal principal,
        JobDetailResponse job)
    {
        var context = new AuthorizationHandlerContext([requirement], principal, job);
        var handler = new JobResourceAuthorizationHandler(
            new WindowsPrincipalMapper(),
            Options.Create(new WindowsAuthorizationOptions
            {
                OperatorGroupSids = [OperatorGroupSid],
                ReportReaderGroupSids = [ReportReaderGroupSid],
                ApproverGroupSids = [ApproverGroupSid],
                AdministratorGroupSids = [AdministratorGroupSid],
            }));

        await handler.HandleAsync(context);

        return context;
    }

    private static ClaimsPrincipal CreatePrincipal(
        string userSid,
        IEnumerable<string>? groupSids = null,
        string displayName = "CONTOSO\\Synthetic")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.PrimarySid, userSid),
            new(ClaimTypes.Name, displayName),
        };
        claims.AddRange((groupSids ?? []).Select(groupSid => new Claim(ClaimTypes.GroupSid, groupSid)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "SyntheticWindows"));
    }

    private static JobDetailResponse CreateJob(string status, string requestedBy) =>
        new(
            Id: Guid.Parse("66666666-6666-6666-6666-666666666666"),
            ScriptDefinitionId: Guid.Parse("77777777-7777-7777-7777-777777777777"),
            ScriptVersionId: Guid.Parse("88888888-8888-8888-8888-888888888888"),
            RequestedPhase: nameof(ExecutionPhase.Execute),
            Status: status,
            RequestedBy: requestedBy,
            CreatedUtc: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            UpdatedUtc: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            SubmittedUtc: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            Description: null,
            ChangeReference: null,
            Targets: [],
            Parameters: [],
            Executions: [],
            Approvals: []);
}
