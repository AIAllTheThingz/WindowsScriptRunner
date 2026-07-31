using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Contracts.Jobs;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Reports;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Web.Pages.Reports.LocalHostInventory;
using WindowsScriptRunner.Web.Security;

namespace WindowsScriptRunner.SecurityTests;

public sealed class PortalWebFlowTests
{
    private const string RequesterSid = "S-1-5-21-1001-1002-1003-1004";
    private const string OtherUserSid = "S-1-5-21-1001-1002-1003-1005";
    private const string ApproverSid = "S-1-5-21-1001-1002-1003-1006";
    private const string SecondRequesterSid = "S-1-5-21-1001-1002-1003-1007";
    private const string OperatorGroupSid = "S-1-5-32-547";
    private const string ReportReaderGroupSid = "S-1-5-32-545";
    private const string ApproverGroupSid = "S-1-5-32-546";
    private const string AdministratorGroupSid = "S-1-5-32-544";

    [Fact]
    public async Task AnonymousPortalRequestsAreChallenged()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var protectedResponse = await client.GetAsync($"/Jobs/Details/{factory.State.Job.Id.Value:D}");

        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
    }

    [Fact]
    public async Task PortalRoleAndResourceRulesAreAppliedAtTheRenderedEndpoints()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        var jobPath = $"/Jobs/Details/{factory.State.Job.Id.Value:D}";
        var reportPath = $"/Reports/LocalHostInventory/Details/{factory.State.Report.Id.Value:D}";
        var reviewPath = $"/Approvals/Review/{factory.State.Job.Id.Value:D}";

        Assert.Equal(
            HttpStatusCode.OK,
            (await SendAsAsync(client, HttpMethod.Get, jobPath, RequesterSid)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendAsAsync(client, HttpMethod.Get, jobPath, OtherUserSid)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendAsAsync(client, HttpMethod.Get, reportPath, RequesterSid)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendAsAsync(client, HttpMethod.Get, reportPath, OtherUserSid)).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await SendAsAsync(
                client,
                HttpMethod.Get,
                jobPath,
                OtherUserSid,
                ReportReaderGroupSid)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendAsAsync(
                client,
                HttpMethod.Get,
                "/Approvals",
                OtherUserSid,
                ReportReaderGroupSid)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendAsAsync(
                client,
                HttpMethod.Get,
                "/Approvals",
                ApproverSid,
                ApproverGroupSid)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendAsAsync(
                client,
                HttpMethod.Get,
                reviewPath,
                ApproverSid,
                ApproverGroupSid)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendAsAsync(
                client,
                HttpMethod.Get,
                "/Administration",
                OtherUserSid,
                AdministratorGroupSid)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendAsAsync(
                client,
                HttpMethod.Get,
                "/Account/SignOut",
                RequesterSid)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendAsAsync(
                client,
                HttpMethod.Get,
                "/AccessDenied",
                RequesterSid)).StatusCode);
    }

    [Fact]
    public async Task AuthorizationFailuresRenderTheProtectedAccessDeniedPage()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await SendAsAsync(
            client,
            HttpMethod.Get,
            $"/Jobs/Details/{factory.State.Job.Id.Value:D}",
            OtherUserSid);
        var missing = await SendAsAsync(
            client,
            HttpMethod.Get,
            "/missing-route",
            RequesterSid);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(
            "Access denied",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task RenderedInventoryReportContainsOnlyTheTypedSafeSurface()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await SendAsAsync(
            client,
            HttpMethod.Get,
            $"/Reports/LocalHostInventory/Details/{factory.State.Report.Id.Value:D}",
            RequesterSid);
        var markup = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(factory.State.Report.Inventory.ComputerName, markup, StringComparison.Ordinal);
        Assert.Contains(factory.State.Report.Inventory.OsDescription, markup, StringComparison.Ordinal);
        Assert.Contains(factory.State.Report.Inventory.PowerShellVersion, markup, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.State.Report.WorkerNodeId.ToString(), markup, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.State.Report.LeaseId.ToString(), markup, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.State.Report.FencingToken.ToString(), markup, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.State.Report.PowerShellExecutionId.ToString(), markup, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.State.Report.Sha256, markup, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.State.SecureReference, markup, StringComparison.Ordinal);
        Assert.DoesNotContain("StandardOutput", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("StandardError", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkingDirectory", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Json", markup, StringComparison.Ordinal);
        Assert.Equal(
            new[]
            {
                "ReportId",
                "JobId",
                "PackageId",
                "PackageVersion",
                "CreatedUtc",
                "CollectedUtc",
                "ComputerName",
                "OsDescription",
                "OsVersion",
                "OsArchitecture",
                "PowerShellVersion",
            }.Order(StringComparer.Ordinal),
            typeof(LocalHostInventoryReportView)
                .GetProperties()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task InventoryListAndLookupReturnOnlyReportsTheAuthenticatedUserMayView()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateClient();
        var reportsPath = "/Reports/LocalHostInventory";
        var lookupPath = $"{reportsPath}?JobId={factory.State.Job.Id.Value:D}";

        var requesterList = await SendAsAsync(
            client,
            HttpMethod.Get,
            reportsPath,
            RequesterSid);
        var requesterLookup = await SendAsAsync(
            client,
            HttpMethod.Get,
            lookupPath,
            RequesterSid);
        var secondRequesterList = await SendAsAsync(
            client,
            HttpMethod.Get,
            reportsPath,
            SecondRequesterSid);
        var unprivilegedList = await SendAsAsync(
            client,
            HttpMethod.Get,
            reportsPath,
            OtherUserSid);
        var unprivilegedLookup = await SendAsAsync(
            client,
            HttpMethod.Get,
            lookupPath,
            OtherUserSid);

        Assert.Equal(HttpStatusCode.OK, requesterList.StatusCode);
        var requesterMarkup = await requesterList.Content.ReadAsStringAsync();
        Assert.Contains(
            factory.State.Report.Inventory.ComputerName,
            requesterMarkup,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            factory.State.OtherReport.Inventory.ComputerName,
            requesterMarkup,
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, requesterLookup.StatusCode);
        Assert.Contains(
            factory.State.Report.Inventory.ComputerName,
            await requesterLookup.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, secondRequesterList.StatusCode);
        var secondRequesterMarkup = await secondRequesterList.Content.ReadAsStringAsync();
        Assert.Contains(
            factory.State.OtherReport.Inventory.ComputerName,
            secondRequesterMarkup,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            factory.State.Report.Inventory.ComputerName,
            secondRequesterMarkup,
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, unprivilegedList.StatusCode);
        Assert.DoesNotContain(
            factory.State.Report.Inventory.ComputerName,
            await unprivilegedList.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(3, factory.State.RequesterReportListCount);
        Assert.Equal(HttpStatusCode.Forbidden, unprivilegedLookup.StatusCode);
    }

    [Fact]
    public async Task ApprovalPostRequiresAntiforgeryValidationBeforeAnyDecision()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await SendAsAsync(
            client,
            HttpMethod.Post,
            $"/Approvals/Review/{factory.State.Job.Id.Value:D}?handler=Approve",
            ApproverSid,
            ApproverGroupSid,
            new Dictionary<string, string>
            {
                ["jobId"] = Guid.NewGuid().ToString("D"),
                ["expectedFingerprint"] = PortalState.Fingerprint,
                ["Comment"] = "No antiforgery token.",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(JobStatus.AwaitingApproval, factory.State.Job.Status);
        Assert.Empty(factory.State.Job.Approvals);
        Assert.Equal(0, factory.State.AuditCount);
    }

    [Fact]
    public async Task ApprovalPostUsesTheAuthenticatedActorIgnoresForgedFieldsAndIsReplaySafe()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        var reviewPath = $"/Approvals/Review/{factory.State.Job.Id.Value:D}";
        var get = await SendAsAsync(
            client,
            HttpMethod.Get,
            reviewPath,
            ApproverSid,
            ApproverGroupSid);
        var antiforgeryToken = ExtractAntiforgeryToken(await get.Content.ReadAsStringAsync());
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiforgeryToken,
            ["jobId"] = Guid.NewGuid().ToString("D"),
            ["expectedFingerprint"] = PortalState.Fingerprint,
            ["Comment"] = "Approved from the reviewed page.",
            ["requestedBy"] = $"sid:{ApproverSid}",
            ["approver"] = $"sid:{RequesterSid}",
            ["approvalFingerprint"] = new string('a', 64),
            ["role"] = "Administrator",
        };

        var approved = await SendAsAsync(
            client,
            HttpMethod.Post,
            $"{reviewPath}?handler=Approve",
            ApproverSid,
            ApproverGroupSid,
            form);

        Assert.Equal(HttpStatusCode.Found, approved.StatusCode);
        Assert.Equal("/Approvals", approved.Headers.Location?.OriginalString);
        Assert.Equal(JobStatus.Approved, factory.State.Job.Status);
        var approval = Assert.Single(factory.State.Job.Approvals);
        Assert.Equal($"sid:{ApproverSid}", approval.Approver.Value);
        Assert.Equal(PortalState.Fingerprint, approval.ApprovalFingerprint);
        Assert.Equal(1, factory.State.AuditCount);

        var redirected = await SendAsAsync(
            client,
            HttpMethod.Get,
            "/Approvals",
            ApproverSid,
            ApproverGroupSid);
        Assert.Equal(HttpStatusCode.OK, redirected.StatusCode);

        var replay = await SendAsAsync(
            client,
            HttpMethod.Post,
            $"{reviewPath}?handler=Approve",
            ApproverSid,
            ApproverGroupSid,
            form);
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        Assert.Single(factory.State.Job.Approvals);
        Assert.Equal(1, factory.State.AuditCount);
    }

    [Fact]
    public async Task RequesterCannotApproveTheirOwnMediumRiskJob()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        var reviewPath = $"/Approvals/Review/{factory.State.Job.Id.Value:D}";
        var get = await SendAsAsync(
            client,
            HttpMethod.Get,
            reviewPath,
            RequesterSid,
            ApproverGroupSid);
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(
                await get.Content.ReadAsStringAsync()),
            ["jobId"] = factory.State.Job.Id.Value.ToString("D"),
            ["expectedFingerprint"] = PortalState.Fingerprint,
            ["Comment"] = "Self approval must fail.",
        };

        var response = await SendAsAsync(
            client,
            HttpMethod.Post,
            $"{reviewPath}?handler=Approve",
            RequesterSid,
            ApproverGroupSid,
            form);
        var markup = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The reviewed job changed or cannot be decided.", markup, StringComparison.Ordinal);
        Assert.Equal(JobStatus.AwaitingApproval, factory.State.Job.Status);
        Assert.Empty(factory.State.Job.Approvals);
        Assert.Equal(0, factory.State.AuditCount);
    }

    private static async Task<HttpResponseMessage> SendAsAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string userSid,
        string? groupSid = null,
        IReadOnlyDictionary<string, string>? form = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(TestWindowsAuthenticationHandler.UserSidHeader, userSid);
        if (groupSid is not null)
        {
            request.Headers.Add(TestWindowsAuthenticationHandler.GroupSidHeader, groupSid);
        }

        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }

        return await client.SendAsync(request);
    }

    private static string ExtractAntiforgeryToken(string markup)
    {
        var token = Regex.Match(
            markup,
            "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"[^>]*>",
            RegexOptions.CultureInvariant);
        if (!token.Success)
        {
            token = Regex.Match(
                markup,
                "<input[^>]*value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"[^>]*>",
                RegexOptions.CultureInvariant);
        }

        Assert.True(token.Success, "The approval form did not render an antiforgery token.");
        return WebUtility.HtmlDecode(token.Groups[1].Value);
    }

    private sealed class PortalWebApplicationFactory : WebApplicationFactory<WindowsPrincipalMapper>
    {
        internal PortalState State { get; } = PortalState.Create();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting(
                "ConnectionStrings:WindowsScriptRunner",
                "Server=(localdb)\\MSSQLLocalDB;Database=WindowsScriptRunnerPortalTests;Integrated Security=true;TrustServerCertificate=true");
            builder.UseSetting("WindowsAuthorization:OperatorGroupSids:0", OperatorGroupSid);
            builder.UseSetting("WindowsAuthorization:ReportReaderGroupSids:0", ReportReaderGroupSid);
            builder.UseSetting("WindowsAuthorization:ApproverGroupSids:0", ApproverGroupSid);
            builder.UseSetting("WindowsAuthorization:AdministratorGroupSids:0", AdministratorGroupSid);
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:WindowsScriptRunner"] =
                        "Server=(localdb)\\MSSQLLocalDB;Database=WindowsScriptRunnerPortalTests;Integrated Security=true;TrustServerCertificate=true",
                    ["WindowsAuthorization:OperatorGroupSids:0"] = OperatorGroupSid,
                    ["WindowsAuthorization:ReportReaderGroupSids:0"] = ReportReaderGroupSid,
                    ["WindowsAuthorization:ApproverGroupSids:0"] = ApproverGroupSid,
                    ["WindowsAuthorization:AdministratorGroupSids:0"] = AdministratorGroupSid,
                }));
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestWindowsAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestWindowsAuthenticationHandler.SchemeName;
                        options.DefaultForbidScheme = TestWindowsAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestWindowsAuthenticationHandler>(
                        TestWindowsAuthenticationHandler.SchemeName,
                        _ => { });
                services.RemoveAll<IAuthenticationSchemeProvider>();
                services.AddSingleton<IAuthenticationSchemeProvider, TestAuthenticationSchemeProvider>();
                services.PostConfigure<AuthorizationOptions>(options =>
                    options.FallbackPolicy = new AuthorizationPolicyBuilder(
                            TestWindowsAuthenticationHandler.SchemeName)
                        .RequireAuthenticatedUser()
                        .Build());

                services.RemoveAll<IJobRepository>();
                services.RemoveAll<IJobAuthorizationResourceReader>();
                services.RemoveAll<IScriptDefinitionRepository>();
                services.RemoveAll<IJobReportRepository>();
                services.RemoveAll<IAuditWriter>();
                services.RemoveAll<IUnitOfWork>();
                services.RemoveAll<IWorkerCoordinationClock>();
                services.RemoveAll<IJobFingerprintService>();
                services.RemoveAll<IAuthenticatedPrincipalMapper>();
                services.RemoveAll<IValidateOptions<WindowsAuthorizationOptions>>();
                services.AddSingleton(State);
                services.AddSingleton<IAuthenticatedPrincipalMapper, PortalPrincipalMapper>();
                services.AddScoped<IJobRepository, PortalJobRepository>();
                services.AddScoped<IJobAuthorizationResourceReader, PortalJobAuthorizationResourceReader>();
                services.AddScoped<IScriptDefinitionRepository, PortalScriptRepository>();
                services.AddScoped<IJobReportRepository, PortalReportRepository>();
                services.AddScoped<IAuditWriter, PortalAuditWriter>();
                services.AddScoped<IUnitOfWork, PortalUnitOfWork>();
                services.AddSingleton<IWorkerCoordinationClock, PortalClock>();
                services.AddSingleton<IJobFingerprintService, PortalFingerprintService>();
            });
        }
    }

    private sealed class TestWindowsAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(
            options,
            logger,
            encoder)
    {
        internal const string SchemeName = "SecurityTests.Windows";
        internal const string UserSidHeader = "X-Security-Test-User-Sid";
        internal const string GroupSidHeader = "X-Security-Test-Group-Sid";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userSid = Request.Headers[UserSidHeader].SingleOrDefault();
            if (string.IsNullOrWhiteSpace(userSid))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.PrimarySid, userSid),
                new(ClaimTypes.Name, "CONTOSO\\PortalTest"),
            };
            claims.AddRange(
                Request.Headers[GroupSidHeader]
                    .Where(value => value is not null)
                    .SelectMany(value => value!.Split('|', StringSplitOptions.RemoveEmptyEntries))
                    .Select(groupSid => new Claim(ClaimTypes.GroupSid, groupSid)));
            var identity = new ClaimsIdentity(claims, SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class TestAuthenticationSchemeProvider(
        IOptions<AuthenticationOptions> options) : AuthenticationSchemeProvider(options)
    {
        public override Task<IEnumerable<AuthenticationScheme>> GetRequestHandlerSchemesAsync() =>
            Task.FromResult<IEnumerable<AuthenticationScheme>>([]);
    }

    private sealed class PortalPrincipalMapper : IAuthenticatedPrincipalMapper
    {
        public AuthenticatedPrincipal Map(ClaimsPrincipal principal)
        {
            ArgumentNullException.ThrowIfNull(principal);
            var identity = principal.Identities.SingleOrDefault(item => item.IsAuthenticated)
                ?? throw new AuthenticationMappingException("The test principal must be authenticated.");
            var userSid = identity.FindFirst(ClaimTypes.PrimarySid)?.Value
                ?? throw new AuthenticationMappingException("The test principal must contain a primary SID.");
            var groupSids = identity.FindAll(ClaimTypes.GroupSid)
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new AuthenticatedPrincipal(
                new UserIdentity($"sid:{userSid}"),
                "Portal test user",
                groupSids);
        }
    }

    private sealed class PortalState
    {
        internal const string Fingerprint = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        private PortalState(
            Job job,
            ScriptDefinition script,
            JobReport report,
            JobReport otherReport,
            UserIdentity otherRequester,
            string secureReference)
        {
            Job = job;
            Script = script;
            Report = report;
            OtherReport = otherReport;
            OtherRequester = otherRequester;
            SecureReference = secureReference;
        }

        internal Job Job { get; }
        internal ScriptDefinition Script { get; }
        internal JobReport Report { get; }
        internal JobReport OtherReport { get; }
        internal UserIdentity OtherRequester { get; }
        internal string SecureReference { get; }
        internal int AuditCount { get; set; }
        internal int RequesterReportListCount { get; set; }

        internal static PortalState Create()
        {
            var started = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
            var requester = new UserIdentity($"sid:{RequesterSid}");
            var otherRequester = new UserIdentity($"sid:{SecondRequesterSid}");
            var system = new UserIdentity("system:portal-test");
            var parameter = new ScriptParameterDefinition(
                ScriptParameterDefinitionId.New(),
                "Credential",
                "Credential",
                null,
                ScriptParameterType.SecureReference,
                isRequired: false,
                defaultValue: null,
                allowedValues: null,
                isSensitive: true);
            var version = new ScriptVersion(
                ScriptVersionId.New(),
                ScriptVersionNumber.Parse("1.0.0"),
                "scripts/PortalTest.ps1",
                new string('c', 64),
                "abcdef1",
                "7.4.0",
                30,
                [ExecutionPhase.DryRun, ExecutionPhase.Execute],
                [ReportFormat.Json],
                started,
                requester);
            version.AddParameterDefinition(parameter);
            version.Publish();
            var script = ScriptDefinition.Create(
                ScriptDefinitionId.New(),
                new ScriptName("portal.test"),
                "Portal Test",
                "Test script for portal boundary coverage.",
                RiskLevel.Medium,
                requester,
                started);
            script.AddVersion(version, started);

            var secureReference = CredentialReferenceId.New().ToString();
            var job = Job.CreateDraft(
                JobId.New(),
                script.Id,
                version.Id,
                ExecutionPhase.Execute,
                requester,
                started,
                "Portal approval request");
            job.AddTarget(new TargetName("portal-target"), requester, started.AddMinutes(1));
            job.SetParameterValue("Credential", secureReference, requester, started.AddMinutes(2));
            job.Submit(script, requester, started.AddMinutes(3));
            job.MarkValidated(system, started.AddMinutes(4));
            job.QueueDryRun(system, started.AddMinutes(5));
            var dryRunCredentials = job.AcquireWorkLease(
                JobLeaseId.New(),
                WorkerNodeId.New(),
                JobWorkKind.DryRun,
                56,
                system,
                started.AddMinutes(6),
                started.AddMinutes(16)).Credentials;
            job.StartDryRun(dryRunCredentials, system, started.AddMinutes(7));
            job.CompleteDryRun(dryRunCredentials, system, started.AddMinutes(8));
            job.RequireApproval(system, started.AddMinutes(9));

            var report = JobReport.CreateLocalHostInventory(
                job.Id,
                script.Id,
                version.Id,
                WorkerNodeId.New(),
                JobLeaseId.New(),
                fencingToken: 57,
                Guid.Parse("56565656-5656-5656-5656-565656565656"),
                started.AddMinutes(10),
                started.AddMinutes(10),
                new LocalHostInventoryReportPayload(
                    "PORTAL-01",
                    "Microsoft Windows 11 Enterprise",
                    "10.0.26100",
                    InventoryOsArchitecture.X64,
                    "7.4.0"),
                new string('d', 64));
            var otherReport = JobReport.CreateLocalHostInventory(
                new JobId(Guid.Parse("57575757-5757-5757-5757-575757575757")),
                script.Id,
                version.Id,
                WorkerNodeId.New(),
                JobLeaseId.New(),
                fencingToken: 58,
                Guid.Parse("58585858-5858-5858-5858-585858585858"),
                started.AddMinutes(11),
                started.AddMinutes(11),
                new LocalHostInventoryReportPayload(
                    "PORTAL-02",
                    "Microsoft Windows Server 2025",
                    "10.0.26100",
                    InventoryOsArchitecture.X64,
                    "7.4.0"),
                new string('e', 64));
            return new PortalState(
                job,
                script,
                report,
                otherReport,
                otherRequester,
                secureReference);
        }
    }

    private sealed class PortalJobRepository(PortalState state) : IJobRepository
    {
        public Task<Job?> GetByIdAsync(JobId id, CancellationToken cancellationToken) =>
            Task.FromResult<Job?>(state.Job.Id == id ? state.Job : null);

        public Task<IReadOnlyList<Job>> ListAwaitingApprovalAsync(
            int maximumCount,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Job>>(
                state.Job.Status == JobStatus.AwaitingApproval && maximumCount > 0
                    ? [state.Job]
                    : []);

        public Task<bool> ExistsAsync(JobId id, CancellationToken cancellationToken) =>
            Task.FromResult(state.Job.Id == id);

        public Task AddAsync(Job job, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateAsync(Job job, CancellationToken cancellationToken)
        {
            Assert.Same(state.Job, job);
            return Task.CompletedTask;
        }

        public Task UpdateLeaseAsync(Job job, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> TryRefreshLeaseAsync(
            JobId jobId,
            JobLeaseCredentials credentials,
            CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class PortalScriptRepository(PortalState state) : IScriptDefinitionRepository
    {
        public Task<ScriptDefinition?> GetByIdAsync(
            ScriptDefinitionId id,
            CancellationToken cancellationToken) =>
            Task.FromResult<ScriptDefinition?>(state.Script.Id == id ? state.Script : null);

        public Task AddAsync(ScriptDefinition definition, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateAsync(ScriptDefinition definition, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class PortalJobAuthorizationResourceReader(PortalState state) :
        IJobAuthorizationResourceReader
    {
        public Task<IReadOnlyList<JobAuthorizationResourceResponse>> ListAsync(
            IReadOnlyCollection<JobId> jobIds,
            CancellationToken cancellationToken)
        {
            var resources = new List<JobAuthorizationResourceResponse>();
            if (jobIds.Contains(state.Job.Id))
            {
                resources.Add(new JobAuthorizationResourceResponse(
                    state.Job.Id.Value,
                    state.Job.Status.ToString(),
                    state.Job.RequestedBy.Value));
            }

            if (jobIds.Contains(state.OtherReport.JobId))
            {
                resources.Add(new JobAuthorizationResourceResponse(
                    state.OtherReport.JobId.Value,
                    nameof(JobStatus.Completed),
                    state.OtherRequester.Value));
            }

            return Task.FromResult<IReadOnlyList<JobAuthorizationResourceResponse>>(resources);
        }
    }

    private sealed class PortalReportRepository(PortalState state) : IJobReportRepository
    {
        public Task<JobReport?> GetByIdAsync(JobReportId id, CancellationToken cancellationToken) =>
            Task.FromResult<JobReport?>(
                state.Report.Id == id
                    ? state.Report
                    : state.OtherReport.Id == id ? state.OtherReport : null);

        public Task<JobReport?> GetByJobIdAsync(JobId id, CancellationToken cancellationToken) =>
            Task.FromResult<JobReport?>(
                state.Report.JobId == id
                    ? state.Report
                    : state.OtherReport.JobId == id ? state.OtherReport : null);

        public Task<IReadOnlyList<JobReport>> ListLocalHostInventoryAsync(
            int maximumCount,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobReport>>(
                maximumCount > 0 ? [state.Report, state.OtherReport] : []);

        public Task<IReadOnlyList<JobReport>> ListLocalHostInventoryForRequesterAsync(
            UserIdentity requester,
            int maximumCount,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(requester);
            state.RequesterReportListCount++;
            IReadOnlyList<JobReport> reports = maximumCount > 0
                ? requester == state.Job.RequestedBy
                    ? [state.Report]
                    : requester == state.OtherRequester ? [state.OtherReport] : []
                : [];
            return Task.FromResult(reports);
        }

        public Task AddAsync(JobReport report, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class PortalAuditWriter(PortalState state) : IAuditWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            state.AuditCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class PortalUnitOfWork : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class PortalClock : IWorkerCoordinationClock
    {
        public Task<DateTimeOffset> GetUtcNowAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DateTimeOffset(2026, 8, 1, 13, 0, 0, TimeSpan.Zero));
    }

    private sealed class PortalFingerprintService : IJobFingerprintService
    {
        public Task<string> CreateFingerprintAsync(Job job, CancellationToken cancellationToken) =>
            Task.FromResult(PortalState.Fingerprint);

        public bool IsExpectedFingerprintCurrent(string? expectedFingerprint, string currentFingerprint) =>
            string.Equals(expectedFingerprint, currentFingerprint, StringComparison.Ordinal);
    }
}
