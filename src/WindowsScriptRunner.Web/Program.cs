using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using WindowsScriptRunner.Application;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Infrastructure;
using WindowsScriptRunner.Web.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddSingleton<IAuthenticatedPrincipalMapper, WindowsPrincipalMapper>();
builder.Services.AddSingleton<IValidateOptions<WindowsAuthorizationOptions>, WindowsAuthorizationOptionsValidator>();
builder.Services.AddOptions<WindowsAuthorizationOptions>()
    .Bind(builder.Configuration.GetSection(WindowsAuthorizationOptions.SectionName))
    .ValidateOnStart();
builder.Services
    .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder(NegotiateDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy(
        AuthorizationPolicies.Authenticated,
        policy => policy.RequireAuthenticatedUser());
    options.AddPolicy(
        AuthorizationPolicies.JobOperator,
        policy => policy.AddRequirements(new WindowsGroupMembershipRequirement(
            new HashSet<WindowsAuthorizationCapability>
            {
                WindowsAuthorizationCapability.Operator,
                WindowsAuthorizationCapability.Administrator,
            })));
    options.AddPolicy(
        AuthorizationPolicies.ReportReader,
        policy => policy.AddRequirements(new WindowsGroupMembershipRequirement(
            new HashSet<WindowsAuthorizationCapability>
            {
                WindowsAuthorizationCapability.ReportReader,
                WindowsAuthorizationCapability.Approver,
                WindowsAuthorizationCapability.Administrator,
            })));
    options.AddPolicy(
        AuthorizationPolicies.Approver,
        policy => policy.AddRequirements(new WindowsGroupMembershipRequirement(
            new HashSet<WindowsAuthorizationCapability>
            {
                WindowsAuthorizationCapability.Approver,
                WindowsAuthorizationCapability.Administrator,
            })));
    options.AddPolicy(
        AuthorizationPolicies.Administrator,
        policy => policy.AddRequirements(new WindowsGroupMembershipRequirement(
            new HashSet<WindowsAuthorizationCapability>
            {
                WindowsAuthorizationCapability.Administrator,
            })));
});
builder.Services.AddSingleton<IAuthorizationHandler, WindowsGroupMembershipHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, JobResourceAuthorizationHandler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages().WithStaticAssets();
app.MapHealthChecks(
    "/health",
    new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("live"),
    })
    .AllowAnonymous();
app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("live"),
    })
    .AllowAnonymous();
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready"),
    })
    .AllowAnonymous();

app.Run();
