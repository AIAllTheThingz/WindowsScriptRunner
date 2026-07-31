using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Options;

namespace WindowsScriptRunner.Web.Security;

public sealed class WindowsAuthorizationOptions
{
    public const string SectionName = "WindowsAuthorization";

    public string[] OperatorGroupSids { get; set; } = [];
    public string[] ReportReaderGroupSids { get; set; } = [];
    public string[] ApproverGroupSids { get; set; } = [];
    public string[] AdministratorGroupSids { get; set; } = [];
}

public sealed class WindowsAuthorizationOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<WindowsAuthorizationOptions>
{
    public ValidateOptionsResult Validate(string? name, WindowsAuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!OperatingSystem.IsWindows())
        {
            return ValidateOptionsResult.Fail("Windows SID authorization is unavailable on this host.");
        }

        return ValidateWindowsOptions(options);
    }

    [SupportedOSPlatform("windows")]
    private ValidateOptionsResult ValidateWindowsOptions(WindowsAuthorizationOptions options)
    {
        var failures = new List<string>();
        options.OperatorGroupSids = Normalize(
            options.OperatorGroupSids,
            nameof(WindowsAuthorizationOptions.OperatorGroupSids),
            failures);
        options.ReportReaderGroupSids = Normalize(
            options.ReportReaderGroupSids,
            nameof(WindowsAuthorizationOptions.ReportReaderGroupSids),
            failures);
        options.ApproverGroupSids = Normalize(
            options.ApproverGroupSids,
            nameof(WindowsAuthorizationOptions.ApproverGroupSids),
            failures);
        options.AdministratorGroupSids = Normalize(
            options.AdministratorGroupSids,
            nameof(WindowsAuthorizationOptions.AdministratorGroupSids),
            failures);

        if (!environment.IsEnvironment("Testing") && options.AdministratorGroupSids.Length == 0)
        {
            failures.Add("At least one administrator group SID is required outside the test environment.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    [SupportedOSPlatform("windows")]
    private static string[] Normalize(
        IEnumerable<string>? values,
        string fieldName,
        ICollection<string> failures)
    {
        if (values is null)
        {
            failures.Add($"{fieldName} must be configured as a SID array.");
            return [];
        }

        var normalized = new List<string>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
            {
                failures.Add($"{fieldName} contains an empty or invalid SID value.");
                continue;
            }

            SecurityIdentifier sid;
            try
            {
                sid = new SecurityIdentifier(value.Trim());
            }
            catch (Exception exception) when (exception is ArgumentException or Win32Exception)
            {
                failures.Add($"{fieldName} contains an invalid SID value.");
                continue;
            }

            if (sid.IsWellKnown(WellKnownSidType.WorldSid) ||
                sid.IsWellKnown(WellKnownSidType.AnonymousSid) ||
                sid.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
                sid.IsWellKnown(WellKnownSidType.LocalServiceSid) ||
                sid.IsWellKnown(WellKnownSidType.NetworkServiceSid))
            {
                failures.Add($"{fieldName} cannot contain an anonymous, everyone, or service-account SID.");
                continue;
            }

            normalized.Add(sid.Value);
        }

        if (normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Count)
        {
            failures.Add($"{fieldName} cannot contain duplicate SID values.");
        }

        return normalized.ToArray();
    }
}
